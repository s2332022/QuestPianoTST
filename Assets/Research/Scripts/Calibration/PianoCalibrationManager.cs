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
        public PianoCalibrationData Current { get; private set; } = new PianoCalibrationData();
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
                Current = result;
                StatusText = "Valid (not saved)";
                CalibrationChanged?.Invoke(Current);
                return true;
            }
            Current = result;
            StatusText = result.validationMessage;
            return false;
        }

        public bool Save()
        {
            if (Current == null || !Current.valid) { StatusText = "No valid calibration to save."; return false; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PersistentPath));
                File.WriteAllText(PersistentPath, JsonUtility.ToJson(Current, true));
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
                if (!File.Exists(PersistentPath)) { StatusText = "No saved calibration"; return false; }
                var loaded = JsonUtility.FromJson<PianoCalibrationData>(File.ReadAllText(PersistentPath));
                if (loaded == null || !loaded.valid) { StatusText = "Saved calibration is invalid"; return false; }
                Current = loaded;
                StatusText = "Loaded";
                CalibrationChanged?.Invoke(Current);
                return true;
            }
            catch (Exception exception)
            {
                StatusText = "Load failed: " + exception.Message;
                return false;
            }
        }
    }
}
