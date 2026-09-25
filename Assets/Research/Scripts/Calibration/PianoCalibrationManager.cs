using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class PianoCalibrationManager : MonoBehaviour
    {
        public enum CaptureState { Idle, ArmedA, ArmedB, ArmedC, SamplingA, SamplingB, SamplingC }
        public const float MoveWaitSeconds = 3f;
        public const float SampleSeconds = 0.4f;
        public const int MinimumValidSamples = 10;
        public const float MaximumSampleDeviationMeters = 0.015f;
        const int MaximumSamples = 128;
        struct CaptureSample
        {
            public Vector3 WorldPosition;
            public bool PoseValid;
            public bool HandTracked;
            public double TimeSeconds;
        }

        XRHandPoseProvider m_Hands;
        readonly Vector3[] m_Points = new Vector3[3];
        readonly CaptureSample[] m_Samples = new CaptureSample[MaximumSamples];
        readonly float[] m_X = new float[MaximumSamples];
        readonly float[] m_Y = new float[MaximumSamples];
        readonly float[] m_Z = new float[MaximumSamples];
        int m_SampleCount;
        long m_LastSampleCallback;
        double m_ArmedAt;
        int m_CountdownNumber;
        int m_PointCount;

        public event Action<PianoCalibrationData> CalibrationChanged;
        public PianoCalibrationData LastCalibrationAttempt { get; private set; } = new PianoCalibrationData();
        public PianoCalibrationData LastValidCalibration { get; private set; }
        public PianoCalibrationData CurrentAppliedCalibration { get; private set; }
        public PianoCalibrationData Current => CurrentAppliedCalibration;
        public bool IsCapturing { get; private set; }
        public bool UseLeftHand { get; private set; }
        public string CaptureHandName => UseLeftHand ? "Left" : "Right";
        public CaptureState State { get; private set; }
        public int ValidSampleCount => m_SampleCount;
        public int CapturedPointCount => m_PointCount;
        public string StatusText { get; private set; } = "Not calibrated";
        public string PersistentPath => Path.Combine(Application.persistentDataPath, "PianoResearch", "piano_calibration.json");

        public void Initialize(XRHandPoseProvider hands)
        {
            m_Hands = hands;
            Load();
        }

        public void StartCalibration() => Begin();
        public void RegisterNextPoint() => CaptureNextPoint();
        public void ToggleCaptureHand() { if (State == CaptureState.Idle) UseLeftHand = !UseLeftHand; }
        public void SelectLeftHand() { if (State == CaptureState.Idle) UseLeftHand = true; }
        public void SelectRightHand() { if (State == CaptureState.Idle) UseLeftHand = false; }

        public void CaptureA()
        {
            Arm(0);
        }

        public void CaptureB()
        {
            if (State != CaptureState.Idle) return;
            if (m_PointCount != 1)
            {
                StatusText = "Capture A first.";
                return;
            }
            Arm(1);
        }

        public void CaptureC()
        {
            if (State != CaptureState.Idle) return;
            if (m_PointCount != 2)
            {
                StatusText = "Capture A and B first.";
                return;
            }
            Arm(2);
        }

        public void CancelCapture()
        {
            if (State == CaptureState.Idle) return;
            State = CaptureState.Idle;
            Array.Clear(m_Samples, 0, m_SampleCount);
            m_SampleCount = 0;
            m_LastSampleCallback = 0;
            IsCapturing = m_PointCount > 0 && m_PointCount < 3;
            StatusText = "Capture cancelled";
        }

        void OnDisable() => CancelCapture();
        void OnApplicationQuit() => CancelCapture();

        void Update() => AdvanceCapture(Time.unscaledTimeAsDouble);

        void Arm(int point)
        {
            if (State != CaptureState.Idle) return;
            State = (CaptureState)((int)CaptureState.ArmedA + point);
            m_ArmedAt = Time.unscaledTimeAsDouble;
            m_CountdownNumber = 3;
            m_SampleCount = 0;
            m_LastSampleCallback = m_Hands != null && m_Hands.LatestDisplayFrame != null
                ? m_Hands.LatestDisplayFrame.CallbackIndex : 0;
            IsCapturing = true;
            StatusText = $"Calibration: {(char)('A' + point)} - {PointInstruction(point)} - Capture in 3";
        }

        static string PointInstruction(int point) => point == 0 ? "C4 front-left corner" :
            point == 1 ? "C5 front-left corner" : "C4 back-left corner";

        // Called by Update; a monotonic time argument keeps the countdown independently testable.
        void AdvanceCapture(double now)
        {
            if (State == CaptureState.Idle) return;
            var latest = m_Hands != null ? m_Hands.LatestDisplayFrame : null;
            if (latest != null && latest.CallbackIndex != m_LastSampleCallback &&
                !(UseLeftHand ? latest.LeftTracked : latest.RightTracked))
            {
                FailCapture("Hand not tracked");
                return;
            }
            var state = State;
            var point = state >= CaptureState.SamplingA ? (int)state - (int)CaptureState.SamplingA :
                (int)state - (int)CaptureState.ArmedA;
            var elapsed = now - m_ArmedAt;
            if (state < CaptureState.SamplingA)
            {
                if (elapsed < MoveWaitSeconds)
                {
                    var remaining = Mathf.Clamp(Mathf.CeilToInt((float)(MoveWaitSeconds - elapsed)), 1, 3);
                    if (remaining != m_CountdownNumber)
                    {
                        m_CountdownNumber = remaining;
                        StatusText = $"Calibration: {(char)('A' + point)} - {PointInstruction(point)} - Capture in {remaining}";
                    }
                    return;
                }
                State = (CaptureState)((int)CaptureState.SamplingA + point);
                StatusText = $"Calibration: {(char)('A' + point)} - Hold still";
            }
            SampleCurrentFrame(now);
            if (State == CaptureState.Idle) return;
            if (elapsed >= MoveWaitSeconds + SampleSeconds)
                FinishCapture(point);
        }

        void SampleCurrentFrame(double now)
        {
            var frame = m_Hands != null ? m_Hands.LatestDisplayFrame : null;
            if (frame == null || frame.CallbackIndex == m_LastSampleCallback) return;
            m_LastSampleCallback = frame.CallbackIndex;
            var tracked = UseLeftHand ? frame.LeftTracked : frame.RightTracked;
            if (!tracked) { FailCapture("Hand not tracked"); return; }
            var joint = frame.FindJoint(UseLeftHand, XRHandJointID.IndexTip);
            if (!joint.PoseValid) return;
            if (!m_Hands.TryGetWorldJoint(UseLeftHand, XRHandJointID.IndexTip, out var pose) ||
                !IsFinite(pose.position)) return;
            if (m_SampleCount == MaximumSamples) { FailCapture("Too many samples"); return; }
            m_Samples[m_SampleCount++] = new CaptureSample
            {
                WorldPosition = pose.position, PoseValid = joint.PoseValid,
                HandTracked = tracked, TimeSeconds = now
            };
        }

        void FinishCapture(int point)
        {
            if (m_SampleCount < MinimumValidSamples)
            {
                FailCapture(m_SampleCount == 0 ? "IndexTip invalid" : "Not enough samples");
                return;
            }
            for (var i = 0; i < m_SampleCount; ++i)
            {
                var sample = m_Samples[i];
                if (!sample.HandTracked || !sample.PoseValid || !IsFinite(sample.WorldPosition))
                { FailCapture("IndexTip invalid"); return; }
                m_X[i] = sample.WorldPosition.x;
                m_Y[i] = sample.WorldPosition.y;
                m_Z[i] = sample.WorldPosition.z;
            }
            Array.Sort(m_X, 0, m_SampleCount);
            Array.Sort(m_Y, 0, m_SampleCount);
            Array.Sort(m_Z, 0, m_SampleCount);
            var median = new Vector3(Median(m_X), Median(m_Y), Median(m_Z));
            for (var i = 0; i < m_SampleCount; ++i)
                if (Vector3.Distance(m_Samples[i].WorldPosition, median) > MaximumSampleDeviationMeters)
                { FailCapture("Hand moved too much"); return; }
            PianoCalibrationData candidate = null;
            if (point == 2 && !PianoCalibrationMath.TryCalculate(m_Points[0], m_Points[1], median, out candidate))
            {
                LastCalibrationAttempt = candidate;
                FailCapture(candidate.validationMessage);
                return;
            }
            m_Points[point] = median;
            m_PointCount = point + 1;
            State = CaptureState.Idle;
            Array.Clear(m_Samples, 0, m_SampleCount);
            m_SampleCount = 0;
            StatusText = $"{(char)('A' + point)} captured";
            if (m_PointCount < 3) return;
            IsCapturing = false;
            LastCalibrationAttempt = candidate;
            LastValidCalibration = candidate;
            CurrentAppliedCalibration = candidate;
            StatusText = CalibrationSummary(candidate);
            CalibrationChanged?.Invoke(candidate);
        }

        float Median(float[] sorted)
        {
            var middle = m_SampleCount / 2;
            return (m_SampleCount & 1) != 0 ? sorted[middle] :
                (sorted[middle - 1] + sorted[middle]) * 0.5f;
        }

        static string CalibrationSummary(PianoCalibrationData calibration) =>
            "Calibration complete\n" +
            $"Width scale: {calibration.scaleX.ToString("F2", CultureInfo.InvariantCulture)}\n" +
            $"Depth scale: {calibration.scaleZ.ToString("F2", CultureInfo.InvariantCulture)}\n" +
            $"Measured octave span: {(calibration.physicalOctaveSpanMeters * 1000f).ToString("F0", CultureInfo.InvariantCulture)} mm\n" +
            $"Measured white-key depth: {(calibration.physicalWhiteKeyDepthMeters * 1000f).ToString("F0", CultureInfo.InvariantCulture)} mm";

        void FailCapture(string reason)
        {
            State = CaptureState.Idle;
            Array.Clear(m_Samples, 0, m_SampleCount);
            m_SampleCount = 0;
            IsCapturing = m_PointCount > 0 && m_PointCount < 3;
            StatusText = reason;
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        public void SaveCalibration() => Save();
        public void LoadCalibration() => Load();

        public void Begin()
        {
            CancelCapture();
            m_PointCount = 0;
            IsCapturing = true;
            StatusText = $"Capture A ({PointInstruction(0)})";
        }

        public bool CaptureNextPoint()
        {
            if (!IsCapturing || m_Hands == null) return false;
            if (!m_Hands.TryGetWorldJoint(UseLeftHand, XRHandJointID.IndexTip, out var pose))
            {
                StatusText = "Index tip is not tracked.";
                return false;
            }
            m_Points[m_PointCount++] = pose.position;
            if (m_PointCount < 3)
            {
                StatusText = m_PointCount == 1
                    ? $"Capture B ({PointInstruction(1)})"
                    : $"Capture C ({PointInstruction(2)})";
                return true;
            }
            IsCapturing = false;
            if (PianoCalibrationMath.TryCalculate(m_Points[0], m_Points[1], m_Points[2], out var result))
            {
                LastCalibrationAttempt = result;
                LastValidCalibration = result;
                CurrentAppliedCalibration = result;
                StatusText = CalibrationSummary(result);
                CalibrationChanged?.Invoke(CurrentAppliedCalibration);
                return true;
            }
            m_PointCount = 2;
            IsCapturing = true;
            LastCalibrationAttempt = result;
            StatusText = result.validationMessage;
            return false;
        }

        public bool Save()
        {
            if (CurrentAppliedCalibration == null || !CurrentAppliedCalibration.valid)
            {
                StatusText = "No valid calibration to save.";
                return false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PersistentPath));
                File.WriteAllText(PersistentPath, JsonUtility.ToJson(CurrentAppliedCalibration, true));
                StatusText = "Saved";
                return true;
            }
            catch (Exception exception)
            {
                StatusText = "Save failed: " + exception.Message;
                return false;
            }
        }

        public bool Load()
        {
            try
            {
                if (!File.Exists(PersistentPath))
                {
                    RecordAttemptFailure("No saved calibration");
                    StatusText = LastCalibrationAttempt.validationMessage;
                    return false;
                }
                var loaded = JsonUtility.FromJson<PianoCalibrationData>(File.ReadAllText(PersistentPath));
                if (loaded == null || !loaded.valid)
                {
                    RecordAttemptFailure("Saved calibration is invalid");
                    StatusText = LastCalibrationAttempt.validationMessage;
                    return false;
                }
                if (!PianoCalibrationMath.IsSupportedFormatVersion(loaded.formatVersion))
                {
                    RecordAttemptFailure($"Unsupported calibration format version: {loaded.formatVersion}");
                    StatusText = $"Unsupported calibration format version: {loaded.formatVersion}";
                    return false;
                }
                if (loaded.formatVersion == 0)
                    loaded.formatVersion = PianoCalibrationMath.CurrentFormatVersion;
                if (loaded.calibrationPointDefinitionVersion == 0)
                {
                    loaded.scaleX = 1f;
                    loaded.scaleZ = 1f;
                    loaded.physicalOctaveSpanMeters = VirtualPianoKeyboard.BaseOctaveSpanMeters;
                    loaded.physicalWhiteKeyDepthMeters = VirtualPianoKeyboard.BaseWhiteKeyDepthMeters;
                    loaded.baseOctaveSpanMeters = VirtualPianoKeyboard.BaseOctaveSpanMeters;
                    loaded.baseWhiteKeyDepthMeters = VirtualPianoKeyboard.BaseWhiteKeyDepthMeters;
                }
                else if (loaded.calibrationPointDefinitionVersion != PianoCalibrationMath.CurrentPointDefinitionVersion)
                {
                    RecordAttemptFailure($"Unsupported calibration point definition version: {loaded.calibrationPointDefinitionVersion}");
                    StatusText = LastCalibrationAttempt.validationMessage;
                    return false;
                }
                else if (!PianoCalibrationMath.TryGetScale(loaded, out _, out _, out var scaleError))
                {
                    RecordAttemptFailure(scaleError);
                    StatusText = LastCalibrationAttempt.validationMessage;
                    return false;
                }
                LastCalibrationAttempt = loaded;
                LastValidCalibration = loaded;
                CurrentAppliedCalibration = loaded;
                StatusText = "Loaded";
                CalibrationChanged?.Invoke(CurrentAppliedCalibration);
                return true;
            }
            catch (Exception exception)
            {
                RecordAttemptFailure("Load failed: " + exception.Message);
                StatusText = LastCalibrationAttempt.validationMessage;
                return false;
            }
        }

        public void ClearCalibration()
        {
            CancelCapture();
            IsCapturing = false;
            m_PointCount = 0;
            LastCalibrationAttempt = new PianoCalibrationData();
            LastValidCalibration = null;
            CurrentAppliedCalibration = null;
            StatusText = "Not calibrated";
        }

        void RecordAttemptFailure(string message)
        {
            LastCalibrationAttempt = new PianoCalibrationData { validationMessage = message };
        }
    }
}
