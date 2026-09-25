using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class KeyboardDisplayModeTests
    {
        static GameObject Create(out VirtualPianoKeyboard keyboard)
        {
            var host = new GameObject("Keyboard mode test");
            keyboard = host.AddComponent<VirtualPianoKeyboard>();
            typeof(VirtualPianoKeyboard).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(keyboard, null);
            return host;
        }

        [TestCase(KeyboardDisplayMode.Research13Keys, 60, 72, 13, 8, 5)]
        [TestCase(KeyboardDisplayMode.Full88Keys, 21, 108, 88, 52, 36)]
        public void Modes_HaveContinuousUniqueNotesAndExpectedColors(
            KeyboardDisplayMode mode, int min, int max, int count, int white, int black)
        {
            var host = Create(out var keyboard);
            try
            {
                keyboard.SetDisplayMode(mode);
                Assert.That(keyboard.MinNote, Is.EqualTo(min));
                Assert.That(keyboard.MaxNote, Is.EqualTo(max));
                Assert.That(keyboard.KeyCount, Is.EqualTo(count));
                Assert.That(keyboard.KeyboardGeometry.childCount, Is.EqualTo(count));
                var seen = new HashSet<int>();
                var whiteCount = 0;
                var blackCount = 0;
                Mesh sharedMesh = null;
                Material sharedMaterial = null;
                for (var note = min; note <= max; ++note)
                {
                    Assert.That(keyboard.TryGetKey(note, out var key), Is.True);
                    Assert.That(seen.Add(key.MidiNoteNumber), Is.True);
                    if (key.IsBlack) ++blackCount; else ++whiteCount;
                    var renderer = key.Renderer;
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    if (sharedMesh == null) sharedMesh = mesh; else Assert.That(mesh, Is.SameAs(sharedMesh));
                    if (sharedMaterial == null) sharedMaterial = renderer.sharedMaterial;
                    else Assert.That(renderer.sharedMaterial, Is.SameAs(sharedMaterial));
                }
                Assert.That(whiteCount, Is.EqualTo(white));
                Assert.That(blackCount, Is.EqualTo(black));
                Assert.That(keyboard.TryGetKey(min - 1, out _), Is.False);
                Assert.That(keyboard.TryGetKey(max + 1, out _), Is.False);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void FullKeyboard_AnchorsC4AndUsesCorrectEndNotesAndWhiteGaps()
        {
            var host = Create(out var keyboard);
            try
            {
                keyboard.TryGetKey(21, out var a0);
                keyboard.TryGetKey(60, out var c4);
                keyboard.TryGetKey(108, out var c8);
                Assert.That(a0.IsBlack || c4.IsBlack || c8.IsBlack, Is.False);
                Assert.That(a0.Renderer.name, Does.Contain("21"));
                Assert.That(c4.Renderer.name, Does.Contain("60"));
                Assert.That(c8.Renderer.name, Does.Contain("108"));
                Assert.That(a0.RestLocalPosition.x, Is.LessThan(c4.RestLocalPosition.x));
                Assert.That(c8.RestLocalPosition.x, Is.GreaterThan(c4.RestLocalPosition.x));
                Assert.That(c4.RestLocalPosition.x, Is.Zero);
                Assert.That(keyboard.TryGetKey(64, out var e4), Is.True);
                Assert.That(keyboard.TryGetKey(65, out var f4), Is.True);
                Assert.That(f4.RestLocalPosition.x - e4.RestLocalPosition.x,
                    Is.EqualTo(VirtualPianoKeyboard.BaseWhiteKeyPitchMeters).Within(1e-5f));
                Assert.That(keyboard.TryGetKey(71, out var b4), Is.True);
                Assert.That(keyboard.TryGetKey(72, out var c5), Is.True);
                Assert.That(c5.RestLocalPosition.x - b4.RestLocalPosition.x,
                    Is.EqualTo(VirtualPianoKeyboard.BaseWhiteKeyPitchMeters).Within(1e-5f));
                Assert.That(keyboard.KeyboardRoot.localScale, Is.EqualTo(Vector3.one));
                Assert.That(keyboard.KeyboardGeometry.localScale.y, Is.EqualTo(1f));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void SwitchingModes_PreservesTransformScaleStateAndDirectReferences()
        {
            var host = Create(out var keyboard);
            try
            {
                keyboard.KeyboardRoot.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 30f, 0f));
                keyboard.KeyboardGeometry.localScale = new Vector3(0.8f, 1f, 1.2f);
                keyboard.KeyboardGeometry.localPosition = new Vector3(
                    VirtualPianoKeyboard.BaseWhiteKeyWidthMeters * 0.4f, 0f,
                    VirtualPianoKeyboard.BaseWhiteKeyDepthMeters * 0.6f);
                var c4Position = keyboard.KeyboardRoot.position;
                var c4Rotation = keyboard.KeyboardRoot.rotation;
                var on = new MidiMessage(1, 1, "test", MidiEventType.NoteOn, 2, 21, 90, -1, -1);
                var sustain = new MidiMessage(1, 2, "test", MidiEventType.ControlChange, 2, -1, 0, 64, 127);
                keyboard.ApplyMidi(in on);
                keyboard.ApplyMidi(in sustain);
                keyboard.SetDisplayMode(KeyboardDisplayMode.Research13Keys);
                Assert.That(keyboard.TryGetKey(21, out _), Is.False);
                Assert.That(keyboard.State.IsPressed(21), Is.True);
                keyboard.SetDisplayMode(KeyboardDisplayMode.Full88Keys);
                keyboard.SetDisplayMode(KeyboardDisplayMode.Full88Keys);
                Assert.That(keyboard.KeyboardGeometry.childCount, Is.EqualTo(88));
                Assert.That(keyboard.TryGetKey(21, out var a0), Is.True);
                Assert.That(a0.Pressed, Is.True);
                Assert.That(a0.Velocity, Is.EqualTo(90));
                Assert.That(keyboard.State.IsSustainActive(2), Is.True);
                Assert.That(keyboard.KeyboardRoot.position, Is.EqualTo(c4Position));
                Assert.That(keyboard.KeyboardRoot.rotation, Is.EqualTo(c4Rotation));
                Assert.That(keyboard.KeyboardGeometry.localScale, Is.EqualTo(new Vector3(0.8f, 1f, 1.2f)));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void SavedMode_MissingInvalidAndLegacyValuesDefaultToFull()
        {
            Assert.That(DistributedQuestUi.ParseSavedDisplayMode("{}"), Is.EqualTo(KeyboardDisplayMode.Full88Keys));
            Assert.That(DistributedQuestUi.ParseSavedDisplayMode("{\"ip\":\"10.0.0.1\"}"), Is.EqualTo(KeyboardDisplayMode.Full88Keys));
            Assert.That(DistributedQuestUi.ParseSavedDisplayMode("{\"keyboardMode\":\"bogus\"}"), Is.EqualTo(KeyboardDisplayMode.Full88Keys));
            Assert.That(DistributedQuestUi.ParseSavedDisplayMode("{\"keyboardMode\":\"Research13Keys\"}"), Is.EqualTo(KeyboardDisplayMode.Research13Keys));
            var json = DistributedQuestUi.SerializeSavedSettings("10.0.0.1", KeyboardDisplayMode.Research13Keys);
            Assert.That(DistributedQuestUi.ParseSavedDisplayMode(json), Is.EqualTo(KeyboardDisplayMode.Research13Keys));
        }

        [Test]
        public void SavedMode_RestoresFromFileAndDefaultsWhenMissingOrInvalid()
        {
            var directory = Path.Combine(Path.GetTempPath(), "KeyboardDisplayModeTests-" + System.Guid.NewGuid());
            var path = Path.Combine(directory, "distributed_host_ip.json");
            try
            {
                Assert.That(DistributedQuestUi.LoadSavedDisplayModeFromPath(path), Is.EqualTo(KeyboardDisplayMode.Full88Keys));
                DistributedQuestUi.SaveSettingsToPath(path, "10.0.0.1", KeyboardDisplayMode.Research13Keys);
                Assert.That(DistributedQuestUi.LoadSavedDisplayModeFromPath(path), Is.EqualTo(KeyboardDisplayMode.Research13Keys));
                File.WriteAllText(path, "{\"ip\":\"10.0.0.1\",\"keyboardMode\":\"invalid\"}");
                Assert.That(DistributedQuestUi.LoadSavedDisplayModeFromPath(path), Is.EqualTo(KeyboardDisplayMode.Full88Keys));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void MidiEndpoints_ApplyOnOffAndOutOfRangeNotesStayInTracker()
        {
            var host = Create(out var keyboard);
            try
            {
                foreach (var note in new[] { 21, 60, 108 })
                {
                    var on = new MidiMessage(1, note, "test", MidiEventType.NoteOn, 3, note, 97, -1, -1);
                    var off = new MidiMessage(2, note, "test", MidiEventType.NoteOff, 3, note, 0, -1, -1);
                    keyboard.ApplyMidi(in on);
                    Assert.That(keyboard.TryGetKey(note, out var key), Is.True);
                    Assert.That(key.Pressed, Is.True);
                    Assert.That(key.Velocity, Is.EqualTo(97));
                    keyboard.ApplyMidi(in off);
                    Assert.That(key.Pressed, Is.False);
                }

                foreach (var note in new[] { 20, 109 })
                {
                    var on = new MidiMessage(3, note, "test", MidiEventType.NoteOn, 1, note, 80, -1, -1);
                    keyboard.ApplyMidi(in on);
                    Assert.That(keyboard.State.IsPressed(note), Is.True);
                    Assert.That(keyboard.TryGetKey(note, out _), Is.False);
                }
                keyboard.SetDisplayMode(KeyboardDisplayMode.Research13Keys);
                Assert.That(keyboard.State.IsPressed(20), Is.True);
                Assert.That(keyboard.State.IsPressed(109), Is.True);
                keyboard.State.Reset(4);
                Assert.That(keyboard.State.IsPressed(20) || keyboard.State.IsPressed(109), Is.False);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
