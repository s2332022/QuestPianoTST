using System.Collections;
using System.Linq;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class QuestPresentationRuntimeTests
    {
        [UnityTest]
        public IEnumerator QuestUi_BuildsOnceWithExplicitVisibleTmpAssets()
        {
            var originObject = new GameObject("XR Origin");
            originObject.SetActive(false);
            var origin = originObject.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(UnityEngine.InputSystem.XR.TrackedPoseDriver));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            origin.CameraFloorOffsetObject = offset;
            origin.Camera = cameraObject.GetComponent<Camera>();
            originObject.SetActive(true);
            var uiObject = new GameObject("Research UI", typeof(RectTransform), typeof(DistributedQuestUi));

            yield return null;

            var ui = uiObject.GetComponent<DistributedQuestUi>();
            var canvases = uiObject.GetComponents<Canvas>();
            Assert.That(Object.FindObjectsByType<DistributedQuestUi>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(canvases.Length, Is.EqualTo(1));
            Assert.That(ui.FontAsset, Is.Not.Null);
            Assert.That(ui.FontAsset.material, Is.Not.Null);
            Assert.That(ui.FontAsset.material.shader, Is.Not.Null);
            Assert.That(ui.StatusText, Is.Not.Null);
            Assert.That(ui.StatusText.font, Is.SameAs(ui.FontAsset));
            Assert.That(ui.StatusText.fontSharedMaterial, Is.SameAs(ui.FontAsset.material));
            Assert.That(ui.StatusText.color.a, Is.EqualTo(1f));
            Assert.That(ui.StatusText.rectTransform.rect.width, Is.GreaterThan(0f));
            Assert.That(ui.StatusText.rectTransform.rect.height, Is.GreaterThan(0f));
            Assert.That(uiObject.GetComponentsInChildren<Mask>(true), Is.Empty);
            Assert.That(uiObject.GetComponentsInChildren<RectMask2D>(true), Is.Empty);

            var required = new[] { "RECENTER UI", "HAND GAMEOBJECTS", "HAND GPU" };
            var buttons = uiObject.GetComponentsInChildren<Button>(true);
            foreach (var label in required)
            {
                var button = buttons.Single(x => x.name == label);
                var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
                Assert.That(text.text, Is.EqualTo(label));
                Assert.That(text.font, Is.SameAs(ui.FontAsset));
                Assert.That(text.color.a, Is.EqualTo(1f));
                Assert.That(text.rectTransform.rect.width, Is.GreaterThan(0f));
                Assert.That(text.rectTransform.rect.height, Is.GreaterThan(0f));
            }

            Object.Destroy(uiObject);
            Object.Destroy(originObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator VirtualPiano_CreatesOnlyOneFullKeyboardRoot()
        {
            var pianoHost = new GameObject("Piano Root");
            var runtime = new GameObject("Research Runtime", typeof(VirtualPianoKeyboard));
            yield return null;
            var keyboard = runtime.GetComponent<VirtualPianoKeyboard>();
            keyboard.ConfigureMinimal(pianoHost.transform);
            Assert.That(Object.FindObjectsByType<VirtualPianoKeyboard>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(keyboard.KeyboardRoot.parent, Is.SameAs(pianoHost.transform));
            Assert.That(keyboard.KeyboardRoot.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(keyboard.KeyboardRoot.childCount, Is.EqualTo(1));
            var geometry = keyboard.KeyboardRoot.Find("Keyboard Geometry");
            Assert.That(geometry, Is.Not.Null);
            Assert.That(geometry.childCount, Is.EqualTo(88));
            Object.Destroy(runtime);
            Object.Destroy(pianoHost);
            yield return null;
        }
    }
}
