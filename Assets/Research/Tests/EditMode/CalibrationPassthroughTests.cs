using System.Reflection;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class CalibrationPassthroughTests
    {
        [TestCase(KeyboardDisplayMode.Research13Keys, false)]
        [TestCase(KeyboardDisplayMode.Full88Keys, false)]
        [TestCase(KeyboardDisplayMode.Full88Keys, true)]
        public void Session_RestoresPreviousStateAndSharedKeyMaterial(KeyboardDisplayMode mode, bool initiallyEnabled)
        {
            var origin = new GameObject("XR Origin");
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(origin.transform, false);
            var camera = new GameObject("Main Camera");
            camera.transform.SetParent(offset.transform, false);
            var switchObject = new GameObject("Passthrough switch");
            var passthrough = switchObject.AddComponent<Light>();
            passthrough.enabled = initiallyEnabled;
            var host = new GameObject("Keyboard");
            var keyboard = host.AddComponent<VirtualPianoKeyboard>();
            typeof(VirtualPianoKeyboard).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(keyboard, null);
            try
            {
                keyboard.SetDisplayMode(mode);
                var originalMaterial = keyboard.TryGetKey(60, out var c4) ? c4.Renderer.sharedMaterial : null;
                var originPosition = origin.transform.position;
                var offsetPosition = offset.transform.position;
                var cameraPosition = camera.transform.position;
                var session = new CalibrationPassthroughSession(passthrough, keyboard);
                session.Begin();
                session.Begin();
                Assert.That(session.Active, Is.True);
                Assert.That(passthrough.enabled, Is.True);
                Assert.That(keyboard.CalibrationTransparency, Is.True);
                Assert.That(c4.Opacity, Is.EqualTo(0.35f));
                Assert.That(c4.Renderer.sharedMaterial, Is.Not.SameAs(originalMaterial));
                Assert.That(c4.Renderer.sharedMaterial.renderQueue, Is.EqualTo(3000));
                var transparentMaterial = c4.Renderer.sharedMaterial;
                foreach (var note in new[] { keyboard.MinNote, 60, keyboard.MaxNote })
                {
                    Assert.That(keyboard.TryGetKey(note, out var key), Is.True);
                    Assert.That(key.Renderer.sharedMaterial, Is.SameAs(transparentMaterial));
                    Assert.That(key.Opacity, Is.EqualTo(0.35f));
                    var block = new MaterialPropertyBlock();
                    key.Renderer.GetPropertyBlock(block);
                    Assert.That(block.GetColor("_BaseColor").a, Is.EqualTo(0.35f));
                }
                keyboard.SetDisplayMode(mode == KeyboardDisplayMode.Full88Keys
                    ? KeyboardDisplayMode.Research13Keys : KeyboardDisplayMode.Full88Keys);
                Assert.That(keyboard.TryGetKey(60, out c4), Is.True);
                Assert.That(c4.Opacity, Is.EqualTo(0.35f));
                session.End();
                session.End();
                Assert.That(passthrough.enabled, Is.EqualTo(initiallyEnabled));
                Assert.That(keyboard.CalibrationTransparency, Is.False);
                Assert.That(c4.Opacity, Is.EqualTo(1f));
                Assert.That(c4.Renderer.sharedMaterial, Is.SameAs(originalMaterial));
                Assert.That(origin.transform.position, Is.EqualTo(originPosition));
                Assert.That(offset.transform.position, Is.EqualTo(offsetPosition));
                Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
                session.Begin();
                Assert.That(c4.Opacity, Is.EqualTo(0.35f));
                session.End();
                Assert.That(c4.Opacity, Is.EqualTo(1f));
                Assert.That(keyboard.KeyboardGeometry.childCount, Is.EqualTo(keyboard.KeyCount));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(switchObject);
                Object.DestroyImmediate(origin);
            }
        }
    }
}
