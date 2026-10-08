using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class PerformanceUiRuntimeTests
    {
        GameObject m_UiGo, m_MidiGo, m_KeyboardGo, m_BleGo;
        DistributedQuestUi m_Ui;
        NetworkMidiInput m_Udp;
        BleMidiInput m_Ble;
        VirtualPianoKeyboard m_Keyboard;
        string m_BleLog;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_MidiGo = new GameObject("Safety UDP test", typeof(NetworkMidiInput));
            m_Udp = m_MidiGo.GetComponent<NetworkMidiInput>();
            m_KeyboardGo = new GameObject("Safety keyboard test", typeof(VirtualPianoKeyboard));
            m_Keyboard = m_KeyboardGo.GetComponent<VirtualPianoKeyboard>();
            m_Udp.MessageReceived += ApplyUdp;
            m_BleGo = new GameObject("Safety BLE test", typeof(BleMidiInput), typeof(BleMidiDiagnostics));
            m_Ble = m_BleGo.GetComponent<BleMidiInput>();
            m_BleLog = m_Ble.LogPath;
            m_Keyboard.BindBleMidi(m_Ble);
            m_UiGo = new GameObject("Safety UI test", typeof(RectTransform), typeof(DistributedQuestUi));
            m_Ui = m_UiGo.GetComponent<DistributedQuestUi>();
            yield return null;
            yield return null;
        }

        void ApplyUdp(MidiMessage m) => m_Keyboard.ApplyMidi(in m);
        static MidiMessage Note(MidiEventType type, double time = -987654) => new MidiMessage(time, 1, "test", type, 1, 60, type == MidiEventType.NoteOn ? 90 : 0, -1, -1);
        void Ble(MidiMessage m) => typeof(BleMidiInput).GetMethod("OnMessage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(m_Ble, new object[] { m });
        void Udp(MidiEventType type)
        {
            var packet = new MidiPacket
            {
                Header = new PacketHeader(PacketType.Midi, 1, -987654, Guid.Empty, 0),
                EventIndex = 1, EventType = type, Channel = 1, Note = 60,
                Velocity = (byte)(type == MidiEventType.NoteOn ? 90 : 0), Control = 255
            };
            Assert.That(m_Udp.Enqueue(in packet), Is.True);
            m_Udp.FlushPending();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Udp.MessageReceived -= ApplyUdp;
            UnityEngine.Object.Destroy(m_UiGo);
            UnityEngine.Object.Destroy(m_BleGo);
            UnityEngine.Object.Destroy(m_KeyboardGo);
            UnityEngine.Object.Destroy(m_MidiGo);
            yield return null;
            if (!string.IsNullOrEmpty(m_BleLog) && File.Exists(m_BleLog)) File.Delete(m_BleLog);
        }

        [UnityTest]
        public IEnumerator Udp_LocksImmediatelyWithoutChangingMidiOrKeyState_ThenUnlocks()
        {
            MidiMessage observed = default;
            m_Udp.MessageReceived += m => observed = m;
            Udp(MidiEventType.NoteOn);
            Assert.That(observed.AbsoluteTimeSeconds, Is.EqualTo(-987654));
            Assert.That(m_Ui.Safety.Locked, Is.True);
            Assert.That(m_Ui.CanvasGroup.interactable, Is.False);
            Assert.That(m_Ui.CanvasGroup.blocksRaycasts, Is.False);
            Assert.That(m_Ui.CanvasGroup.alpha, Is.EqualTo(.45f));
            Assert.That(m_Keyboard.HasActiveMidiNotes, Is.True);
            Assert.That(m_Keyboard.TryGetKey(60, out var key) && key.Pressed, Is.True);
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(m_Ui.Safety.Locked, Is.True, "Held keys keep UI locked beyond the cooldown.");
            Udp(MidiEventType.NoteOff);
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(m_Ui.Safety.Locked, Is.False);
            Assert.That(m_Keyboard.HasActiveMidiNotes, Is.False);
        }

        [Test]
        public void Ble_LocksAndCancelsConfirmation_WithoutChangingEventTimestamp()
        {
            var calls = 0;
            m_Ui.Safety.Run("Reconnect", () => ++calls, true);
            Assert.That(m_Ui.Safety.ConfirmationPending, Is.True);
            MidiMessage observed = default;
            m_Ble.MessageReceived += m => observed = m;
            Ble(Note(MidiEventType.NoteOn, 123.45));
            Assert.That(m_Ui.Safety.Locked, Is.True);
            Assert.That(m_Ui.Safety.ConfirmationPending, Is.False);
            m_Ui.Safety.ConfirmPending();
            Assert.That(calls, Is.Zero);
            Assert.That(observed.AbsoluteTimeSeconds, Is.EqualTo(123.45));
            Assert.That(m_Keyboard.HasActiveMidiNotes, Is.True);
        }

        [Test]
        public void Confirmation_RequiresSeparateAction_AndExecutesExactlyOnce()
        {
            var calls = 0;
            m_Ui.Safety.Run("Reconnect", () => ++calls, true);
            Assert.That(calls, Is.Zero);
            m_Ui.Safety.Run("Another action", () => calls += 10);
            Assert.That(calls, Is.Zero);
            m_UiGo.transform.Find("UI operation confirmation/CONFIRM").GetComponent<Button>().onClick.Invoke();
            m_Ui.Safety.ConfirmPending();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(m_Ui.Safety.ConfirmationPending, Is.False);
        }

        [Test]
        public void CancelAndExpiry_DoNotExecutePendingAction()
        {
            var calls = 0;
            m_Ui.Safety.Run("Reset", () => ++calls, true);
            m_Ui.Safety.CancelPending();
            m_Ui.Safety.ConfirmPending();
            m_Ui.Safety.Run("Reset", () => ++calls, true);
            typeof(PerformanceUiSafety).GetField("m_ExpiresAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(m_Ui.Safety, -1d);
            m_Ui.Safety.ConfirmPending();
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void BleConnectAndDisconnect_UseConfirmationAndInheritMainLock()
        {
            var panel = m_UiGo.transform.Find("BLE diagnostic panel");
            Assert.That(panel, Is.Not.Null);
            panel.Find("CONNECT").GetComponent<Button>().onClick.Invoke();
            Assert.That(m_Ui.Safety.ConfirmationPending, Is.True);
            m_Ui.Safety.CancelPending();
            panel.Find("DISCONNECT").GetComponent<Button>().onClick.Invoke();
            Assert.That(m_Ui.Safety.ConfirmationPending, Is.True);
            Ble(Note(MidiEventType.NoteOff));
            Assert.That(panel.Find("CONNECT").GetComponent<Button>().IsInteractable(), Is.False);
            Assert.That(m_Ui.Safety.ConfirmationPending, Is.False);
        }

        [UnityTest]
        public IEnumerator CalibrationChanges_ReanchorWorldPanel_WithoutMovingKeyboard()
        {
            var cameraGo = new GameObject("Safety placement camera", typeof(Camera));
            try
            {
                var root = m_Keyboard.KeyboardRoot;
                root.SetPositionAndRotation(new Vector3(2, 1, 4), Quaternion.Euler(0, 70, 0));
                m_Keyboard.KeyboardGeometry.localScale = new Vector3(1.5f, 1, .8f);
                m_Ui.PlaceAtCamera(cameraGo.GetComponent<Camera>());
                var before = m_Ui.transform.position;
                cameraGo.transform.position += Vector3.one;
                yield return null;
                Assert.That(Vector3.Distance(before, m_Ui.transform.position), Is.LessThan(.00001f), "Not head-locked.");
                root.position += Vector3.right;
                yield return null;
                Assert.That(Vector3.Distance(before + Vector3.right, m_Ui.transform.position), Is.LessThan(.00001f));
                Assert.That(root.position, Is.EqualTo(new Vector3(3, 1, 4)));
                Assert.That(m_Ui.transform.localScale.x, Is.EqualTo(.00065f));
            }
            finally { UnityEngine.Object.Destroy(cameraGo); }
        }

        [Test]
        public void MidiLock_BlocksMainButtonsAndIndependentIpCanvas()
        {
            m_Ui.OpenIpKeyboard();
            var originalIp = m_Ui.EditingIp;
            Udp(MidiEventType.NoteOn);
            m_UiGo.transform.Find("DISCONNECT").GetComponent<Button>().onClick.Invoke();
            m_Ui.HandleIpKey("CLEAR");
            Assert.That(m_Ui.EditingIp, Is.EqualTo(originalIp));
            Assert.That(m_Ui.ApplyIpKeyboardValue(), Is.False);
            Assert.That(m_Ui.Safety.ConfirmationPending, Is.False);
            foreach (var button in m_Ui.IpKeyboardCanvas.GetComponentsInChildren<Button>(true))
                Assert.That(button.IsInteractable(), Is.False);
        }
    }
}
