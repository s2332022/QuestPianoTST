using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class QuestSpatialPlacementTests
    {
        [Test]
        public void HmdGate_DoesNotCompleteBeforeMinimumTimeOrPoseUpdate()
        {
            var gate = new QuestHmdPoseGate(10d);
            Assert.That(gate.Observe(10.49d, true, true, true, Vector3.zero, Quaternion.identity), Is.False);
            Assert.That(gate.Observe(10.50d, true, true, true, Vector3.zero, Quaternion.identity), Is.False);
            Assert.That(gate.ConsecutiveFrames, Is.Zero);
            Assert.That(gate.Observe(10.51d, true, true, true, Vector3.zero, Quaternion.identity), Is.False);
            Assert.That(gate.ConsecutiveFrames, Is.Zero);
        }

        [Test]
        public void HmdGate_CompletesAfterThreeConsecutiveUpdatedFrames()
        {
            var gate = new QuestHmdPoseGate(0d);
            Assert.That(gate.Observe(0.5d, true, true, true, Vector3.zero, Quaternion.identity), Is.False);
            Assert.That(gate.Observe(0.6d, true, true, true, Vector3.right * 0.001f, Quaternion.identity), Is.False);
            Assert.That(gate.Observe(0.7d, true, true, true, Vector3.right * 0.002f, Quaternion.identity), Is.False);
            Assert.That(gate.Observe(0.8d, true, true, true, Vector3.right * 0.003f, Quaternion.identity), Is.True);
            Assert.That(gate.ConsecutiveFrames, Is.EqualTo(3));
        }

        [Test]
        public void HmdGate_ResetsConsecutiveCountWhenTrackingIsLost()
        {
            var gate = new QuestHmdPoseGate(0d);
            gate.Observe(0.5d, true, true, true, Vector3.zero, Quaternion.identity);
            gate.Observe(0.6d, true, true, true, Vector3.right, Quaternion.identity);
            Assert.That(gate.ConsecutiveFrames, Is.EqualTo(1));
            Assert.That(gate.Observe(0.7d, true, false, true, Vector3.right, Quaternion.identity), Is.False);
            Assert.That(gate.ConsecutiveFrames, Is.Zero);
        }

        [Test]
        public void UiAndPianoPlacement_UseSpecifiedCameraRelativeWorldPositions()
        {
            var cameraObject = new GameObject("XR Camera", typeof(Camera));
            var ui = new GameObject("UI");
            var piano = new GameObject("Piano");
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(1f, 1.6f, 2f), Quaternion.identity);
                QuestSpatialPlacement.PlaceUi(ui.transform, camera);
                QuestSpatialPlacement.PlacePiano(piano.transform, camera);
                var comparer = new Vector3EqualityComparer(0.00001f);
                Assert.That(camera.transform.InverseTransformPoint(ui.transform.position), Is.EqualTo(new Vector3(0f, -0.1f, 1.1f)).Using(comparer));
                Assert.That(camera.transform.InverseTransformPoint(piano.transform.position), Is.EqualTo(new Vector3(0f, -0.35f, 0.75f)).Using(comparer));
                Assert.That(ui.transform.up, Is.EqualTo(Vector3.up).Using(comparer));
                Assert.That(piano.transform.up, Is.EqualTo(Vector3.up).Using(comparer));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(ui);
                Object.DestroyImmediate(piano);
            }
        }

        [Test]
        public void TrackingOrigin_UsesCameraOffsetAndAppliesTransformOnce()
        {
            var originObject = new GameObject("XR Origin");
            originObject.SetActive(false);
            var xrOrigin = originObject.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            try
            {
                offset.transform.SetParent(originObject.transform, false);
                offset.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(UnityEngine.InputSystem.XR.TrackedPoseDriver));
                cameraObject.transform.SetParent(offset.transform, false);
                xrOrigin.CameraFloorOffsetObject = offset;
                xrOrigin.Camera = cameraObject.GetComponent<Camera>();
                originObject.SetActive(true);
                var trackingOrigin = ResearchServices.FindTrackingOrigin();
                Assert.That(trackingOrigin, Is.SameAs(offset.transform));
                var localWrist = new Vector3(0.1f, 0.2f, 0.4f);
                Assert.That(trackingOrigin.TransformPoint(localWrist), Is.EqualTo(new Vector3(0.1f, 1.45f, 0.4f)).Using(new Vector3EqualityComparer(0.00001f)));
            }
            finally
            {
                Object.DestroyImmediate(offset);
                Object.DestroyImmediate(originObject);
            }
        }

        [Test]
        public void ResearchFontAssetAndMaterialAndShaderExist()
        {
            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Assert.That(font, Is.Not.Null);
            Assert.That(font.material, Is.Not.Null);
            Assert.That(font.material.shader, Is.Not.Null);
        }
    }
}