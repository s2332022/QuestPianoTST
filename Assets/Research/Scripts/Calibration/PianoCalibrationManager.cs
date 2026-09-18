using System;
using System.IO;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class PianoCalibrationManager : MonoBehaviour
    {
        XRHandPoseProvider m_Hands;
        readonly Vector3[] m_Points = new Vector3[3];
        int m_PointCount;

        public event Action<PianoCalibrationData> CalibrationChanged;
        public PianoCalibrationData LastCalibrationAttempt { get; private set; } = new PianoCalibrationData();
        public PianoCalibrationData LastValidCalibration { get; private set; }
        public PianoCalibrationData CurrentAppliedCalibration { get; private set; }
        public PianoCalibrationData Current => CurrentAppliedCalibration;
        public bool IsCapturing { get; private set; }
        public bool UseLeftHand { get; private set; } = true;
        public string CaptureHandName => UseLeftHand ? "Left" : "Right";
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
        public void ToggleCaptureHand() => UseLeftHand = !UseLeftHand;
        public void SelectLeftHand() => UseLeftHand = true;
        public void SelectRightHand() => UseLeftHand = false;

        public void CaptureA()
        {
            Begin();
            CaptureNextPoint();
        }

        public void CaptureB()
        {
            if (m_PointCount != 1)
            {
                StatusText = "Capture A first.";
                return;
            }
            CaptureNextPoint();
        }

        public void CaptureC()
        {
            if (m_PointCount != 2)
            {
                StatusText = "Capture A and B first.";
                return;
            }
            CaptureNextPoint();
        }

        public void SaveCalibration() => Save();
        public void LoadCalibration() => Load();

        public void Begin()
        {
            m_PointCount = 0;
            IsCapturing = true;
            StatusText = "Capture A (reference white key, front-left)";
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
                StatusText = m_PointCount == 1 ? "Capture B (keyboard right)" : "Capture C (keyboard depth)";
                return true;
            }
            IsCapturing = false;
            if (PianoCalibrationMath.TryCalculate(m_Points[0], m_Points[1], m_Points[2], out var result))
            {
                LastCalibrationAttempt = result;
                LastValidCalibration = result;
                CurrentAppliedCalibration = result;
                StatusText = "Valid (not saved)";
                CalibrationChanged?.Invoke(CurrentAppliedCalibration);
                return true;
            }
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
