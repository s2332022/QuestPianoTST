using System;
using System.IO;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    [Serializable]
    public sealed class PianoCalibrationData
    {
        public string calibrationId = string.Empty;
        public string createdUtc = string.Empty;
        public bool valid;
        public Vector3 pointA;
        public Vector3 pointB;
        public Vector3 pointC;
        public Vector3 origin;
        public Vector3 rightAxis;
        public Vector3 depthAxis;
        public Vector3 normal;
        public Quaternion rotation = Quaternion.identity;
        public string validationMessage = "Not calibrated";
    }

    public static class PianoCalibrationMath
    {
        public const float MinimumPointDistanceMeters = 0.05f;
        public const float MinimumAxisAngleDegrees = 10f;

        public static bool TryCalculate(Vector3 a, Vector3 b, Vector3 c, out PianoCalibrationData data)
        {
            data = new PianoCalibrationData { pointA = a, pointB = b, pointC = c };
            var rightRaw = b - a;
            var depthRaw = c - a;
            if (rightRaw.magnitude < MinimumPointDistanceMeters || depthRaw.magnitude < MinimumPointDistanceMeters ||
                (c - b).magnitude < MinimumPointDistanceMeters)
            {
                data.validationMessage = "Calibration points are too close.";
                return false;
            }

            var angle = Vector3.Angle(rightRaw, depthRaw);
            if (angle < MinimumAxisAngleDegrees || angle > 180f - MinimumAxisAngleDegrees)
            {
                data.validationMessage = "Right and depth axes are nearly parallel.";
                return false;
            }

            var right = rightRaw.normalized;
            var depthOrthogonal = depthRaw - Vector3.Project(depthRaw, right);
            if (depthOrthogonal.sqrMagnitude < 0.000001f)
            {
                data.validationMessage = "Depth axis is invalid.";
                return false;
            }
            var depth = depthOrthogonal.normalized;
            var normal = Vector3.Cross(depth, right).normalized;
            data.calibrationId = Guid.NewGuid().ToString("N");
            data.createdUtc = DateTime.UtcNow.ToString("O");
            data.valid = true;
            data.origin = a;
            data.rightAxis = right;
            data.depthAxis = depth;
            data.normal = normal;
            data.rotation = Quaternion.LookRotation(depth, normal);
            data.validationMessage = "Valid";
            return true;
        }
    }

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
