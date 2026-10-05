using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class PianoFoundationTests
    {
        const float PositionTolerance = 1e-5f;
        static readonly Vector3 GeometryOffset = VirtualPianoKeyboard.BaseGeometryOriginOffsetMeters;
        static readonly Vector3 WhiteKeyCenterSpacing =
            new Vector3(VirtualPianoKeyboard.BaseWhiteKeyPitchMeters, 0f, 0f);

        static GameObject CreateKeyboard(out VirtualPianoKeyboard keyboard)
        {
            var host = new GameObject("Virtual Piano Keyboard Test Host");
            Assert.That(host.activeSelf, Is.True);
            Assert.That(host.activeInHierarchy, Is.True);
            keyboard = host.AddComponent<VirtualPianoKeyboard>();
            Assert.That(keyboard, Is.Not.Null);
            var awake = typeof(VirtualPianoKeyboard).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            var buildKeyboard = typeof(VirtualPianoKeyboard).GetMethod("BuildKeyboard", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(awake, Is.Not.Null);
            Assert.That(buildKeyboard, Is.Not.Null);
            Assert.That(keyboard.KeyboardRoot, Is.Null);
            var component = keyboard;
            Assert.DoesNotThrow(() => awake.Invoke(component, null));
            Assert.That(keyboard.KeyboardRoot, Is.Not.Null);
            Assert.That(host.transform.childCount, Is.EqualTo(1));
            var geometry = keyboard.KeyboardRoot.Find("Keyboard Geometry");
            Assert.That(geometry, Is.Not.Null);
            Assert.That(geometry.childCount, Is.EqualTo(88));
            Assert.That(geometry.Find("Key 60 White"), Is.Not.Null);
            Assert.That(geometry.Find("Key 72 White"), Is.Not.Null);
            Assert.That(geometry.Find("Key 61 Black"), Is.Not.Null);
            return host;
        }

        static Transform FindKey(Transform root, int note, bool black = false) =>
            root.Find($"Keyboard Geometry/Key {note} {(black ? "Black" : "White")}");

        static void AssertVectorClose(Vector3 expected, Vector3 actual)
        {
            Assert.That((actual - expected).magnitude, Is.LessThanOrEqualTo(PositionTolerance));
        }

        static PianoCalibrationData CreateCalibration(Vector3 origin, Quaternion rotation)
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                origin, origin + rotation * Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                origin + rotation * Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters,
                out var data), Is.True);
            return data;
        }

        static PianoCalibrationData CreateScaledCalibration(Vector3 origin, Quaternion rotation,
            float scaleX, float scaleZ)
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                origin,
                origin + rotation * Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters * scaleX,
                origin + rotation * Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * scaleZ,
                out var data), Is.True, data != null ? data.validationMessage : "No calibration result");
            return data;
        }

        static Vector3 TopFaceCorner(Transform key, float xSign, float zSign) =>
            key.TransformPoint(new Vector3(xSign * 0.5f, 0.5f, zSign * 0.5f));

        static GameObject CreateCalibrationFixture(out PianoCalibrationManager manager, out XRHandPoseProvider hands)
        {
            var host = new GameObject("Piano Calibration Test Host");
            hands = host.AddComponent<XRHandPoseProvider>();
            manager = host.AddComponent<PianoCalibrationManager>();
            SetHandPoint(hands, Vector3.zero);
            SetPrivateField(manager, "m_Hands", hands);
            return host;
        }

        static void SetHandPoint(XRHandPoseProvider hands, Vector3 position)
        {
            var frame = new HandPoseFrame(1);
            frame.LeftJoints[0] = new HandJointPose
            {
                JointId = XRHandJointID.IndexTip,
                PoseValid = true,
                TrackingState = XRHandJointTrackingState.Pose,
                Pose = new Pose(position, Quaternion.identity)
            };
            frame.RightJoints[0] = frame.LeftJoints[0];
            frame.LeftTracked = true;
            frame.RightTracked = true;
            SetPrivateField(hands, "m_Display", frame);
        }

        static bool CaptureAttempt(PianoCalibrationManager manager, XRHandPoseProvider hands,
            Vector3 a, Vector3 b, Vector3 c)
        {
            manager.Begin();
            SetHandPoint(hands, a);
            Assert.That(manager.CaptureNextPoint(), Is.True);
            SetHandPoint(hands, b);
            Assert.That(manager.CaptureNextPoint(), Is.True);
            SetHandPoint(hands, c);
            return manager.CaptureNextPoint();
        }

        static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}");
            field.SetValue(target, value);
        }

        sealed class PersistentCalibrationFileScope : System.IDisposable
        {
            readonly string m_Path;
            readonly bool m_Existed;
            readonly byte[] m_Original;

            public PersistentCalibrationFileScope(PianoCalibrationManager manager)
            {
                m_Path = manager.PersistentPath;
                m_Existed = File.Exists(m_Path);
                m_Original = m_Existed ? File.ReadAllBytes(m_Path) : null;
            }

            public void WriteText(string text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_Path));
                File.WriteAllText(m_Path, text);
            }

            public void Dispose()
            {
                if (m_Existed)
                {
                    File.WriteAllBytes(m_Path, m_Original);
                    return;
                }
                if (File.Exists(m_Path)) File.Delete(m_Path);
            }
        }

        static PianoCalibrationData WriteSessionSnapshot(PianoCalibrationManager manager, string sessionPath)
        {
            Directory.CreateDirectory(sessionPath);
            var host = new GameObject("Session Snapshot Test Host");
            try
            {
                var recorder = host.AddComponent<SynchronizedSessionRecorder>();
                SetPrivateField(recorder, "m_Calibration", manager);
                SetPrivateField(recorder, "<SessionPath>k__BackingField", sessionPath);
                var method = typeof(SynchronizedSessionRecorder).GetMethod(
                    "WriteCalibrationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                Assert.DoesNotThrow(() => method.Invoke(recorder, null));
                return JsonUtility.FromJson<PianoCalibrationData>(
                    File.ReadAllText(Path.Combine(sessionPath, "piano_calibration.json")));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Calibration_ProducesOrthogonalAxes()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters, Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters, out var data), Is.True);
            Assert.That(Vector3.Dot(data.rightAxis, data.depthAxis), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(Vector3.Dot(data.normal, data.rightAxis), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(Vector3.Dot(data.normal, data.depthAxis), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(data.rightAxis.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(data.depthAxis.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(data.normal.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That((data.rotation * Vector3.right - data.rightAxis).magnitude, Is.LessThan(1e-5f));
            Assert.That((data.rotation * Vector3.up - data.normal).magnitude, Is.LessThan(1e-5f));
            Assert.That((data.rotation * Vector3.forward - data.depthAxis).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void Calibration_RejectsNearParallelAxes()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right, Vector3.right * 2f, out var data), Is.False);
            Assert.That(data.valid, Is.False);
        }

        [TestCase(0.01f, 0f, 0f, 0.2f)]
        [TestCase(0.2f, 0f, 0.01f, 0f)]
        [TestCase(0.2f, 0f, 0.21f, 0f)]
        public void Calibration_RejectsEveryTooClosePointPair(float bx, float bz, float cx, float cz)
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, new Vector3(bx, 0f, bz), new Vector3(cx, 0f, cz), out var data), Is.False);
            Assert.That(data.valid, Is.False);
            Assert.That(data.validationMessage, Does.Contain("too close"));
        }

        [Test]
        public void Calibration_RejectsMirroredOrientation()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                Vector3.back * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters, out var data), Is.False);
            Assert.That(data.validationMessage, Does.Contain("mirrored"));
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.left * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters, out data), Is.False);
            Assert.That(data.validationMessage, Does.Contain("mirrored"));
        }

        [Test]
        public void Calibration_UsesAAsOriginWithoutScale()
        {
            var a = new Vector3(1.2f, 0.7f, -0.4f);
            Assert.That(PianoCalibrationMath.TryCalculate(
                a, a + Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters, a + Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters, out var data), Is.True);
            Assert.That(data.origin, Is.EqualTo(a));
            Assert.That(data.scaleX, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(data.scaleZ, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(data.scaleY, Is.EqualTo(1f));
        }

        [Test]
        public void Calibration_RejectsNonFinitePoints()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right, new Vector3(float.NaN, 0f, 1f), out var data), Is.False);
            Assert.That(data.validationMessage, Does.Contain("finite"));
        }

        [Test]
        public void Calibration_QualityRewardsSeparatedPerpendicularPoints()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                Vector3.forward * 0.20f, out var good), Is.True);
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero,
                Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters * PianoCalibrationMath.MinimumScaleX,
                new Vector3(VirtualPianoKeyboard.BaseOctaveSpanMeters * PianoCalibrationMath.MinimumScaleX * 0.5f,
                    0f, VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * PianoCalibrationMath.MinimumScaleZ),
                out var low), Is.True);
            Assert.That(good.qualityScore, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(good.qualityLabel, Is.EqualTo("Good"));
            Assert.That(low.qualityScore, Is.LessThan(good.qualityScore));
        }

        [Test]
        public void Calibration_BaseDimensionsProduceUnitScales()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(Vector3.zero,
                Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters, out var data), Is.True);
            Assert.That(data.scaleX, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(data.scaleZ, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(data.scaleY, Is.EqualTo(1f));
            Assert.That(data.physicalOctaveSpanMeters,
                Is.EqualTo(VirtualPianoKeyboard.BaseWhiteKeyPitchMeters * 7f).Within(1e-5f));
            Assert.That(data.baseOctaveSpanMeters, Is.EqualTo(VirtualPianoKeyboard.BaseOctaveSpanMeters));
            Assert.That(data.baseWhiteKeyDepthMeters, Is.EqualTo(VirtualPianoKeyboard.BaseWhiteKeyDepthMeters));
        }

        [TestCase(0.8f)]
        [TestCase(1.2f)]
        public void Calibration_HorizontalScaleMatchesMeasuredOctaveSpan(float expectedScaleX)
        {
            var data = CreateScaledCalibration(Vector3.zero, Quaternion.identity, expectedScaleX, 1f);
            Assert.That(data.scaleX, Is.EqualTo(expectedScaleX).Within(1e-5f));
            Assert.That(data.scaleZ, Is.EqualTo(1f).Within(1e-5f));
        }

        [TestCase(0.8f)]
        [TestCase(1.2f)]
        public void Calibration_DepthScaleMatchesMeasuredWhiteKeyDepth(float expectedScaleZ)
        {
            var data = CreateScaledCalibration(Vector3.zero, Quaternion.identity, 1f, expectedScaleZ);
            Assert.That(data.scaleX, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(data.scaleZ, Is.EqualTo(expectedScaleZ).Within(1e-5f));
        }

        [TestCase(0f, 1f)]
        [TestCase(-0.1f, 1f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(1f, -0.1f)]
        [TestCase(1f, float.NaN)]
        [TestCase(1f, float.NegativeInfinity)]
        public void Calibration_RejectsNonFiniteOrNonPositiveScale(float scaleX, float scaleZ)
        {
            Assert.That(PianoCalibrationMath.TryValidateScale(scaleX, scaleZ, out var message), Is.False);
            Assert.That(message, Is.Not.EqualTo("Valid"));
        }

        [TestCase(0.49f, 1f)]
        [TestCase(1.51f, 1f)]
        [TestCase(1f, 0.59f)]
        [TestCase(1f, 1.41f)]
        public void Calibration_RejectsScaleOutsideSafetyRange(float scaleX, float scaleZ)
        {
            Assert.That(PianoCalibrationMath.TryValidateScale(scaleX, scaleZ, out _), Is.False);
        }

        [Test]
        public void Calibration_FormatVersionIsSerializedAndValidated()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters, Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters, out var data), Is.True);
            var json = JsonUtility.ToJson(data);
            Assert.That(json, Does.Contain("\"formatVersion\":1"));
            Assert.That(json, Does.Contain("\"scaleX\""));
            Assert.That(json, Does.Contain("\"scaleZ\""));
            Assert.That(json, Does.Contain("\"physicalOctaveSpanMeters\""));
            Assert.That(json, Does.Contain("\"physicalWhiteKeyDepthMeters\""));
            Assert.That(json, Does.Contain("\"baseOctaveSpanMeters\""));
            Assert.That(json, Does.Contain("\"baseWhiteKeyDepthMeters\""));
            Assert.That(json, Does.Contain("\"calibrationPointDefinitionVersion\":1"));
            Assert.That(json, Does.Not.Contain("\"scaleY\""));
            Assert.That(PianoCalibrationMath.IsSupportedFormatVersion(0), Is.True, "legacy unversioned data");
            Assert.That(PianoCalibrationMath.IsSupportedFormatVersion(1), Is.True);
            Assert.That(PianoCalibrationMath.IsSupportedFormatVersion(2), Is.False);
        }

        [Test]
        public void CalibrationManager_FirstSuccessSetsLastValid()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                Assert.That(manager.LastValidCalibration, Is.SameAs(manager.LastCalibrationAttempt));
                Assert.That(manager.LastValidCalibration.valid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_FirstSuccessSetsCurrentApplied()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                Assert.That(manager.CurrentAppliedCalibration, Is.SameAs(manager.LastValidCalibration));
                Assert.That(manager.Current, Is.SameAs(manager.CurrentAppliedCalibration));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_SuccessRaisesCalibrationChangedOnce()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                var count = 0;
                PianoCalibrationData received = null;
                manager.CalibrationChanged += value => { count++; received = value; };
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                Assert.That(count, Is.EqualTo(1));
                Assert.That(received, Is.SameAs(manager.CurrentAppliedCalibration));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_FailedAttemptPreservesLastValid()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                var valid = manager.LastValidCalibration;
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                    Vector3.forward * 0.01f), Is.False);
                Assert.That(manager.LastValidCalibration, Is.SameAs(valid));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_FailedAttemptPreservesCurrentApplied()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                var applied = manager.CurrentAppliedCalibration;
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                    Vector3.forward * 0.01f), Is.False);
                Assert.That(manager.CurrentAppliedCalibration, Is.SameAs(applied));
                Assert.That(manager.Current, Is.SameAs(applied));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_FailedAttemptDoesNotRaiseCalibrationChanged()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                var count = 0;
                manager.CalibrationChanged += _ => count++;
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                    Vector3.forward * 0.01f), Is.False);
                Assert.That(count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_FailedAttemptDoesNotChangeKeyboardRoot()
        {
            var managerHost = CreateCalibrationFixture(out var manager, out var hands);
            var keyboardHost = CreateKeyboard(out var keyboard);
            try
            {
                manager.CalibrationChanged += keyboard.ApplyCalibration;
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero,
                    Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters * 0.8f,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * 1.2f), Is.True);
                var position = keyboard.KeyboardRoot.position;
                var rotation = keyboard.KeyboardRoot.rotation;
                var geometryPosition = keyboard.KeyboardGeometry.localPosition;
                var geometryScale = keyboard.KeyboardGeometry.localScale;
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                    Vector3.forward * 0.01f), Is.False);
                AssertVectorClose(position, keyboard.KeyboardRoot.position);
                Assert.That(Quaternion.Angle(rotation, keyboard.KeyboardRoot.rotation),
                    Is.LessThanOrEqualTo(PositionTolerance));
                AssertVectorClose(Vector3.one, keyboard.KeyboardRoot.localScale);
                AssertVectorClose(geometryPosition, keyboard.KeyboardGeometry.localPosition);
                AssertVectorClose(geometryScale, keyboard.KeyboardGeometry.localScale);
            }
            finally
            {
                Object.DestroyImmediate(keyboardHost);
                Object.DestroyImmediate(managerHost);
            }
        }

        [Test]
        public void CalibrationManager_ScaleFailureKeepsAppliedScaleAndAllowsRecapturingLastPoint()
        {
            var managerHost = CreateCalibrationFixture(out var manager, out var hands);
            var keyboardHost = CreateKeyboard(out var keyboard);
            try
            {
                manager.CalibrationChanged += keyboard.ApplyCalibration;
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero,
                    Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                var lastValid = manager.LastValidCalibration;
                var geometryPosition = keyboard.KeyboardGeometry.localPosition;
                var geometryScale = keyboard.KeyboardGeometry.localScale;

                var origin = new Vector3(1f, 2f, 3f);
                Assert.That(CaptureAttempt(manager, hands, origin,
                    origin + Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    origin + Vector3.forward * 0.08f), Is.False);
                Assert.That(manager.LastValidCalibration, Is.SameAs(lastValid));
                Assert.That(manager.CurrentAppliedCalibration, Is.SameAs(lastValid));
                Assert.That(manager.CapturedPointCount, Is.EqualTo(2));
                Assert.That(manager.IsCapturing, Is.True);
                AssertVectorClose(geometryPosition, keyboard.KeyboardGeometry.localPosition);
                AssertVectorClose(geometryScale, keyboard.KeyboardGeometry.localScale);

                SetHandPoint(hands, origin + Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters);
                Assert.That(manager.CaptureNextPoint(), Is.True);
                Assert.That(manager.LastValidCalibration, Is.SameAs(manager.CurrentAppliedCalibration));
                Assert.That(manager.CurrentAppliedCalibration.scaleZ, Is.EqualTo(1f).Within(1e-5f));
                AssertVectorClose(origin, keyboard.KeyboardRoot.position);
            }
            finally
            {
                Object.DestroyImmediate(keyboardHost);
                Object.DestroyImmediate(managerHost);
            }
        }

        [Test]
        public void CalibrationManager_FailedAttemptStillSavesLastValid()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                        Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                    var calibrationId = manager.LastValidCalibration.calibrationId;
                    Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                        Vector3.forward * 0.01f), Is.False);
                    Assert.That(manager.Save(), Is.True);
                    var saved = JsonUtility.FromJson<PianoCalibrationData>(File.ReadAllText(manager.PersistentPath));
                    Assert.That(saved.valid, Is.True);
                    Assert.That(saved.calibrationId, Is.EqualTo(calibrationId));
                    Assert.That(saved.scaleX, Is.EqualTo(manager.CurrentAppliedCalibration.scaleX));
                    Assert.That(saved.scaleZ, Is.EqualTo(manager.CurrentAppliedCalibration.scaleZ));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void CalibrationManager_FirstFailureHasNoLastValid()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                    Vector3.forward * 0.01f), Is.False);
                Assert.That(manager.LastCalibrationAttempt.valid, Is.False);
                Assert.That(manager.LastValidCalibration, Is.Null);
                Assert.That(manager.CurrentAppliedCalibration, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_FirstFailureRejectsSave()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                        Vector3.forward * 0.01f), Is.False);
                    Assert.That(manager.Save(), Is.False);
                    Assert.That(manager.StatusText, Is.EqualTo("No valid calibration to save."));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void CalibrationManager_FailedAttemptKeepsValidationReason()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                    Vector3.forward * 0.01f), Is.False);
                Assert.That(manager.LastCalibrationAttempt.validationMessage, Does.Contain("too close"));
                Assert.That(manager.StatusText, Does.Contain("too close"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_ValidJsonLoadRestoresCurrentApplied()
        {
            var host = CreateCalibrationFixture(out var manager, out _);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    var expected = CreateScaledCalibration(new Vector3(1.2f, 0.7f, -0.4f),
                        Quaternion.identity, 0.8f, 1.2f);
                    files.WriteText(JsonUtility.ToJson(expected, true));
                    var count = 0;
                    manager.CalibrationChanged += _ => count++;
                    Assert.That(manager.Load(), Is.True);
                    Assert.That(manager.CurrentAppliedCalibration, Is.SameAs(manager.LastValidCalibration));
                    AssertVectorClose(expected.origin, manager.CurrentAppliedCalibration.origin);
                    Assert.That(manager.CurrentAppliedCalibration.scaleX, Is.EqualTo(0.8f).Within(1e-5f));
                    Assert.That(manager.CurrentAppliedCalibration.scaleZ, Is.EqualTo(1.2f).Within(1e-5f));
                    Assert.That(manager.LastCalibrationAttempt.valid, Is.True);
                    Assert.That(count, Is.EqualTo(1));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void CalibrationManager_SaveAndLoadPreservesCurrentAppliedScaleFields()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    Assert.That(CaptureAttempt(manager, hands, Vector3.zero,
                        Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters * 0.8f,
                        Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * 1.2f), Is.True);
                    Assert.That(manager.CurrentAppliedCalibration.scaleX, Is.EqualTo(0.8f).Within(1e-5f));
                    Assert.That(manager.CurrentAppliedCalibration.scaleZ, Is.EqualTo(1.2f).Within(1e-5f));
                    Assert.That(manager.Save(), Is.True);

                    var saved = JsonUtility.FromJson<PianoCalibrationData>(File.ReadAllText(manager.PersistentPath));
                    Assert.That(saved.scaleX, Is.EqualTo(manager.CurrentAppliedCalibration.scaleX));
                    Assert.That(saved.scaleZ, Is.EqualTo(manager.CurrentAppliedCalibration.scaleZ));
                    Assert.That(saved.physicalOctaveSpanMeters,
                        Is.EqualTo(manager.CurrentAppliedCalibration.physicalOctaveSpanMeters));
                    Assert.That(saved.physicalWhiteKeyDepthMeters,
                        Is.EqualTo(manager.CurrentAppliedCalibration.physicalWhiteKeyDepthMeters));
                    Assert.That(saved.calibrationPointDefinitionVersion,
                        Is.EqualTo(PianoCalibrationMath.CurrentPointDefinitionVersion));

                    manager.ClearCalibration();
                    Assert.That(manager.Load(), Is.True);
                    Assert.That(manager.CurrentAppliedCalibration.scaleX, Is.EqualTo(0.8f).Within(1e-5f));
                    Assert.That(manager.CurrentAppliedCalibration.scaleZ, Is.EqualTo(1.2f).Within(1e-5f));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void CalibrationManager_InvalidStoredScalePreservesCurrentAppliedCalibration()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    Assert.That(CaptureAttempt(manager, hands, Vector3.zero,
                        Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                        Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                    var valid = manager.LastValidCalibration;
                    var invalid = CreateScaledCalibration(Vector3.one, Quaternion.identity, 1f, 1f);
                    invalid.scaleX = 0f;
                    files.WriteText(JsonUtility.ToJson(invalid, true));

                    Assert.That(manager.Load(), Is.False);
                    Assert.That(manager.LastValidCalibration, Is.SameAs(valid));
                    Assert.That(manager.CurrentAppliedCalibration, Is.SameAs(valid));
                    Assert.That(manager.StatusText, Does.Contain("outside the supported range"));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void CalibrationManager_CorruptJsonLoadPreservesExistingValidState()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                        Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                    var valid = manager.LastValidCalibration;
                    var applied = manager.CurrentAppliedCalibration;
                    files.WriteText("{");
                    var count = 0;
                    manager.CalibrationChanged += _ => count++;
                    Assert.That(manager.Load(), Is.False);
                    Assert.That(manager.LastValidCalibration, Is.SameAs(valid));
                    Assert.That(manager.CurrentAppliedCalibration, Is.SameAs(applied));
                    Assert.That(manager.LastCalibrationAttempt.valid, Is.False);
                    Assert.That(manager.StatusText, Does.Contain("Load failed"));
                    Assert.That(count, Is.EqualTo(0));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void CalibrationManager_UnknownVersionLoadPreservesExistingValidState()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                        Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                    var valid = manager.LastValidCalibration;
                    var unknown = CreateCalibration(Vector3.one, Quaternion.identity);
                    unknown.formatVersion = 99;
                    files.WriteText(JsonUtility.ToJson(unknown, true));
                    Assert.That(manager.Load(), Is.False);
                    Assert.That(manager.LastValidCalibration, Is.SameAs(valid));
                    Assert.That(manager.CurrentAppliedCalibration, Is.SameAs(valid));
                    Assert.That(manager.LastCalibrationAttempt.valid, Is.False);
                    Assert.That(manager.StatusText, Does.Contain("Unsupported calibration format version"));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void CalibrationManager_OldPointDefinitionLoadsAtUnitScaleAndPreservesPose(int formatVersion)
        {
            var host = CreateCalibrationFixture(out var manager, out _);
            using (var files = new PersistentCalibrationFileScope(manager))
            {
                try
                {
                    var legacyJson = "{\"formatVersion\":" + formatVersion + ",\"valid\":true," +
                                     "\"origin\":{\"x\":1,\"y\":2,\"z\":3}," +
                                     "\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}";
                    files.WriteText(legacyJson);
                    Assert.That(manager.Load(), Is.True);
                    Assert.That(manager.CurrentAppliedCalibration.formatVersion,
                        Is.EqualTo(PianoCalibrationMath.CurrentFormatVersion));
                    Assert.That(manager.LastValidCalibration.valid, Is.True);
                    Assert.That(manager.CurrentAppliedCalibration.calibrationPointDefinitionVersion, Is.Zero);
                    Assert.That(manager.CurrentAppliedCalibration.scaleX, Is.EqualTo(1f));
                    Assert.That(manager.CurrentAppliedCalibration.scaleZ, Is.EqualTo(1f));
                    AssertVectorClose(new Vector3(1f, 2f, 3f), manager.CurrentAppliedCalibration.origin);
                    Assert.That(manager.CurrentAppliedCalibration.rotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(manager.CurrentAppliedCalibration.physicalOctaveSpanMeters,
                        Is.EqualTo(VirtualPianoKeyboard.BaseOctaveSpanMeters));
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void CalibrationManager_ClearExplicitlyRemovesValidState()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                manager.ClearCalibration();
                Assert.That(manager.LastValidCalibration, Is.Null);
                Assert.That(manager.CurrentAppliedCalibration, Is.Null);
                Assert.That(manager.Current, Is.Null);
                Assert.That(manager.LastCalibrationAttempt.valid, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CalibrationManager_SessionSnapshotUsesLastValidAfterFailure()
        {
            var host = CreateCalibrationFixture(out var manager, out var hands);
            var sessionPath = Path.Combine(Application.persistentDataPath, "PianoResearch",
                "test-session-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters,
                    Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters), Is.True);
                var calibrationId = manager.LastValidCalibration.calibrationId;
                Assert.That(CaptureAttempt(manager, hands, Vector3.zero, Vector3.right * 0.01f,
                    Vector3.forward * 0.01f), Is.False);
                var snapshot = WriteSessionSnapshot(manager, sessionPath);
                Assert.That(snapshot.valid, Is.True);
                Assert.That(snapshot.calibrationId, Is.EqualTo(calibrationId));
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (Directory.Exists(sessionPath)) Directory.Delete(sessionPath, true);
            }
        }

        [Test]
        public void KeyboardRoot_OriginRepresentsPhysicalA()
        {
            var origin = new Vector3(1.2f, 0.7f, -0.4f);
            var calibration = CreateCalibration(origin, Quaternion.Euler(12f, 37f, 8f));
            var host = CreateKeyboard(out var keyboard);
            try
            {
                keyboard.ApplyCalibration(calibration);
                AssertVectorClose(origin, keyboard.KeyboardRoot.position);
                Assert.That(Quaternion.Angle(calibration.rotation, keyboard.KeyboardRoot.rotation), Is.LessThanOrEqualTo(PositionTolerance));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_C4WhiteKeyCenterUsesGeometryOffsetFromKeyboardRoot()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                Assert.That(c4, Is.Not.Null);
                var topCenter = c4.TransformPoint(Vector3.up * 0.5f);
                AssertVectorClose(GeometryOffset, keyboard.KeyboardRoot.InverseTransformPoint(topCenter));
                AssertVectorClose(new Vector3(0f, -VirtualPianoKeyboard.BaseWhiteKeyHeightMeters * 0.5f, 0f),
                    c4.localPosition);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_C4FrontLeftEdgeMatchesKeyboardRootOrigin()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                AssertVectorClose(keyboard.KeyboardRoot.position, TopFaceCorner(c4, -1f, -1f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_C4FrontLeftEdgeMatchesAfterCalibrationRotation()
        {
            var calibration = CreateCalibration(new Vector3(-0.4f, 1.1f, 2.3f), Quaternion.Euler(12f, 37f, 8f));
            var host = CreateKeyboard(out var keyboard);
            try
            {
                keyboard.ApplyCalibration(calibration);
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                AssertVectorClose(calibration.pointA, TopFaceCorner(c4, -1f, -1f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_C4FrontLeftEdgeMatchesAfterCalibrationTranslation()
        {
            var origin = new Vector3(4.2f, -0.3f, 1.7f);
            var calibration = CreateCalibration(origin, Quaternion.identity);
            var host = CreateKeyboard(out var keyboard);
            try
            {
                keyboard.ApplyCalibration(calibration);
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                AssertVectorClose(origin, TopFaceCorner(c4, -1f, -1f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardRoot_ScaleRemainsOne()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                keyboard.ApplyCalibration(CreateCalibration(Vector3.one, Quaternion.Euler(4f, 25f, 2f)));
                AssertVectorClose(Vector3.one, keyboard.KeyboardRoot.localScale);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_GeometryOffsetIsAppliedExactlyOnce()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                var geometry = keyboard.KeyboardRoot.Find("Keyboard Geometry");
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                Assert.That(geometry, Is.Not.Null);
                AssertVectorClose(GeometryOffset, geometry.localPosition);
                AssertVectorClose(Vector3.one, geometry.localScale);
                AssertVectorClose(new Vector3(0f, -VirtualPianoKeyboard.BaseWhiteKeyHeightMeters * 0.5f, 0f),
                    c4.localPosition);
                AssertVectorClose(GeometryOffset,
                    keyboard.KeyboardRoot.InverseTransformPoint(c4.TransformPoint(Vector3.up * 0.5f)));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardCalibration_ScalesOnlyGeometryAndKeepsKeyboardRootAtUnitScale()
        {
            var host = CreateKeyboard(out var keyboard);
            var hostPosition = new Vector3(1.4f, 0.9f, -0.7f);
            var hostRotation = Quaternion.Euler(3f, 17f, 1f);
            var calibration = CreateScaledCalibration(new Vector3(-0.4f, 1.1f, 2.3f),
                Quaternion.Euler(12f, 37f, 8f), 0.8f, 1.2f);
            try
            {
                host.transform.SetPositionAndRotation(hostPosition, hostRotation);
                Assert.That(keyboard.TryApplyCalibration(calibration), Is.True);

                AssertVectorClose(hostPosition, host.transform.position);
                Assert.That(Quaternion.Angle(hostRotation, host.transform.rotation),
                    Is.LessThanOrEqualTo(PositionTolerance));
                AssertVectorClose(Vector3.one, host.transform.localScale);
                AssertVectorClose(Vector3.one, keyboard.KeyboardRoot.localScale);
                AssertVectorClose(new Vector3(0.8f, 1f, 1.2f), keyboard.KeyboardGeometry.localScale);
                AssertVectorClose(new Vector3(VirtualPianoKeyboard.BaseWhiteKeyWidthMeters * 0.4f,
                    0f, VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * 0.6f),
                    keyboard.KeyboardGeometry.localPosition);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardCalibration_ThreeTopFaceCornersMatchRotatedTranslatedPhysicalPoints()
        {
            var origin = new Vector3(1.3f, -0.2f, 2.4f);
            var rotation = Quaternion.Euler(12f, 37f, 8f);
            var calibration = CreateScaledCalibration(origin, rotation, 0.8f, 1.2f);
            var host = CreateKeyboard(out var keyboard);
            try
            {
                Assert.That(keyboard.TryApplyCalibration(calibration), Is.True);
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                var c5 = FindKey(keyboard.KeyboardRoot, 72);
                AssertVectorClose(calibration.pointA, TopFaceCorner(c4, -1f, -1f));
                AssertVectorClose(calibration.pointB, TopFaceCorner(c5, -1f, -1f));
                AssertVectorClose(calibration.pointC, TopFaceCorner(c4, -1f, 1f));

                var expectedC4TopCenter = origin + calibration.rightAxis *
                    (VirtualPianoKeyboard.BaseWhiteKeyWidthMeters * calibration.scaleX * 0.5f) +
                    calibration.depthAxis *
                    (VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * calibration.scaleZ * 0.5f);
                AssertVectorClose(expectedC4TopCenter, c4.TransformPoint(Vector3.up * 0.5f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardCalibration_WhiteKeyWidthPitchAndDepthFollowIndependentScales()
        {
            const float scaleX = 1.2f;
            const float scaleZ = 0.8f;
            var host = CreateKeyboard(out var keyboard);
            try
            {
                Assert.That(keyboard.TryApplyCalibration(
                    CreateScaledCalibration(Vector3.zero, Quaternion.identity, scaleX, scaleZ)), Is.True);
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                var d4 = FindKey(keyboard.KeyboardRoot, 62);
                var c5 = FindKey(keyboard.KeyboardRoot, 72);
                Assert.That(Vector3.Distance(c4.position, d4.position),
                    Is.EqualTo(VirtualPianoKeyboard.BaseWhiteKeyPitchMeters * scaleX).Within(PositionTolerance));
                Assert.That(Vector3.Distance(c4.position, c5.position),
                    Is.EqualTo(VirtualPianoKeyboard.BaseOctaveSpanMeters * scaleX).Within(PositionTolerance));
                Assert.That(Vector3.Distance(TopFaceCorner(c4, -1f, -1f), TopFaceCorner(c4, 1f, -1f)),
                    Is.EqualTo(VirtualPianoKeyboard.BaseWhiteKeyWidthMeters * scaleX).Within(PositionTolerance));
                Assert.That(Vector3.Distance(TopFaceCorner(c4, -1f, -1f), TopFaceCorner(c4, -1f, 1f)),
                    Is.EqualTo(VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * scaleZ).Within(PositionTolerance));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardCalibration_BlackKeyRelativePositionFollowsAnisotropicScale()
        {
            const float scaleX = 0.8f;
            const float scaleZ = 1.2f;
            var host = CreateKeyboard(out var keyboard);
            try
            {
                Assert.That(keyboard.TryApplyCalibration(
                    CreateScaledCalibration(Vector3.zero, Quaternion.identity, scaleX, scaleZ)), Is.True);
                var white = FindKey(keyboard.KeyboardRoot, 60);
                var black = FindKey(keyboard.KeyboardRoot, 61, true);
                var relative = keyboard.KeyboardRoot.InverseTransformPoint(black.position) -
                               keyboard.KeyboardRoot.InverseTransformPoint(white.position);
                AssertVectorClose(new Vector3(VirtualPianoKeyboard.BaseWhiteKeyPitchMeters * 0.5f * scaleX,
                    0.021f, 0.035f * scaleZ), relative);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardCalibration_CollidersRemainAlignedWithKeyMeshesAfterScale()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                Assert.That(keyboard.TryApplyCalibration(
                    CreateScaledCalibration(Vector3.one, Quaternion.Euler(4f, 25f, 2f), 0.8f, 1.2f)), Is.True);
                Physics.SyncTransforms();
                // Only key views have physics; decorative separator renderers intentionally do not.
                for (var note = keyboard.MinNote; note <= keyboard.MaxNote; ++note)
                {
                    Assert.That(keyboard.TryGetKey(note, out var key), Is.True);
                    var renderer = key.Renderer;
                    var collider = renderer.GetComponent<BoxCollider>();
                    Assert.That(collider, Is.Not.Null, renderer.name);
                    AssertVectorClose(renderer.bounds.center, collider.bounds.center);
                    AssertVectorClose(renderer.bounds.size, collider.bounds.size);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardCalibration_PressedAnimationKeepsWorldYTravelAtEightMillimeters()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                Assert.That(keyboard.TryApplyCalibration(
                    CreateScaledCalibration(Vector3.zero, Quaternion.Euler(10f, 35f, 3f), 0.8f, 1.2f)), Is.True);
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                var restTop = c4.TransformPoint(Vector3.up * 0.5f);
                var noteOn = new MidiMessage(1d, 1, "test", MidiEventType.NoteOn, 1, 60, 100, -1, -1);
                keyboard.ApplyMidi(in noteOn);
                var pressedTop = c4.TransformPoint(Vector3.up * 0.5f);
                Assert.That(Vector3.Dot(restTop - pressedTop, keyboard.KeyboardRoot.up),
                    Is.EqualTo(0.008f).Within(PositionTolerance));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(0f, 1f)]
        [TestCase(-0.1f, 1f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(1f, -0.1f)]
        [TestCase(1f, float.NaN)]
        [TestCase(1f, float.PositiveInfinity)]
        [TestCase(0.49f, 1f)]
        [TestCase(1.51f, 1f)]
        [TestCase(1f, 0.59f)]
        [TestCase(1f, 1.41f)]
        public void KeyboardCalibration_InvalidScaleIsRejectedWithoutChangingAppliedTransforms(float scaleX,
            float scaleZ)
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                var current = CreateScaledCalibration(new Vector3(0.2f, 0.3f, 0.4f),
                    Quaternion.Euler(4f, 12f, 2f), 1f, 1f);
                Assert.That(keyboard.TryApplyCalibration(current), Is.True);
                var rootPosition = keyboard.KeyboardRoot.position;
                var rootRotation = keyboard.KeyboardRoot.rotation;
                var rootScale = keyboard.KeyboardRoot.localScale;
                var geometryPosition = keyboard.KeyboardGeometry.localPosition;
                var geometryScale = keyboard.KeyboardGeometry.localScale;
                var invalid = CreateScaledCalibration(Vector3.one, Quaternion.identity, 1f, 1f);
                invalid.scaleX = scaleX;
                invalid.scaleZ = scaleZ;

                Assert.That(keyboard.TryApplyCalibration(invalid), Is.False);
                AssertVectorClose(rootPosition, keyboard.KeyboardRoot.position);
                Assert.That(Quaternion.Angle(rootRotation, keyboard.KeyboardRoot.rotation),
                    Is.LessThanOrEqualTo(PositionTolerance));
                AssertVectorClose(rootScale, keyboard.KeyboardRoot.localScale);
                AssertVectorClose(geometryPosition, keyboard.KeyboardGeometry.localPosition);
                AssertVectorClose(geometryScale, keyboard.KeyboardGeometry.localScale);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void KeyboardCalibration_DoesNotMoveCameraXrOriginPianoRootOrRawHandPose()
        {
            var cameraObject = new GameObject("HMD Camera", typeof(Camera));
            var xrOrigin = new GameObject("XR Origin").transform;
            var rawHandHost = new GameObject("Raw Hand Pose Provider");
            var rawHands = rawHandHost.AddComponent<XRHandPoseProvider>();
            var host = CreateKeyboard(out var keyboard);
            var previousTrackingOrigin = ResearchServices.TrackingOrigin;
            var raw = new HandPoseFrame(1)
            {
                RightTracked = true,
                RightRootPose = new Pose(new Vector3(0.2f, 0.4f, 0.6f), Quaternion.Euler(1f, 2f, 3f))
            };
            raw.RightJoints[0] = new HandJointPose
            {
                JointId = XRHandJointID.IndexTip,
                PoseValid = true,
                TrackingState = XRHandJointTrackingState.Pose,
                Pose = new Pose(new Vector3(0.7f, 0.8f, 0.9f), Quaternion.identity)
            };
            SetPrivateField(rawHands, "m_Raw", raw);
            cameraObject.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(5f, 6f, 7f));
            xrOrigin.SetPositionAndRotation(new Vector3(-1f, 0.5f, 2f), Quaternion.Euler(1f, 20f, 2f));
            host.transform.SetPositionAndRotation(new Vector3(0.5f, 0.6f, 0.7f), Quaternion.Euler(3f, 8f, 1f));
            var cameraPosition = cameraObject.transform.position;
            var cameraRotation = cameraObject.transform.rotation;
            var originPosition = xrOrigin.position;
            var originRotation = xrOrigin.rotation;
            var rootParentPosition = host.transform.position;
            var rootParentRotation = host.transform.rotation;
            var rawRootPose = raw.RightRootPose;
            var rawJointPose = raw.FindJoint(false, XRHandJointID.IndexTip).Pose;
            typeof(ResearchServices).GetProperty("TrackingOrigin").SetValue(null, xrOrigin);
            try
            {
                Assert.That(keyboard.TryApplyCalibration(
                    CreateScaledCalibration(Vector3.one, Quaternion.Euler(2f, 40f, 3f), 0.8f, 1.2f)), Is.True);
                AssertVectorClose(cameraPosition, cameraObject.transform.position);
                Assert.That(Quaternion.Angle(cameraRotation, cameraObject.transform.rotation),
                    Is.LessThanOrEqualTo(PositionTolerance));
                AssertVectorClose(originPosition, xrOrigin.position);
                Assert.That(Quaternion.Angle(originRotation, xrOrigin.rotation),
                    Is.LessThanOrEqualTo(PositionTolerance));
                AssertVectorClose(rootParentPosition, host.transform.position);
                Assert.That(Quaternion.Angle(rootParentRotation, host.transform.rotation),
                    Is.LessThanOrEqualTo(PositionTolerance));
                Assert.That(typeof(XRHandPoseProvider).GetField("m_Raw", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(rawHands), Is.SameAs(raw));
                Assert.That(raw.RightRootPose.position, Is.EqualTo(rawRootPose.position));
                Assert.That(raw.FindJoint(false, XRHandJointID.IndexTip).Pose.position,
                    Is.EqualTo(rawJointPose.position));
            }
            finally
            {
                typeof(ResearchServices).GetProperty("TrackingOrigin").SetValue(null, previousTrackingOrigin);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(rawHandHost);
                Object.DestroyImmediate(xrOrigin.gameObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Keyboard_C4ToC5ExistingKeySpacingRemainsUnchanged()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                var c4 = FindKey(keyboard.KeyboardRoot, 60);
                var c5 = FindKey(keyboard.KeyboardRoot, 72);
                var spacing = keyboard.KeyboardRoot.InverseTransformPoint(c5.position) -
                              keyboard.KeyboardRoot.InverseTransformPoint(c4.position);
                AssertVectorClose(WhiteKeyCenterSpacing * 7f, spacing);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_BlackKeyExistingRelativePlacementRemainsUnchanged()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                var root = keyboard.KeyboardRoot;
                var whiteNotes = new[] { 60, 62, 65, 67, 69 };
                var blackNotes = new[] { 61, 63, 66, 68, 70 };
                for (var i = 0; i < blackNotes.Length; ++i)
                {
                    var white = FindKey(root, whiteNotes[i]);
                    var black = FindKey(root, blackNotes[i], true);
                    var relative = root.InverseTransformPoint(black.position) - root.InverseTransformPoint(white.position);
                    AssertVectorClose(new Vector3(0.018f, 0.021f, 0.035f), relative);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_WithoutCalibrationPreservesHmdForwardInitialRootPlacement()
        {
            var host = CreateKeyboard(out var keyboard);
            try
            {
                var defaultPosition = new Vector3(0f, 1.1f, 0.75f);
                var defaultRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                keyboard.KeyboardRoot.SetPositionAndRotation(defaultPosition, defaultRotation);
                keyboard.ApplyCalibration(null);
                keyboard.ApplyCalibration(new PianoCalibrationData());
                AssertVectorClose(defaultPosition, keyboard.KeyboardRoot.position);
                Assert.That(Quaternion.Angle(defaultRotation, keyboard.KeyboardRoot.rotation), Is.LessThanOrEqualTo(PositionTolerance));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Keyboard_DuplicateNoteOnAndChordRemainStable()
        {
            var tracker = new KeyboardStateTracker();
            var first = new MidiMessage(1d, 1, "test", MidiEventType.NoteOn, 1, 60, 90, -1, -1);
            var duplicate = new MidiMessage(2d, 2, "test", MidiEventType.NoteOn, 1, 60, 100, -1, -1);
            var chord = new MidiMessage(3d, 3, "test", MidiEventType.NoteOn, 1, 64, 80, -1, -1);
            tracker.Apply(in first);
            tracker.Apply(in duplicate);
            tracker.Apply(in chord);
            Assert.That(tracker.IsPressed(60), Is.True);
            Assert.That(tracker.IsPressed(64), Is.True);

            var off = new MidiMessage(4d, 4, "test", MidiEventType.NoteOff, 1, 60, 0, -1, -1);
            tracker.Apply(in off);
            Assert.That(tracker.IsPressed(60), Is.False);
            Assert.That(tracker.IsPressed(64), Is.True);
        }

        [Test]
        public void Keyboard_Cc64AndChannelsArePreserved()
        {
            var tracker = new KeyboardStateTracker();
            var channelOne = new MidiMessage(1d, 1, "test", MidiEventType.NoteOn, 1, 60, 45, -1, -1);
            var channelTwo = new MidiMessage(2d, 2, "test", MidiEventType.NoteOn, 2, 60, 110, -1, -1);
            tracker.Apply(in channelOne);
            tracker.Apply(in channelTwo);

            var channelOneOff = new MidiMessage(3d, 3, "test", MidiEventType.NoteOff, 1, 60, 0, -1, -1);
            tracker.Apply(in channelOneOff);
            Assert.That(tracker.IsPressed(60), Is.True);
            Assert.That(tracker.Velocity(60), Is.EqualTo(110));

            var sustainOn = new MidiMessage(4d, 4, "test", MidiEventType.ControlChange, 2, -1, -1, 64, 127);
            tracker.Apply(in sustainOn);
            Assert.That(tracker.SustainActive, Is.True);

            var sustainOff = new MidiMessage(5d, 5, "test", MidiEventType.ControlChange, 2, -1, -1, 64, 0);
            tracker.Apply(in sustainOff);
            Assert.That(tracker.SustainActive, Is.False);
        }

        [Test]
        public void IdentityProcessor_CopiesRawPoseIntoDisplayFrame()
        {
            var raw = new HandPoseFrame(1)
            {
                AbsoluteTimeSeconds = 12.5d,
                UnityFrame = 42,
                CallbackIndex = 7,
                LeftTracked = true,
                LeftRootPose = new Pose(Vector3.one, Quaternion.Euler(1f, 2f, 3f))
            };
            raw.LeftJoints[0] = new HandJointPose
            {
                JointId = XRHandJointID.IndexTip,
                PoseValid = true,
                TrackingState = XRHandJointTrackingState.Pose,
                Pose = new Pose(new Vector3(0.1f, 0.2f, 0.3f), Quaternion.identity)
            };
            var display = new HandPoseFrame(1);

            new IdentityHandPoseProcessor().Process(raw, display);

            Assert.That(display.AbsoluteTimeSeconds, Is.EqualTo(raw.AbsoluteTimeSeconds));
            Assert.That(display.UnityFrame, Is.EqualTo(raw.UnityFrame));
            Assert.That(display.CallbackIndex, Is.EqualTo(raw.CallbackIndex));
            Assert.That(display.LeftTracked, Is.True);
            Assert.That(display.LeftJoints[0].JointId, Is.EqualTo(XRHandJointID.IndexTip));
            Assert.That(display.LeftJoints[0].Pose.position, Is.EqualTo(raw.LeftJoints[0].Pose.position));
        }
    }
}
