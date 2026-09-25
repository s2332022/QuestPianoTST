using System;
using System.Reflection;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class CalibrationCaptureTests
    {
        GameObject m_Host;
        XRHandPoseProvider m_Hands;
        PianoCalibrationManager m_Manager;
        HandPoseFrame m_Frame;
        Transform m_PreviousTrackingOrigin;
        long m_Callback;

        [SetUp]
        public void SetUp()
        {
            m_PreviousTrackingOrigin = ResearchServices.TrackingOrigin;
            typeof(ResearchServices).GetProperty("TrackingOrigin").SetValue(null, null);
            m_Host = new GameObject("Calibration Capture Test");
            m_Hands = m_Host.AddComponent<XRHandPoseProvider>();
            m_Manager = m_Host.AddComponent<PianoCalibrationManager>();
            m_Frame = new HandPoseFrame(1);
            typeof(XRHandPoseProvider).GetField("m_Display", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m_Hands, m_Frame);
            typeof(PianoCalibrationManager).GetField("m_Hands", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m_Manager, m_Hands);
            Frame(new Vector3(9f, 9f, 9f));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(m_Host);
            typeof(ResearchServices).GetProperty("TrackingOrigin").SetValue(null, m_PreviousTrackingOrigin);
        }

        void Frame(Vector3 position, bool tracked = true, bool valid = true)
        {
            m_Frame.CallbackIndex = ++m_Callback;
            m_Frame.RightTracked = tracked;
            m_Frame.RightJoints[0] = new HandJointPose
            {
                JointId = XRHandJointID.IndexTip,
                PoseValid = valid,
                TrackingState = valid ? XRHandJointTrackingState.Pose : XRHandJointTrackingState.None,
                Pose = new Pose(position, Quaternion.identity)
            };
        }

        double ArmedAt => (double)typeof(PianoCalibrationManager)
            .GetField("m_ArmedAt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(m_Manager);

        void Tick(double offset) => typeof(PianoCalibrationManager)
            .GetMethod("AdvanceCapture", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(m_Manager, new object[] { ArmedAt + offset });

        void Sample(Vector3 position, int count = 12)
        {
            for (var i = 0; i < count; ++i)
            {
                Frame(position);
                Tick(3.01 + i * 0.03);
            }
            Tick(3.41);
        }

        void CompleteA() { m_Manager.CaptureA(); Sample(Vector3.zero); }
        void CompleteB() { m_Manager.CaptureB(); Sample(Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters); }
        void CompleteC() { m_Manager.CaptureC(); Sample(Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters); }

        [Test]
        public void PassthroughSession_StaysRequestedThroughAAndBThenEndsAfterC()
        {
            var started = 0;
            var ended = 0;
            m_Manager.CaptureSessionStarted += () => ++started;
            m_Manager.CaptureSessionEnded += () => ++ended;
            CompleteA();
            Assert.That((started, ended), Is.EqualTo((1, 0)));
            CompleteB();
            Assert.That((started, ended), Is.EqualTo((1, 0)));
            CompleteC();
            Assert.That((started, ended), Is.EqualTo((1, 1)));
        }

        [Test]
        public void PassthroughSession_FailedPointKeepsRequestAndCancelRestores()
        {
            var started = 0;
            var ended = 0;
            m_Manager.CaptureSessionStarted += () => ++started;
            m_Manager.CaptureSessionEnded += () => ++ended;
            CompleteA();
            m_Manager.CaptureB();
            Frame(Vector3.zero, tracked: false);
            Tick(3.01);
            Assert.That((started, ended), Is.EqualTo((1, 0)));
            Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(1));
            m_Manager.CancelCapture();
            Assert.That((started, ended), Is.EqualTo((1, 1)));
            Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(1));
        }

        [Test]
        public void PassthroughStartupFailure_PreservesCalibrationAndOffersSamePointRetry()
        {
            CompleteA();
            CompleteB();
            CompleteC();
            var lastValid = m_Manager.LastValidCalibration;
            m_Manager.CaptureA();
            m_Manager.FailPassthroughStartup("Camera subsystem failed to start");

            Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.Idle));
            Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(3));
            Assert.That(m_Manager.LastValidCalibration, Is.SameAs(lastValid));
            Assert.That(m_Manager.Current, Is.SameAs(lastValid));
            Assert.That(m_Manager.PassthroughRetryAvailable, Is.True);
            Assert.That(m_Manager.StatusText, Does.Contain("Passthrough unavailable"));
            Assert.That(m_Manager.StatusText, Does.Contain("Camera subsystem failed to start"));

            m_Manager.RetryPassthroughCapture();
            Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.ArmedA));
            Assert.That(m_Manager.PassthroughRetryAvailable, Is.False);
            Assert.That(m_Manager.LastValidCalibration, Is.SameAs(lastValid));
        }

        [Test]
        public void CancelCalibrationAfterPassthroughFailure_PreservesPointsAndLastValidCalibration()
        {
            CompleteA();
            CompleteB();
            CompleteC();
            var lastValid = m_Manager.LastValidCalibration;
            m_Manager.CaptureA();
            m_Manager.FailPassthroughStartup("Camera subsystem failed to start");
            m_Manager.CancelCalibration();

            Assert.That(m_Manager.PassthroughRetryAvailable, Is.False);
            Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(3));
            Assert.That(m_Manager.LastValidCalibration, Is.SameAs(lastValid));
            Assert.That(m_Manager.Current, Is.SameAs(lastValid));
            Assert.That(m_Manager.StatusText, Is.EqualTo("Calibration cancelled"));
        }

        [Test] public void CaptureAButtonOnlyArms() { m_Manager.CaptureA(); Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.ArmedA)); Assert.That(m_Manager.CapturedPointCount, Is.Zero); }
        [Test]
        public void CaptureInstructionsUseTheFixedC4AndC5Corners()
        {
            m_Manager.CaptureA();
            Assert.That(m_Manager.StatusText, Does.Contain("C4 front-left corner"));
            Sample(Vector3.zero);
            m_Manager.CaptureB();
            Assert.That(m_Manager.StatusText, Does.Contain("C5 front-left corner"));
            Sample(Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters);
            m_Manager.CaptureC();
            Assert.That(m_Manager.StatusText, Does.Contain("C4 back-left corner"));
        }
        [Test] public void BeforeThreeSecondsDoesNotCapture() { m_Manager.CaptureA(); Tick(2.99); Assert.That(m_Manager.CapturedPointCount, Is.Zero); }
        [Test] public void ValidSamplesCaptureA() { CompleteA(); Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(1)); Assert.That(m_Manager.StatusText, Does.Contain("A captured")); }
        [Test] public void OneFrameCannotCapture() { m_Manager.CaptureA(); Frame(Vector3.zero); Tick(3.01); Tick(3.41); Assert.That(m_Manager.CapturedPointCount, Is.Zero); }
        [Test] public void InvalidIndexTipFails() { m_Manager.CaptureA(); Frame(Vector3.zero, valid: false); Tick(3.01); Tick(3.41); Assert.That(m_Manager.StatusText, Is.EqualTo("IndexTip invalid")); }
        [Test] public void UntrackedHandFails() { m_Manager.CaptureA(); Frame(Vector3.zero, tracked: false); Tick(3.01); Assert.That(m_Manager.StatusText, Is.EqualTo("Hand not tracked")); }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(float.NegativeInfinity)]
        public void NonFinitePositionFails(float x) { m_Manager.CaptureA(); Frame(new Vector3(x, 0, 0)); Tick(3.01); Tick(3.41); Assert.That(m_Manager.CapturedPointCount, Is.Zero); }
        [Test] public void TooFewValidSamplesFail() { m_Manager.CaptureA(); for (var i = 0; i < 9; ++i) { Frame(Vector3.zero); Tick(3.01 + i * 0.03); } Tick(3.41); Assert.That(m_Manager.StatusText, Is.EqualTo("Not enough samples")); }
        [Test] public void MovingHandFails() { m_Manager.CaptureA(); for (var i = 0; i < 12; ++i) { Frame(new Vector3(i * 0.01f, 0, 0)); Tick(3.01 + i * 0.03); } Tick(3.41); Assert.That(m_Manager.StatusText, Is.EqualTo("Hand moved too much")); }
        [Test] public void MedianDeterminesCapturedPoint() { m_Manager.CaptureA(); for (var i = 0; i < 12; ++i) { Frame(new Vector3(i < 6 ? 0f : 0.01f, 0, 0)); Tick(3.01 + i * 0.03); } Tick(3.41); Assert.That(((Vector3[])typeof(PianoCalibrationManager).GetField("m_Points", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(m_Manager))[0].x, Is.EqualTo(0.005f).Within(1e-5f)); }
        [Test] public void AfterACanArmB() { CompleteA(); m_Manager.CaptureB(); Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.ArmedB)); }
        [Test] public void AfterBCanArmC() { CompleteA(); CompleteB(); m_Manager.CaptureC(); Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.ArmedC)); }
        [Test] public void AfterCCalculationApplies() { CompleteA(); CompleteB(); CompleteC(); Assert.That(m_Manager.LastValidCalibration.valid, Is.True); Assert.That(m_Manager.CurrentAppliedCalibration, Is.SameAs(m_Manager.LastValidCalibration)); Assert.That(m_Manager.StatusText, Does.Contain("Calibration complete")); Assert.That(m_Manager.StatusText, Does.Contain("Width scale:")); Assert.That(m_Manager.StatusText, Does.Contain("Depth scale:")); Assert.That(m_Manager.StatusText, Does.Contain("Measured octave span:")); Assert.That(m_Manager.StatusText, Does.Contain("Measured white-key depth:")); }
        [Test] public void CancelPreservesPointCount() { CompleteA(); m_Manager.CaptureB(); m_Manager.CancelCapture(); Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(1)); Assert.That(m_Manager.ValidSampleCount, Is.Zero); }
        [Test] public void FailurePreservesPriorPoint() { CompleteA(); m_Manager.CaptureB(); Frame(Vector3.zero, tracked: false); Tick(3.01); Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(1)); }
        [Test] public void FailurePreservesLastValidCalibration() { CompleteA(); CompleteB(); CompleteC(); var valid = m_Manager.LastValidCalibration; m_Manager.CaptureA(); Frame(Vector3.zero, tracked: false); Tick(3.01); Assert.That(m_Manager.LastValidCalibration, Is.SameAs(valid)); }
        [Test]
        public void ScaleFailurePreservesLastValidAndAllowsRecapturingC()
        {
            CompleteA(); CompleteB(); CompleteC();
            var lastValid = m_Manager.LastValidCalibration;

            m_Manager.CaptureA(); Sample(Vector3.zero);
            m_Manager.CaptureB(); Sample(Vector3.right * VirtualPianoKeyboard.BaseOctaveSpanMeters);
            m_Manager.CaptureC(); Sample(Vector3.forward * 0.08f);

            Assert.That(m_Manager.LastValidCalibration, Is.SameAs(lastValid));
            Assert.That(m_Manager.CurrentAppliedCalibration, Is.SameAs(lastValid));
            Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(2));
            Assert.That(m_Manager.IsCapturing, Is.True);
            Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.Idle));
            Assert.That(m_Manager.StatusText, Does.Contain("outside the supported range"));

            m_Manager.CaptureC(); Sample(Vector3.forward * VirtualPianoKeyboard.BaseWhiteKeyDepthMeters);
            Assert.That(m_Manager.LastValidCalibration, Is.Not.SameAs(lastValid));
            Assert.That(m_Manager.CurrentAppliedCalibration, Is.SameAs(m_Manager.LastValidCalibration));
            Assert.That(m_Manager.CurrentAppliedCalibration.scaleX, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(m_Manager.CurrentAppliedCalibration.scaleZ, Is.EqualTo(1f).Within(1e-5f));
        }
        [Test] public void ButtonPoseIsNotSampled() { m_Manager.CaptureA(); Tick(2.99); Assert.That(m_Manager.ValidSampleCount, Is.Zero); Sample(Vector3.zero); Assert.That(m_Manager.CapturedPointCount, Is.EqualTo(1)); }
        [Test] public void DuplicateCaptureIsIgnored() { m_Manager.CaptureA(); m_Manager.CaptureA(); m_Manager.CaptureB(); Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.ArmedA)); }
        [Test] public void DisablingManagerCancels() { m_Manager.CaptureA(); m_Manager.enabled = false; typeof(PianoCalibrationManager).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_Manager, null); Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.Idle)); }
        [Test] public void ClosingUiCancels() { var uiObject = new GameObject("Capture UI"); try { var ui = uiObject.AddComponent<DistributedQuestUi>(); typeof(DistributedQuestUi).GetField("m_Calibration", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ui, m_Manager); m_Manager.CaptureA(); uiObject.SetActive(false); typeof(DistributedQuestUi).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, null); Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.Idle)); } finally { UnityEngine.Object.DestroyImmediate(uiObject); } }
        [Test] public void QuitCancels() { m_Manager.CaptureA(); typeof(PianoCalibrationManager).GetMethod("OnApplicationQuit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_Manager, null); Assert.That(m_Manager.State, Is.EqualTo(PianoCalibrationManager.CaptureState.Idle)); }
        [Test] public void RightHandIsDefault() { Assert.That(m_Manager.UseLeftHand, Is.False); Assert.That(m_Manager.CaptureHandName, Is.EqualTo("Right")); }
    }
}
