using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class PianoFoundationTests
    {
        [Test]
        public void Calibration_ProducesOrthogonalAxes()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right, Vector3.forward, out var data), Is.True);
            Assert.That(Vector3.Dot(data.rightAxis, data.depthAxis), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(Vector3.Dot(data.normal, data.rightAxis), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Calibration_RejectsNearParallelAxes()
        {
            Assert.That(PianoCalibrationMath.TryCalculate(
                Vector3.zero, Vector3.right, Vector3.right * 2f, out var data), Is.False);
            Assert.That(data.valid, Is.False);
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
