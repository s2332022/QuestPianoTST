using System.Collections;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class XriHandUiModeTests
    {
        GameObject m_ManagerObject;
        GameObject m_EventObject;
        GameObject m_InteractorObject;
        GameObject m_UiObject;
        GameObject m_ControllerObject;
        GameObject m_CameraObject;
        MinimalHandUiInputModule m_Legacy;
        XRUIInputModule m_XrUi;
        NearFarInteractor m_Interactor;
        DistributedQuestUi m_Ui;
        XriHandUiInputController m_Controller;
        Camera m_Camera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_ManagerObject = new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
            m_CameraObject = new GameObject("Main Camera", typeof(Camera));
            m_CameraObject.tag = "MainCamera";
            m_Camera = m_CameraObject.GetComponent<Camera>();
            m_EventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(MinimalHandUiInputModule), typeof(XRUIInputModule));
            m_InteractorObject = new GameObject("Right Hand UI Near-Far Interactor");
            m_InteractorObject.SetActive(false);
            m_Interactor = m_InteractorObject.AddComponent<NearFarInteractor>();
            m_Interactor.handedness = InteractorHandedness.Right;
            m_Interactor.enableUIInteraction = true;
            m_UiObject = new GameObject("Research UI", typeof(RectTransform), typeof(DistributedQuestUi));
            m_ControllerObject = new GameObject("XRI Hand UI Controller");
            m_Legacy = m_EventObject.GetComponent<MinimalHandUiInputModule>();
            m_XrUi = m_EventObject.GetComponent<XRUIInputModule>();
            m_Ui = m_UiObject.GetComponent<DistributedQuestUi>();
            m_Controller = m_ControllerObject.AddComponent<XriHandUiInputController>();
            m_Controller.Configure(m_Legacy, m_Interactor, m_XrUi, null);
            m_Controller.ApplyMode();
            yield return null;
            m_Ui.PlaceAtCamera(m_Camera);
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(m_ControllerObject);
            Object.Destroy(m_UiObject);
            Object.Destroy(m_InteractorObject);
            Object.Destroy(m_EventObject);
            Object.Destroy(m_ManagerObject);
            Object.Destroy(m_CameraObject);
            yield return null;
        }

        [Test]
        public void DefaultMode_IsXriStandardAndOnlyXriPathIsEnabled()
        {
            Assert.That(m_Controller.Mode, Is.EqualTo(UiInputMode.XriStandard));
            Assert.That(m_Legacy.enabled, Is.False);
            Assert.That(m_XrUi.enabled, Is.True);
            Assert.That(m_InteractorObject.activeSelf, Is.True);
            Assert.That(m_Controller.BothInputModesActive, Is.False);
            Assert.That(m_Ui.XriRaycaster, Is.Not.Null);
            Assert.That(m_Ui.XriRaycaster.enabled, Is.True);
            Assert.That(m_Ui.LegacyRaycaster.enabled, Is.False);
            Assert.That(m_Ui.HandCursor.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void LegacyDiagnostic_DisablesXriInteractorAndModule()
        {
            m_Controller.SetMode(UiInputMode.LegacyDiagnostic);
            Assert.That(m_Legacy.enabled, Is.True);
            Assert.That(m_XrUi.enabled, Is.False);
            Assert.That(m_InteractorObject.activeSelf, Is.False);
            Assert.That(m_Controller.BothInputModesActive, Is.False);
            Assert.That(m_Ui.LegacyRaycaster.enabled, Is.True);
            Assert.That(m_Ui.XriRaycaster.enabled, Is.False);
        }

        [Test]
        public void RuntimeCoreObjects_AreUniqueAndRightInteractorSupportsUi()
        {
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRInteractionManager>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRUIInputModule>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(m_Interactor.handedness, Is.EqualTo(InteractorHandedness.Right));
            Assert.That(m_Interactor.enableUIInteraction, Is.True);
        }

        [UnityTest]
        public IEnumerator XrUiModule_HoverSelectHoldAndRelease_ClicksExactlyOnce()
        {
            var button = m_Ui.transform.Find("CONNECT").GetComponent<UnityEngine.UI.Button>();
            var counter = button.gameObject.AddComponent<UiEventCounter>();
            var start = m_Camera.transform.position;
            var end = ((RectTransform)button.transform).TransformPoint(((RectTransform)button.transform).rect.center);
            var interactor = new TestUiInteractor(start, end);
            m_XrUi.RegisterInteractor(interactor);

            yield return null;
            Assert.That(counter.EnterCount, Is.EqualTo(1), "The standard tracked-device ray should hover the Button.");

            interactor.Select = true;
            yield return null;
            Assert.That(counter.DownCount, Is.EqualTo(1));
            yield return null;
            yield return null;
            Assert.That(counter.DownCount, Is.EqualTo(1));
            Assert.That(counter.ClickCount, Is.Zero);

            interactor.Select = false;
            yield return null;
            Assert.That(counter.UpCount, Is.EqualTo(1));
            Assert.That(counter.ClickCount, Is.EqualTo(1));
            yield return null;
            Assert.That(counter.ClickCount, Is.EqualTo(1));

            m_XrUi.UnregisterInteractor(interactor);
        }

        [UnityTest]
        public IEnumerator TrackingLossDuringSelect_ReleasesWithoutClick()
        {
            var button = m_Ui.transform.Find("CONNECT").GetComponent<UnityEngine.UI.Button>();
            var counter = button.gameObject.AddComponent<UiEventCounter>();
            var start = m_Camera.transform.position;
            var end = ((RectTransform)button.transform).TransformPoint(((RectTransform)button.transform).rect.center);
            var interactor = new TestUiInteractor(start, end);
            m_XrUi.RegisterInteractor(interactor);
            yield return null;
            interactor.Select = true;
            yield return null;
            Assert.That(counter.DownCount, Is.EqualTo(1));

            interactor.Tracked = false;
            interactor.Select = false;
            yield return null;
            Assert.That(counter.UpCount, Is.EqualTo(1));
            Assert.That(counter.ClickCount, Is.Zero);
            m_XrUi.UnregisterInteractor(interactor);
        }

        [UnityTest]
        public IEnumerator MidiDuringRaySelect_CancelsButtonAction()
        {
            var midiGo = new GameObject("Ray lock MIDI test", typeof(NetworkMidiInput));
            var midi = midiGo.GetComponent<NetworkMidiInput>();
            var button = m_Ui.transform.Find("HAND GPU").GetComponent<UnityEngine.UI.Button>();
            var calls = 0;
            button.onClick.AddListener(() => ++calls);
            var end = ((RectTransform)button.transform).TransformPoint(((RectTransform)button.transform).rect.center);
            var ray = new TestUiInteractor(m_Camera.transform.position, end);
            m_XrUi.RegisterInteractor(ray);
            try
            {
                yield return null; // Bind the newly created MIDI source and hover.
                ray.Select = true;
                yield return null;
                var packet = new MidiPacket { EventType = MidiEventType.NoteOn, Channel = 1, Note = 60, Velocity = 90 };
                midi.Enqueue(in packet);
                midi.FlushPending();
                Assert.That(m_Ui.Safety.Locked, Is.True);
                ray.Select = false;
                yield return null;
                Assert.That(calls, Is.Zero, "Release after a MIDI lock must not activate the pressed button.");
            }
            finally { m_XrUi.UnregisterInteractor(ray); Object.Destroy(midiGo); }
        }

        [UnityTest]
        public IEnumerator ConfirmationModal_BlocksUnderlyingRayAndAcceptsExplicitConfirm()
        {
            var calls = 0;
            m_Ui.Safety.Run("Reconnect", () => ++calls, true);
            var underlying = m_Ui.transform.Find("HAND GPU").GetComponent<UnityEngine.UI.Button>();
            var underlyingCalls = 0;
            underlying.onClick.AddListener(() => ++underlyingCalls);
            var end = ((RectTransform)underlying.transform).TransformPoint(((RectTransform)underlying.transform).rect.center);
            var ray = new TestUiInteractor(m_Camera.transform.position, end);
            m_XrUi.RegisterInteractor(ray);
            yield return null;
            ray.Select = true;
            yield return null;
            ray.Select = false;
            yield return null;
            Assert.That(underlyingCalls, Is.Zero);
            Assert.That(calls, Is.Zero);
            m_XrUi.UnregisterInteractor(ray);

            var confirm = (RectTransform)m_Ui.transform.Find("UI operation confirmation/CONFIRM");
            end = confirm.TransformPoint(confirm.rect.center);
            ray = new TestUiInteractor(m_Camera.transform.position, end);
            m_XrUi.RegisterInteractor(ray);
            yield return null;
            ray.Select = true;
            yield return null;
            ray.Select = false;
            yield return null;
            Assert.That(calls, Is.EqualTo(1));
            m_XrUi.UnregisterInteractor(ray);
        }

        sealed class TestUiInteractor : IUIInteractor
        {
            readonly Vector3 m_Start;
            readonly Vector3 m_End;

            public bool Select { get; set; }
            public bool Tracked { get; set; } = true;

            public TestUiInteractor(Vector3 start, Vector3 end)
            {
                m_Start = start;
                m_End = end;
            }

            public void UpdateUIModel(ref TrackedDeviceModel model)
            {
                model.interactionType = UIInteractionType.Ray;
                model.position = m_Start;
                model.orientation = Quaternion.LookRotation(m_End - m_Start);
                model.raycastPoints.Clear();
                if (Tracked)
                {
                    model.raycastPoints.Add(m_Start);
                    model.raycastPoints.Add(m_End + (m_End - m_Start).normalized * 0.1f);
                }
                model.select = Select;
            }

            public bool TryGetUIModel(out TrackedDeviceModel model)
            {
                model = TrackedDeviceModel.invalid;
                return false;
            }
        }

        sealed class UiEventCounter : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
        {
            public int EnterCount { get; private set; }
            public int DownCount { get; private set; }
            public int UpCount { get; private set; }
            public int ClickCount { get; private set; }

            public void OnPointerEnter(PointerEventData eventData) => ++EnterCount;
            public void OnPointerDown(PointerEventData eventData) => ++DownCount;
            public void OnPointerUp(PointerEventData eventData) => ++UpCount;
            public void OnPointerClick(PointerEventData eventData) => ++ClickCount;
        }
    }
}
