using System;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    [Serializable]
    public sealed class PianoCalibrationData
    {
        public int formatVersion = PianoCalibrationMath.CurrentFormatVersion;
        public float scaleX = 1f;
        public float scaleZ = 1f;
        public float physicalOctaveSpanMeters;
        public float physicalWhiteKeyDepthMeters;
        public float baseOctaveSpanMeters = VirtualPianoKeyboard.BaseOctaveSpanMeters;
        public float baseWhiteKeyDepthMeters = VirtualPianoKeyboard.BaseWhiteKeyDepthMeters;
        public int calibrationPointDefinitionVersion;
        public float scaleY => 1f;
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
        public float minimumPointDistanceMeters;
        public float axisAngleDegrees;
        public float qualityScore;
        public string qualityLabel = "Unavailable";
        public string validationMessage = "Not calibrated";
    }

    public static class PianoCalibrationMath
    {
        public const int CurrentFormatVersion = 1;
        public const int CurrentPointDefinitionVersion = 1;
        public const float MinimumPointDistanceMeters = 0.05f;
        public const float MinimumAxisAngleDegrees = 10f;
        public const float GoodPointDistanceMeters = 0.20f;
        public const float MinimumScaleX = 0.50f;
        public const float MaximumScaleX = 1.50f;
        public const float MinimumScaleZ = 0.60f;
        public const float MaximumScaleZ = 1.40f;

        public static bool IsSupportedFormatVersion(int version) =>
            version == 0 || version == CurrentFormatVersion;

        public static bool TryValidateScale(float scaleX, float scaleZ, out string message)
        {
            if (!IsFinite(scaleX) || !IsFinite(scaleZ))
            {
                message = "Calibration scale must be finite.";
                return false;
            }
            if (scaleX < MinimumScaleX || scaleX > MaximumScaleX ||
                scaleZ < MinimumScaleZ || scaleZ > MaximumScaleZ)
            {
                message = $"Calibration scale is outside the supported range (X {MinimumScaleX:F2}-{MaximumScaleX:F2}, Z {MinimumScaleZ:F2}-{MaximumScaleZ:F2}).";
                return false;
            }
            message = "Valid";
            return true;
        }

        public static bool TryGetScale(PianoCalibrationData calibration, out float scaleX,
            out float scaleZ, out string message)
        {
            scaleX = 1f;
            scaleZ = 1f;
            if (calibration == null)
            {
                message = "Calibration is missing.";
                return false;
            }
            if (calibration.calibrationPointDefinitionVersion == 0)
            {
                message = "Legacy calibration uses base geometry scale.";
                return true;
            }
            if (calibration.calibrationPointDefinitionVersion != CurrentPointDefinitionVersion)
            {
                message = $"Unsupported calibration point definition version: {calibration.calibrationPointDefinitionVersion}";
                return false;
            }
            if (!TryValidateScale(calibration.scaleX, calibration.scaleZ, out message))
                return false;
            if (!IsFinite(calibration.physicalOctaveSpanMeters) || calibration.physicalOctaveSpanMeters <= 0f ||
                !IsFinite(calibration.physicalWhiteKeyDepthMeters) || calibration.physicalWhiteKeyDepthMeters <= 0f ||
                !IsFinite(calibration.baseOctaveSpanMeters) || calibration.baseOctaveSpanMeters <= 0f ||
                !IsFinite(calibration.baseWhiteKeyDepthMeters) || calibration.baseWhiteKeyDepthMeters <= 0f)
            {
                message = "Calibration dimensions must be finite and positive.";
                return false;
            }
            scaleX = calibration.scaleX;
            scaleZ = calibration.scaleZ;
            message = "Valid";
            return true;
        }

        public static bool TryCalculate(Vector3 a, Vector3 b, Vector3 c, out PianoCalibrationData data)
        {
            data = new PianoCalibrationData { pointA = a, pointB = b, pointC = c };
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
            {
                data.validationMessage = "Calibration points must be finite.";
                return false;
            }
            var rightRaw = b - a;
            var depthRaw = c - a;
            var minimumDistance = Mathf.Min(rightRaw.magnitude, depthRaw.magnitude, (c - b).magnitude);
            data.minimumPointDistanceMeters = minimumDistance;
            if (minimumDistance < MinimumPointDistanceMeters)
            {
                data.validationMessage = "Calibration points are too close.";
                return false;
            }

            var angle = Vector3.Angle(rightRaw, depthRaw);
            data.axisAngleDegrees = angle;
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
            var physicalOctaveSpan = rightRaw.magnitude;
            var physicalWhiteKeyDepth = depthOrthogonal.magnitude;
            var scaleX = physicalOctaveSpan / VirtualPianoKeyboard.BaseOctaveSpanMeters;
            var scaleZ = physicalWhiteKeyDepth / VirtualPianoKeyboard.BaseWhiteKeyDepthMeters;
            data.physicalOctaveSpanMeters = physicalOctaveSpan;
            data.physicalWhiteKeyDepthMeters = physicalWhiteKeyDepth;
            data.baseOctaveSpanMeters = VirtualPianoKeyboard.BaseOctaveSpanMeters;
            data.baseWhiteKeyDepthMeters = VirtualPianoKeyboard.BaseWhiteKeyDepthMeters;
            data.calibrationPointDefinitionVersion = CurrentPointDefinitionVersion;
            data.scaleX = scaleX;
            data.scaleZ = scaleZ;
            if (!TryValidateScale(scaleX, scaleZ, out var scaleMessage))
            {
                data.validationMessage = scaleMessage;
                return false;
            }
            var normal = Vector3.Cross(depth, right).normalized;
            if (Vector3.Dot(normal, Vector3.up) < 0f)
            {
                data.validationMessage = "Calibration orientation is mirrored; capture B to the right and C toward the rear.";
                return false;
            }
            var separationQuality = Mathf.InverseLerp(MinimumPointDistanceMeters, GoodPointDistanceMeters, minimumDistance);
            var angleQuality = 1f - Mathf.Abs(90f - angle) / (90f - MinimumAxisAngleDegrees);
            data.qualityScore = Mathf.Clamp01(Mathf.Min(separationQuality, angleQuality));
            data.qualityLabel = data.qualityScore >= 0.75f ? "Good" : data.qualityScore >= 0.4f ? "Acceptable" : "Low";
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

        static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

}
