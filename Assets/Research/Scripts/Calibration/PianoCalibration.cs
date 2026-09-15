using System;
using UnityEngine;

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

}
