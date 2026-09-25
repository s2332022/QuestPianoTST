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
        [UnityTest] public IEnumerator HostSendsKeyboardSnapshotImmediatelyAfterQuestConnection()
        {
            var sender=new UdpTransport();sender.Start(0);var pcPort=FindFreePort();var clockPort=FindFreePort();
            var go=new GameObject("snapshot-host");go.SetActive(false);var settings=go.AddComponent<DistributedSettings>();
            settings.pcReceivePort=pcPort;settings.clockSyncPort=clockPort;settings.questReceivePort=sender.LocalPort;
            settings.heartbeatIntervalSeconds=.1f;settings.connectionTimeoutSeconds=1f;var host=go.AddComponent<DistributedPcHost>();go.SetActive(true);
            yield return null;
            var bytes=new byte[NetworkProtocolV1.MaximumDatagramBytes];var hello=NetworkProtocolV1.WriteStartupHello(bytes,1,ResearchServices.Clock.AbsoluteSeconds,Guid.Empty,321,1,1);
            Assert.That(sender.Send(bytes,hello,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);
            var session=Guid.Empty;var snapshotSession=Guid.Empty;var snapshotSeen=false;var decodedSnapshot=new KeyboardStateSnapshotData();var deadline=Time.realtimeSinceStartup+2f;
            while(Time.realtimeSinceStartup<deadline&&!snapshotSeen)
            {
                while(sender.TryDequeue(out var datagram))
                {
                    try
                    {
                        if(NetworkProtocolV1.TryReadStartupAck(datagram.Data,datagram.Length,out var ack))session=ack.Header.SessionId;
                        if(NetworkProtocolV1.TryReadKeyboardStateSnapshot(datagram.Data,datagram.Length,decodedSnapshot)){snapshotSeen=true;snapshotSession=decodedSnapshot.Header.SessionId;}
                    }
                    finally{sender.Recycle(datagram);}
                }
                yield return null;
            }
            Assert.That(session,Is.Not.EqualTo(Guid.Empty));Assert.That(snapshotSeen,Is.True);Assert.That(snapshotSession,Is.EqualTo(session));Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.Connected));
            host.StopNetwork();sender.Dispose();UnityEngine.Object.Destroy(go);yield return null;
        }
        [UnityTest] public IEnumerator NetworkMidiReachesKeyboardAndDisconnectReleasesAllKeys()
        {
            var go=new GameObject("network-midi",typeof(VirtualPianoKeyboard),typeof(NetworkMidiInput));var keyboard=go.GetComponent<VirtualPianoKeyboard>();var input=go.GetComponent<NetworkMidiInput>();input.MessageReceived+=x=>keyboard.ApplyMidi(in x);var packet=new MidiPacket{Header=new PacketHeader(PacketType.Midi,1,1,Guid.Empty,18),EventIndex=1,EventType=MidiEventType.NoteOn,Channel=1,Note=60,Velocity=100,Control=255,Value=0};input.Enqueue(in packet);yield return null;Assert.That(keyboard.State.IsPressed(60),Is.True);input.Disconnect();Assert.That(keyboard.State.IsPressed(60),Is.False);UnityEngine.Object.Destroy(go);yield return null;
        }
        [UnityTest] public IEnumerator RecorderCreatesAndClosesAllSessionFiles()
        {
            var go=new GameObject("recorder",typeof(DistributedSessionRecorder));var recorder=go.GetComponent<DistributedSessionRecorder>();Assert.That(recorder.Begin(Guid.NewGuid(),"127.0.0.1","test"),Is.True);recorder.SetKeyboardSnapshotDiagnostics(1,2,1,1,0,3,4,5);recorder.End("test");Assert.That(recorder.State,Is.EqualTo(DistributedSessionState.Completed));var expected=new[]{"session_metadata.json","quest_hand_joints.csv","quest_head_pose.csv","pc_midi_events.csv","quest_keyboard_state.csv","clock_sync.csv","network_diagnostics.csv","session_summary.json"};foreach(var file in expected)Assert.That(File.Exists(Path.Combine(recorder.SessionPath,file)),Is.True,file);var summary=File.ReadAllText(Path.Combine(recorder.SessionPath,"session_summary.json"));Assert.That(summary,Does.Contain("\"keyboard_snapshot_received\": 2"));Assert.That(summary,Does.Contain("\"keyboard_snapshot_cc64_repairs\": 4"));var validation=DistributedSessionLogValidator.ValidateDirectory(recorder.SessionPath);Assert.That(validation.Status,Is.EqualTo(SessionLogValidationStatus.Valid),string.Join("; ",validation.Errors));UnityEngine.Object.Destroy(go);yield return null;
        }

        [UnityTest] public IEnumerator HostTimeoutAbortsRecordingResetsAndRebinds()
        {
            var sender=new UdpTransport();sender.Start(0);var pcPort=FindFreePort();var clockPort=FindFreePort();
            var go=new GameObject("lifecycle-host");go.SetActive(false);var settings=go.AddComponent<DistributedSettings>();settings.pcReceivePort=pcPort;settings.clockSyncPort=clockPort;settings.questReceivePort=sender.LocalPort;settings.connectionTimeoutSeconds=.25f;settings.heartbeatIntervalSeconds=.1f;settings.initialClockSyncSamples=1;var host=go.AddComponent<DistributedPcHost>();go.SetActive(true);
            yield return null;Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.Listening));
            var bytes=new byte[NetworkProtocolV1.MaximumDatagramBytes];var hello=NetworkProtocolV1.WriteStartupHello(bytes,1,ResearchServices.Clock.AbsoluteSeconds,Guid.Empty,123,1,1);Assert.That(sender.Send(bytes,hello,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);
            Guid connectionId=Guid.Empty;var deadline=ResearchServices.Clock.AbsoluteSeconds+1d;while(ResearchServices.Clock.AbsoluteSeconds<deadline&&connectionId==Guid.Empty){while(sender.TryDequeue(out var datagram)){try{if(NetworkProtocolV1.TryReadStartupAck(datagram.Data,datagram.Length,out var ack))connectionId=ack.Header.SessionId;}finally{sender.Recycle(datagram);}}yield return null;}
            Assert.That(connectionId,Is.Not.EqualTo(Guid.Empty));Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.Connected));
            var heartbeat=NetworkProtocolV1.WriteHeartbeat(bytes,2,ResearchServices.Clock.AbsoluteSeconds,connectionId,1,(byte)HeartbeatState.Idle);Assert.That(sender.Send(bytes,heartbeat,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);yield return null;var last=host.LastReceivedTimestamp;Assert.That(last,Is.GreaterThan(0d));
            Assert.That(sender.Send(bytes,heartbeat,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);yield return null;Assert.That(host.LastReceivedTimestamp,Is.EqualTo(last));
            var old=NetworkProtocolV1.WriteHeartbeat(bytes,3,ResearchServices.Clock.AbsoluteSeconds,Guid.NewGuid(),2,(byte)HeartbeatState.Idle);Assert.That(sender.Send(bytes,old,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);yield return null;Assert.That(host.LastReceivedTimestamp,Is.EqualTo(last));
            var invalid=NetworkProtocolV1.WriteHeartbeat(bytes,4,ResearchServices.Clock.AbsoluteSeconds,connectionId,3,(byte)HeartbeatState.Idle);bytes[NetworkProtocolV1.HeaderSize+4]=3;Assert.That(sender.Send(bytes,invalid,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);yield return null;Assert.That(host.LastReceivedTimestamp,Is.EqualTo(last));
            deadline=ResearchServices.Clock.AbsoluteSeconds+1d;while(ResearchServices.Clock.AbsoluteSeconds<deadline&&host.NetworkState!=DistributedNetworkState.TimedOut)yield return null;Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.TimedOut));Assert.That(host.QuestConnected,Is.False);Assert.That(host.SessionState,Is.EqualTo(DistributedSessionState.Idle));
            deadline=ResearchServices.Clock.AbsoluteSeconds+1d;while(ResearchServices.Clock.AbsoluteSeconds<deadline&&host.NetworkState!=DistributedNetworkState.Listening)yield return null;Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.Listening));

            hello=NetworkProtocolV1.WriteStartupHello(bytes,1,ResearchServices.Clock.AbsoluteSeconds,Guid.Empty,123,1,1);Assert.That(sender.Send(bytes,hello,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);var reconnectedId=Guid.Empty;deadline=ResearchServices.Clock.AbsoluteSeconds+1d;while(ResearchServices.Clock.AbsoluteSeconds<deadline&&reconnectedId==Guid.Empty){while(sender.TryDequeue(out var datagram)){try{if(NetworkProtocolV1.TryReadStartupAck(datagram.Data,datagram.Length,out var ack)&&ack.Header.SessionId!=connectionId)reconnectedId=ack.Header.SessionId;}finally{sender.Recycle(datagram);}}yield return null;}Assert.That(reconnectedId,Is.Not.EqualTo(Guid.Empty));Assert.That(reconnectedId,Is.Not.EqualTo(connectionId));Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.Connected));
            Assert.That(host.StartSession(),Is.True);SessionControlPacket control=default;deadline=ResearchServices.Clock.AbsoluteSeconds+1d;while(ResearchServices.Clock.AbsoluteSeconds<deadline&&control.CommandId==0){while(sender.TryDequeue(out var datagram)){try{if(NetworkProtocolV1.TryReadSessionControl(datagram.Data,datagram.Length,out var received))control=received;}finally{sender.Recycle(datagram);}}yield return null;}Assert.That(control.CommandId,Is.GreaterThan(0u));var ackBytes=new byte[NetworkProtocolV1.MaximumDatagramBytes];var ackLength=NetworkProtocolV1.WriteSessionAck(ackBytes,2,ResearchServices.Clock.AbsoluteSeconds,control.Header.SessionId,control.Command,SessionAckStatus.Accepted,control.CommandId);Assert.That(sender.Send(ackBytes,ackLength,new IPEndPoint(IPAddress.Loopback,pcPort)),Is.True);deadline=ResearchServices.Clock.AbsoluteSeconds+1d;while(ResearchServices.Clock.AbsoluteSeconds<deadline&&host.SessionState!=DistributedSessionState.Recording)yield return null;Assert.That(host.SessionState,Is.EqualTo(DistributedSessionState.Recording));
            deadline=ResearchServices.Clock.AbsoluteSeconds+1d;while(ResearchServices.Clock.AbsoluteSeconds<deadline&&host.NetworkState!=DistributedNetworkState.TimedOut)yield return null;Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.TimedOut));Assert.That(host.SessionState,Is.EqualTo(DistributedSessionState.Aborted));Assert.That(host.Recorder.WritersClosed,Is.True);var summary=File.ReadAllText(Path.Combine(host.SavePath,"session_summary.json"));Assert.That(summary,Does.Contain("\"reason\": \"network_timeout\""));Assert.That(summary,Does.Contain("\"last_normal_packet_timestamp_sec\""));Assert.That(summary,Does.Contain("\"recorded_rows\""));
            host.StopNetwork();Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.Stopped));Assert.That(host.StartNetwork(),Is.True);yield return null;Assert.That(host.NetworkState,Is.EqualTo(DistributedNetworkState.Listening));host.StopNetwork();sender.Dispose();UnityEngine.Object.Destroy(go);yield return null;
        }

        static int FindFreePort(){using(var transport=new UdpTransport()){transport.Start(0);var port=transport.LocalPort;transport.Stop();return port;}}
    }
}
