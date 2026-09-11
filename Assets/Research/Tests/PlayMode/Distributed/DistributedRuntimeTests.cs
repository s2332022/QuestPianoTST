using System;
using System.Collections;
using System.IO;
using System.Net;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;
using UnityEngine.TestTools;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class DistributedRuntimeTests
    {
        [Test] public void HostAndQuestRuntimeComponentsCanBeCreatedWithoutPlatformMidi()
        {
            var host=new GameObject("host");host.SetActive(false);Assert.That(host.AddComponent<DistributedPcHost>(),Is.Not.Null);var quest=new GameObject("quest");quest.SetActive(false);Assert.That(quest.AddComponent<DistributedQuestClient>(),Is.Not.Null);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(quest);
        }
        [UnityTest] public IEnumerator UdpLoopbackDeliversBinaryMidiPacket()
        {
            using var a=new UdpTransport();using var b=new UdpTransport();a.Start(0);b.Start(0);var bytes=new byte[128];var m=new MidiMessage(1,1,"loopback",MidiEventType.NoteOn,1,60,100,-1,-1);var n=NetworkProtocolV1.WriteMidi(bytes,1,1,Guid.Empty,in m,0);Assert.That(a.Send(bytes,n,new IPEndPoint(IPAddress.Loopback,b.LocalPort)),Is.True);ReceivedDatagram received=null;var limit=Time.realtimeSinceStartup+2f;while(Time.realtimeSinceStartup<limit&&!b.TryDequeue(out received))yield return null;Assert.That(received,Is.Not.Null);Assert.That(NetworkProtocolV1.TryReadMidi(received.Data,received.Length,out var decoded),Is.True);Assert.That(decoded.Note,Is.EqualTo(60));b.Recycle(received);
        }
        [UnityTest] public IEnumerator NetworkMidiReachesKeyboardAndDisconnectReleasesAllKeys()
        {
            var go=new GameObject("network-midi",typeof(VirtualPianoKeyboard),typeof(NetworkMidiInput));var keyboard=go.GetComponent<VirtualPianoKeyboard>();var input=go.GetComponent<NetworkMidiInput>();input.MessageReceived+=x=>keyboard.ApplyMidi(in x);var packet=new MidiPacket{Header=new PacketHeader(PacketType.Midi,1,1,Guid.Empty,18),EventIndex=1,EventType=MidiEventType.NoteOn,Channel=1,Note=60,Velocity=100,Control=255,Value=0};input.Enqueue(in packet);yield return null;Assert.That(keyboard.State.IsPressed(60),Is.True);input.Disconnect();Assert.That(keyboard.State.IsPressed(60),Is.False);UnityEngine.Object.Destroy(go);yield return null;
        }
        [UnityTest] public IEnumerator RecorderCreatesAndClosesAllSessionFiles()
        {
            var go=new GameObject("recorder",typeof(DistributedSessionRecorder));var recorder=go.GetComponent<DistributedSessionRecorder>();Assert.That(recorder.Begin(Guid.NewGuid(),"127.0.0.1","test"),Is.True);recorder.End("test");Assert.That(recorder.State,Is.EqualTo(DistributedSessionState.Completed));var expected=new[]{"session_metadata.json","quest_hand_joints.csv","quest_head_pose.csv","pc_midi_events.csv","quest_keyboard_state.csv","clock_sync.csv","network_diagnostics.csv","session_summary.json"};foreach(var file in expected)Assert.That(File.Exists(Path.Combine(recorder.SessionPath,file)),Is.True,file);UnityEngine.Object.Destroy(go);yield return null;
        }
    }
}
