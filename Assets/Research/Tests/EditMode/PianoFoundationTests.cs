using NUnit.Framework;
using UnityEngine;

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
    }
}
