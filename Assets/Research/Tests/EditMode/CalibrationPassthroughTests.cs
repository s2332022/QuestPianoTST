using System.Reflection;
using NUnit.Framework;
using UnityEditor;
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
            var cameraComponent = camera.AddComponent<Camera>();
            cameraComponent.clearFlags = CameraClearFlags.Skybox;
            cameraComponent.backgroundColor = new Color(0.17f, 0.31f, 0.53f, 0.62f);
            cameraComponent.fieldOfView = 63f;
            cameraComponent.nearClipPlane = 0.07f;
            cameraComponent.farClipPlane = 31f;
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
                var cameraRotation = camera.transform.rotation;
                var cameraFov = cameraComponent.fieldOfView;
                var cameraNear = cameraComponent.nearClipPlane;
                var cameraFar = cameraComponent.farClipPlane;
                var cameraClearFlags = cameraComponent.clearFlags;
                var cameraColor = cameraComponent.backgroundColor;
                var session = new CalibrationPassthroughSession(passthrough, keyboard);
                Assert.That(session.Begin(cameraComponent, out var failureReason), Is.True, failureReason);
                Assert.That(session.Begin(cameraComponent, out failureReason), Is.True, failureReason);
                Assert.That(session.Active, Is.True);
                Assert.That(passthrough.enabled, Is.True);
                Assert.That(cameraComponent.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(cameraComponent.backgroundColor, Is.EqualTo(new Color(cameraColor.r, cameraColor.g, cameraColor.b, 0f)));
                Assert.That(keyboard.CalibrationTransparency, Is.False);
                session.MarkReady();
                session.MarkReady();
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
                Assert.That(camera.transform.rotation, Is.EqualTo(cameraRotation));
                Assert.That(cameraComponent.fieldOfView, Is.EqualTo(cameraFov));
                Assert.That(cameraComponent.nearClipPlane, Is.EqualTo(cameraNear));
                Assert.That(cameraComponent.farClipPlane, Is.EqualTo(cameraFar));
                Assert.That(cameraComponent.clearFlags, Is.EqualTo(cameraClearFlags));
                Assert.That(cameraComponent.backgroundColor, Is.EqualTo(cameraColor));
                Assert.That(session.Begin(cameraComponent, out failureReason), Is.True, failureReason);
                session.MarkReady();
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

        [Test]
        public void QuestMobileUrp_DisablesHdrAndKeepsAutoIntermediateTexture()
        {
            var pipelineAsset = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/Mobile_RPAsset.asset");
            Assert.That(pipelineAsset, Is.Not.Null);
            var pipelineProperties = new SerializedObject(pipelineAsset);
            Assert.That(pipelineProperties.FindProperty("m_SupportsHDR").boolValue, Is.False);

            var renderers = pipelineProperties.FindProperty("m_RendererDataList");
            Assert.That(renderers, Is.Not.Null);
            Assert.That(renderers.arraySize, Is.GreaterThan(0));
            var rendererAsset = renderers.GetArrayElementAtIndex(0).objectReferenceValue;
            Assert.That(rendererAsset, Is.Not.Null);
            var rendererProperties = new SerializedObject(rendererAsset);
            Assert.That(rendererProperties.FindProperty("m_IntermediateTextureMode").intValue, Is.Zero);
            Assert.That(rendererProperties.FindProperty("m_RendererFeatures").arraySize, Is.Zero);
        }
        [TestCase(false, false, "Camera subsystem was not created", 0)]
        [TestCase(true, false, "Camera subsystem failed to start", 0)]
        [TestCase(true, true, "Passthrough Composition Layer was not created", 0)]
        [TestCase(true, true, "Multiple Passthrough Composition Layers were found", 2)]
        public void ReadinessFailure_IsReportedWithoutStartingCapture(bool subsystemPresent, bool subsystemRunning,
            string expected, int layerCount)
        {
            var failure = CalibrationPassthroughReadiness.GetFailureReason(true, true,
                subsystemPresent, subsystemRunning, layerCount, layerCount == 1, -1, "Alpha");
            Assert.That(failure, Is.EqualTo(expected));
        }

        [Test]
        public void Session_RejectsMissingCameraOrManagerWithoutChangingCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Depth;
            camera.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            var session = new CalibrationPassthroughSession(null, null);
            try
            {
                Assert.That(session.Begin(camera, out var reason), Is.False);
                Assert.That(reason, Is.EqualTo("ARCameraManager is missing from Main Camera"));
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Depth));
                Assert.That(camera.backgroundColor.a, Is.EqualTo(0.8f));
                Assert.That(session.Begin(null, out reason), Is.False);
                Assert.That(reason, Is.EqualTo("Main Camera is unavailable"));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
