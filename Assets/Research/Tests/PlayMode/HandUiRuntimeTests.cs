using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class HandUiRuntimeTests
    {
        GameObject m_CameraObject;
        GameObject m_UiObject;
        GameObject m_EventObject;
        Camera m_Camera;
        DistributedQuestUi m_Ui;
        MinimalHandUiInputModule m_Module;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_CameraObject = new GameObject("Main Camera", typeof(Camera));
            m_CameraObject.tag = "MainCamera";
            m_Camera = m_CameraObject.GetComponent<Camera>();
            m_Camera.nearClipPlane = 0.01f;
            m_UiObject = new GameObject("Research UI", typeof(RectTransform), typeof(DistributedQuestUi));
            m_EventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(MinimalHandUiInputModule));
            m_Ui = m_UiObject.GetComponent<DistributedQuestUi>();
            m_Module = m_EventObject.GetComponent<MinimalHandUiInputModule>();
            yield return null;
            m_Ui.PlaceAtCamera(m_Camera);
            m_Module.ConfigureForTests(m_Camera, m_Ui);
            m_Module.ConfigurePinch(false, 0.025f, 0.035f);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(m_EventObject);
            Object.Destroy(m_UiObject);
            Object.Destroy(m_CameraObject);
            yield return null;
        }

        [Test]
        public void RuntimeStructure_HasOneEventSystemRaycasterCorrectCameraAndInteractableButtons()
        {
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(m_Ui.WorldCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(m_Ui.WorldCanvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(m_Ui.WorldCanvas.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(m_Ui.LegacyRaycaster.enabled, Is.True);
            Assert.That(m_Ui.XriRaycaster.enabled, Is.False);
            Assert.That(m_Ui.CanvasGroup.interactable, Is.True);
            Assert.That(m_Ui.CanvasGroup.blocksRaycasts, Is.True);
            Assert.That(m_Ui.WorldCanvas.worldCamera, Is.SameAs(m_Camera));
            Assert.That(m_EventObject.GetComponents<BaseInputModule>().Single(), Is.SameAs(m_Module));
            foreach (var button in m_Ui.GetComponentsInChildren<Button>(true))
            {
                Assert.That(button.interactable, Is.True, button.name);
                Assert.That(button.GetComponent<Image>().raycastTarget, Is.True, button.name);
                Assert.That(button.GetComponentsInChildren<TMP_Text>(true).All(x => !x.raycastTarget), Is.True, button.name);
            }
            Assert.That(m_Ui.HandInputDiagnosticText.raycastTarget, Is.False);
            Assert.That(m_Ui.HandCursorImage.raycastTarget, Is.False);
            Assert.That(m_UiObject.transform.Find("UI Background").GetComponent<Image>().raycastTarget, Is.False);
        }

        [Test]
        public void IndexTipProjection_DetectsButtonAndCanvasBehindDoesNot()
        {
            var button = Button("RECENTER UI");
            Sample(button, 0.040f);
            Assert.That(m_Module.CurrentTarget, Is.SameAs(button.gameObject),
                $"screen={m_Module.LastScreenPoint} valid={m_Module.LastScreenValid} overCanvas={m_Module.LastOverCanvas} raycasts={m_Module.LastRaycastCount}");

            m_Ui.transform.position = m_Camera.transform.position - m_Camera.transform.forward;
            LogAssert.Expect(LogType.Error, new Regex("Canvas is not in front"));
            Sample(button, 0.040f);
            Assert.That(m_Module.CurrentTarget, Is.Null);
        }

        [Test]
        public void PinchDownHoldAndUp_SendOneDownOneUpAndOneClick()
        {
            var button = Button("HAND GPU");
            var invoked = 0;
            button.onClick.AddListener(() => ++invoked);
            Sample(button, 0.040f);
            Sample(button, 0.025f);
            Assert.That(m_Module.PointerDownCount, Is.EqualTo(1));
            Sample(button, 0.020f);
            Sample(button, 0.030f);
            Assert.That(m_Module.PointerDownCount, Is.EqualTo(1));
            Assert.That(m_Module.ClickCount, Is.Zero);
            Sample(button, 0.035f);
            Assert.That(m_Module.PointerUpCount, Is.EqualTo(1));
            Assert.That(m_Module.ClickCount, Is.EqualTo(1));
            Assert.That(invoked, Is.EqualTo(1));
        }

        [Test]
        public void PinchUpOnDifferentButton_ReleasesWithoutClick()
        {
            var down = Button("HAND GPU");
            var up = Button("HAND GAMEOBJECTS");
            Sample(down, 0.040f);
            Sample(down, 0.020f);
            Sample(up, 0.020f);
            Sample(up, 0.040f);
            Assert.That(m_Module.PointerDownCount, Is.EqualTo(1));
            Assert.That(m_Module.PointerUpCount, Is.EqualTo(1));
            Assert.That(m_Module.ClickCount, Is.Zero);
            Assert.That(m_Module.PinchPressed, Is.False);
        }

        [Test]
        public void TrackingLoss_CancelsPressAndRequiresOpenBeforeAnotherDown()
        {
            var button = Button("HAND GPU");
            Sample(button, 0.040f);
            Sample(button, 0.020f);
            var position = Center(button);
            m_Module.ProcessSample(false, false, false, new Pose(position, Quaternion.identity), Pose.identity);
            Assert.That(m_Module.PointerUpCount, Is.EqualTo(1));
            Assert.That(m_Module.ClickCount, Is.Zero);
            Assert.That(m_Module.PinchPressed, Is.False);
            Sample(button, 0.020f);
            Assert.That(m_Module.PointerDownCount, Is.EqualTo(1));
            Sample(button, 0.040f);
            Sample(button, 0.020f);
            Assert.That(m_Module.PointerDownCount, Is.EqualTo(2));
        }

        [Test]
        public void LeavingUi_CancelsPressAndCursorAndDoesNotRetriggerClosedPinch()
        {
            var button = Button("HAND GPU");
            Sample(button, 0.040f);
            Sample(button, 0.020f);
            var outside = m_Camera.transform.position + m_Camera.transform.forward + m_Camera.transform.right * 10f;
            m_Module.ProcessSample(true, true, true, new Pose(outside, Quaternion.identity),
                new Pose(outside + m_Camera.transform.right * 0.020f, Quaternion.identity));
            Assert.That(m_Module.PointerUpCount, Is.EqualTo(1));
            Assert.That(m_Module.ClickCount, Is.Zero);
            Assert.That(m_Module.PinchPressed, Is.False);
            Assert.That(m_Ui.HandCursor.gameObject.activeSelf, Is.False);
            Sample(button, 0.020f);
            Assert.That(m_Module.PointerDownCount, Is.EqualTo(1));
        }

        [Test]
        public void Cursor_IsWhiteOnUiYellowOnButtonGreenDuringPinchAndBlueAfterClick()
        {
            var canvasRect = (RectTransform)m_Ui.WorldCanvas.transform;
            var emptyUiPoint = canvasRect.TransformPoint(new Vector3(300f, 300f, 0f));
            Sample(emptyUiPoint, 0.040f);
            Assert.That(m_Ui.HandCursor.gameObject.activeSelf, Is.True);
            AssertColor(m_Ui.HandCursorImage.color, Color.white);

            var button = Button("HAND GPU");
            Sample(button, 0.040f);
            AssertColor(m_Ui.HandCursorImage.color, Color.yellow);
            Sample(button, 0.020f);
            AssertColor(m_Ui.HandCursorImage.color, Color.green);
            Sample(button, 0.040f);
            AssertColor(m_Ui.HandCursorImage.color, Color.blue);
        }

        [Test]
        public void Recenter_KeepsUiInFrontAtSpecifiedRelativePosition()
        {
            m_Camera.transform.SetPositionAndRotation(new Vector3(2f, 1.5f, -3f), Quaternion.Euler(0f, 135f, 0f));
            m_Ui.PlaceAtCamera(m_Camera);
            var relative = m_Camera.transform.InverseTransformPoint(m_Ui.transform.position);
            Assert.That(relative.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(relative.y, Is.EqualTo(-0.1f).Within(0.0001f));
            Assert.That(relative.z, Is.EqualTo(1.1f).Within(0.0001f));
        }

        Button Button(string name) => m_Ui.GetComponentsInChildren<Button>(true).Single(x => x.name == name);

        void Sample(Button button, float distance) => Sample(Center(button), distance);

        void Sample(Vector3 indexPosition, float distance)
        {
            var thumbPosition = indexPosition + m_Camera.transform.right * distance;
            m_Module.ProcessSample(true, true, true,
                new Pose(indexPosition, Quaternion.identity), new Pose(thumbPosition, Quaternion.identity));
        }

        static Vector3 Center(Button button)
        {
            var rect = (RectTransform)button.transform;
            return rect.TransformPoint(rect.rect.center);
        }

        static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
        }
    }
}
