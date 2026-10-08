using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class PerformanceUiPolicyTests
    {
        [Test]
        public void MidiCooldown_UsesQuestTimeAndExtendsOnRepeatedEvents()
        {
            var gate = new PerformanceUiGate();
            Assert.That(gate.Tick(100, false, false), Is.False);
            gate.Observe(MidiEventType.NoteOn, 100);
            gate.Observe(MidiEventType.NoteOff, 100.8);
            Assert.That(gate.Tick(101.79, false, false), Is.True);
            Assert.That(gate.Tick(101.8, false, false), Is.False);
        }

        [Test]
        public void HeldNotesAndHeldPinch_PreventEarlyUnlock()
        {
            var gate = new PerformanceUiGate();
            gate.Observe(MidiEventType.NoteOn, 1);
            Assert.That(gate.Tick(20, true, false), Is.True);
            Assert.That(gate.Tick(21, false, true), Is.True);
            Assert.That(gate.Tick(22, false, false), Is.False);
            Assert.That(gate.Tick(23, false, true), Is.False, "A new intentional pinch is allowed.");
        }

        [Test]
        public void SnapshotRelease_AlsoKeepsACooldown()
        {
            var gate = new PerformanceUiGate();
            Assert.That(gate.Tick(10, true, false), Is.True);
            Assert.That(gate.Tick(10.5, false, false), Is.True);
            Assert.That(gate.Tick(11, false, false), Is.False);
        }

        [TestCase(MidiEventType.NoteOn)]
        [TestCase(MidiEventType.NoteOff)]
        [TestCase(MidiEventType.ControlChange)]
        public void PerformanceEvents_Lock(MidiEventType type)
        {
            var gate = new PerformanceUiGate();
            gate.Observe(type, 10);
            Assert.That(gate.Tick(10.5, false, false), Is.True);
        }

        [Test]
        public void OtherEvents_DoNotLock()
        {
            var gate = new PerformanceUiGate();
            gate.Observe(MidiEventType.Other, 10);
            Assert.That(gate.Tick(10, false, false), Is.False);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        public void Placement_UsesWorldMetresKeyboardAxesAndThirtyDegreeTilt(float yaw)
        {
            var go = new GameObject("Performance UI placement test");
            try
            {
                var center = new Vector3(5, 1, -2);
                var rotation = Quaternion.Euler(0, yaw, 0);
                QuestSpatialPlacement.PlacePerformanceUi(go.transform, center, rotation);
                var offset = Quaternion.Inverse(rotation) * (go.transform.position - center);
                Assert.That(offset, Is.EqualTo(new Vector3(.15f, .4f, .3f)).Using(new Vector3EqualityComparer(.00001f)));
                Assert.That(Quaternion.Angle(go.transform.rotation, rotation * Quaternion.Euler(30, 0, 0)), Is.LessThan(.001f));
                var bottom = go.transform.position - go.transform.up * (970 * .00065f / 2);
                var localBottom = Quaternion.Inverse(rotation) * (bottom - center);
                Assert.That(localBottom.y, Is.GreaterThan(.12f));
                Assert.That(localBottom.z, Is.GreaterThan(.14f));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
