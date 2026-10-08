using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using QuestPianoMotion.Research.Distributed;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class BleMidiIsolationTests
    {
        [UnityTest]
        public IEnumerator BleAdapterImplementsInterfaceAndReenablesWithoutAffectingNetworkKeyboard()
        {
            var go = new GameObject("BLE isolation", typeof(VirtualPianoKeyboard), typeof(NetworkMidiInput), typeof(BleMidiInput));
            var keyboard = go.GetComponent<VirtualPianoKeyboard>(); var network = go.GetComponent<NetworkMidiInput>(); var ble = go.GetComponent<BleMidiInput>();
            Assert.That(ble, Is.InstanceOf<IMidiInput>());
            network.MessageReceived += m => keyboard.ApplyMidi(in m);
            var packet = new MidiPacket { Header = new PacketHeader(PacketType.Midi, 1, 1, System.Guid.Empty, 18), EventIndex = 1,
                EventType = MidiEventType.NoteOn, Channel = 1, Note = 60, Velocity = 100, Control = 255, Value = 0 };
            network.Enqueue(in packet); yield return null;
            // Feed the real adapter's session without requiring an Android device in Editor.
            var session = (BleMidiSession)typeof(BleMidiInput).GetField("m_Session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(ble);
            var bleEvents = 0; ble.MessageReceived += _ => ++bleEvents;
            session.Begin(1, "BLE test");
            new BleMidiPacketParser().Parse(new byte[] { 0x80, 0x81, 0x90, 70, 100 }, 1, 2, s => session.Enqueue(s)); session.Flush();
            Assert.That(bleEvents, Is.EqualTo(1)); Assert.That(ble.LastEventText, Does.Contain("70"));
            Assert.That(keyboard.State.IsPressed(70), Is.False);
            ble.Disconnect(); ble.enabled = false; ble.enabled = true; yield return null;
            Assert.That(keyboard.State.IsPressed(60), Is.True); Assert.That(network.IsConnected, Is.True);
            network.Disconnect(); Assert.That(keyboard.State.IsPressed(60), Is.False);
            Object.Destroy(go); yield return null;
        }
    }
}
