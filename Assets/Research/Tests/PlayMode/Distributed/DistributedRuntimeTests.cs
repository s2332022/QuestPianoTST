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
        [Serializable] sealed class MetadataDocument
        {
            public int protocol_version;
            public string session_id;
            public string keyboard_display_mode;
            public CalibrationDocument calibration_snapshot;
        }
        [Serializable] sealed class CalibrationDocument
        {
            public Vector3 keyboard_root_position;
            public Quaternion keyboard_root_rotation;
            public float scaleX;
            public float scaleZ;
        }
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
            var go=new GameObject("recorder",typeof(DistributedSessionRecorder));var recorder=go.GetComponent<DistributedSessionRecorder>();Assert.That(recorder.Begin(Guid.NewGuid(),"127.0.0.1","test"),Is.True);recorder.SetKeyboardSnapshotDiagnostics(1,2,1,1,0,3,4,5);recorder.End("test");Assert.That(recorder.State,Is.EqualTo(DistributedSessionState.Completed));var expected=new[]{"session_metadata.json","quest_hand_joints.csv","quest_head_pose.csv","pc_midi_events.csv","quest_keyboard_state.csv","clock_sync.csv","network_diagnostics.csv","session_summary.json"};foreach(var file in expected)Assert.That(File.Exists(Path.Combine(recorder.SessionPath,file)),Is.True,file);var metadata=File.ReadAllText(Path.Combine(recorder.SessionPath,"session_metadata.json"));Assert.That(metadata,Does.Contain("\"calibration_snapshot\": null"));Assert.That(metadata,Does.Contain("\"keyboard_display_mode\": null"));Assert.That(metadata,Does.Contain("\"unity_version\""));Assert.That(metadata,Does.Contain("\"coordinate_system\""));var summary=File.ReadAllText(Path.Combine(recorder.SessionPath,"session_summary.json"));Assert.That(summary,Does.Contain("\"keyboard_snapshot_received\": 2"));Assert.That(summary,Does.Contain("\"keyboard_snapshot_cc64_repairs\": 4"));var validation=DistributedSessionLogValidator.ValidateDirectory(recorder.SessionPath);Assert.That(validation.Status,Is.EqualTo(SessionLogValidationStatus.Valid),string.Join("; ",validation.Errors));UnityEngine.Object.Destroy(go);yield return null;
        }

        [UnityTest] public IEnumerator QuestMetadata_RecordsLiveKeyboardTransformAndDisplayMode()
        {
            var id=Guid.NewGuid();var directory=Path.Combine(Application.persistentDataPath,"PianoResearch","DistributedSessions",id.ToString("N")+"_Quest");
            var keyboardObject=new GameObject("metadata-keyboard",typeof(VirtualPianoKeyboard));
            try
            {
                yield return null;
                var keyboard=keyboardObject.GetComponent<VirtualPianoKeyboard>();
                Assert.That(keyboard.KeyboardRoot,Is.Not.Null);
                keyboard.KeyboardRoot.SetPositionAndRotation(new Vector3(4,5,6),Quaternion.Euler(0,30,0));
                var calibration=new PianoCalibrationData{valid=true,formatVersion=1,scaleX=1.1f,scaleZ=0.9f};
                DistributedSessionRecorder.WriteQuestMetadata(id,calibration,keyboard);
                var document=JsonUtility.FromJson<MetadataDocument>(File.ReadAllText(Path.Combine(directory,"session_metadata.json")));
                Assert.That(document.protocol_version,Is.EqualTo(1));
                Assert.That(document.session_id,Is.EqualTo(id.ToString()));
                Assert.That(document.keyboard_display_mode,Is.EqualTo("Full88Keys"));
                Assert.That(document.calibration_snapshot.keyboard_root_position,Is.EqualTo(new Vector3(4,5,6)));
                Assert.That(Quaternion.Angle(document.calibration_snapshot.keyboard_root_rotation,keyboard.KeyboardRoot.rotation),Is.LessThan(0.01f));
                Assert.That(document.calibration_snapshot.scaleX,Is.EqualTo(1.1f).Within(0.0001f));
                Assert.That(document.calibration_snapshot.scaleZ,Is.EqualTo(0.9f).Within(0.0001f));
            }
            finally{UnityEngine.Object.Destroy(keyboardObject);if(Directory.Exists(directory))Directory.Delete(directory,true);}
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

        [UnityTest]
        public IEnumerator BleSessionAndUdpBothDriveSharedViewsAndDisconnectOnlyOwnedNotes()
        {
            var host = new GameObject("shared MIDI keyboard", typeof(VirtualPianoKeyboard), typeof(NetworkMidiInput));
            var bleObject = new GameObject("separate BLE source", typeof(BleMidiInput));
            var keyboard = host.GetComponent<VirtualPianoKeyboard>(); var udp = host.GetComponent<NetworkMidiInput>();
            var ble = bleObject.GetComponent<BleMidiInput>(); keyboard.BindBleMidi(ble);
            udp.MessageReceived += m => keyboard.ApplyMidi(in m);
            var session = (BleMidiSession)typeof(BleMidiInput).GetField("m_Session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(ble);
            session.Begin(1, "BLE actual session");
            void BlePacket(byte status, byte note, byte velocity)
            {
                Assert.That(new BleMidiPacketParser().Parse(new byte[] { 0x80, 0x81, status, note, velocity }, 1, 123, s => session.Enqueue(s)), Is.True);
                session.Flush();
            }
            void UdpEvent(MidiEventType type, byte note, byte velocity)
            {
                var packet = new MidiPacket { Header = new PacketHeader(PacketType.Midi, 1, 1, Guid.Empty, 18), EventIndex = 1,
                    EventType = type, Channel = 1, Note = note, Velocity = velocity, Control = 255 };
                udp.Enqueue(in packet); udp.FlushPending();
            }
            try
            {
                yield return null;
                Assert.That(keyboard.TryGetKey(60, out var key), Is.True);
                UdpEvent(MidiEventType.NoteOn, 60, 100); Assert.That(key.Pressed, Is.True);
                UdpEvent(MidiEventType.NoteOff, 60, 0); Assert.That(key.Pressed, Is.False);
                BlePacket(0x90, 60, 100); Assert.That(key.Pressed, Is.True);
                BlePacket(0x80, 60, 7); Assert.That(key.Pressed, Is.False);
                BlePacket(0x90, 60, 100); BlePacket(0x90, 60, 0); Assert.That(key.Pressed, Is.False);
                UdpEvent(MidiEventType.NoteOn, 60, 100); BlePacket(0x90, 60, 100); BlePacket(0x90, 61, 90);
                Assert.That(key.Renderer.transform.localPosition, Is.EqualTo(key.PressedLocalPosition));
                ble.Disconnect(); Assert.That(key.Pressed, Is.True); Assert.That(udp.IsConnected, Is.True);
                Assert.That(keyboard.TryGetKey(61, out var bleOnly), Is.True); Assert.That(bleOnly.Pressed, Is.False);
                UdpEvent(MidiEventType.NoteOff, 60, 0); Assert.That(key.Pressed, Is.False);
                session.Begin(1, "BLE reconnect"); BlePacket(0x90, 60, 100);
                UdpEvent(MidiEventType.NoteOn, 60, 100); udp.Disconnect(); Assert.That(key.Pressed, Is.True);
                Assert.That(keyboard.State.IsPressed(60), Is.False); // existing UDP state remains source-specific
                ble.enabled = false; Assert.That(key.Pressed, Is.False);
            }
            finally { UnityEngine.Object.Destroy(host); UnityEngine.Object.Destroy(bleObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator QuestCompositionBindsSeparateBleObjectAndCleansUpOnDisable()
        {
            var host = new GameObject("composition MIDI keyboard", typeof(VirtualPianoKeyboard), typeof(DistributedQuestComposition));
            var source = new GameObject("composition BLE source", typeof(BleMidiInput));
            try
            {
                yield return null;
                var keyboard = host.GetComponent<VirtualPianoKeyboard>();
                var ble = source.GetComponent<BleMidiInput>();
                var session = (BleMidiSession)typeof(BleMidiInput).GetField("m_Session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(ble);
                session.Begin(1, "BLE composition"); session.Enqueue(new BleMidiSession.Sample(1, 0, 123, 0x90, 60, 100)); session.Flush();
                Assert.That(keyboard.TryGetKey(60, out var key), Is.True); Assert.That(key.Pressed, Is.True);
                host.GetComponent<DistributedQuestComposition>().enabled = false; Assert.That(key.Pressed, Is.False);
                host.GetComponent<DistributedQuestComposition>().enabled = true; yield return null;
                session.Enqueue(new BleMidiSession.Sample(1, 0, 124, 0x80, 60, 0)); session.Enqueue(new BleMidiSession.Sample(1, 0, 125, 0x90, 60, 90)); session.Flush();
                Assert.That(key.Pressed, Is.True); ble.Disconnect(); Assert.That(key.Pressed, Is.False);
            }
            finally { UnityEngine.Object.Destroy(host); UnityEngine.Object.Destroy(source); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator UdpHandshakeAndMidiDatagramsDriveActualQuestClientKeyViews()
        {
            using var sender = new UdpTransport(); sender.Start(0);
            var host = new GameObject("actual Quest UDP keyboard"); host.SetActive(false);
            var settings = host.AddComponent<DistributedSettings>(); settings.pcIpAddress = "127.0.0.1";
            settings.pcReceivePort = sender.LocalPort; settings.questReceivePort = FindFreePort(); settings.clockSyncPort = FindFreePort();
            settings.connectionTimeoutSeconds = 5f; settings.heartbeatIntervalSeconds = .1f;
            var keyboard = host.AddComponent<VirtualPianoKeyboard>(); host.AddComponent<NetworkMidiInput>();
            var client = host.AddComponent<DistributedQuestClient>(); host.SetActive(true);
            var bytes = new byte[NetworkProtocolV1.MaximumDatagramBytes]; uint instance = 0; var id = Guid.NewGuid();
            try
            {
                var deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline && instance == 0)
                {
                    while (sender.TryDequeue(out var datagram))
                    {
                        try { if (NetworkProtocolV1.TryReadStartupHello(datagram.Data, datagram.Length, out var hello)) instance = hello.InstanceId; }
                        finally { sender.Recycle(datagram); }
                    }
                    yield return null;
                }
                Assert.That(instance, Is.Not.Zero);
                var endpoint = new IPEndPoint(IPAddress.Loopback, settings.questReceivePort);
                var n = NetworkProtocolV1.WriteStartupAck(bytes, 1, ResearchServices.Clock.AbsoluteSeconds, id, instance);
                Assert.That(sender.Send(bytes, n, endpoint), Is.True);
                deadline = Time.realtimeSinceStartup + 2f;
                while (!client.PcConnected && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(client.PcConnected, Is.True);
                Assert.That(keyboard.TryGetKey(60, out var key), Is.True);
                foreach (var type in new[] { MidiEventType.NoteOn, MidiEventType.NoteOff })
                {
                    var on = type == MidiEventType.NoteOn;
                    var message = new MidiMessage(12, on ? 1 : 2, "PC test", type, 1, 60, on ? 100 : 0, -1, -1);
                    n = NetworkProtocolV1.WriteMidi(bytes, on ? 2u : 3u, 12, id, in message, 0);
                    Assert.That(sender.Send(bytes, n, endpoint), Is.True);
                    deadline = Time.realtimeSinceStartup + 2f;
                    while (key.Pressed != on && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(key.Pressed, Is.EqualTo(on));
                    Assert.That(key.Renderer.transform.localPosition, Is.EqualTo(on ? key.PressedLocalPosition : key.RestLocalPosition));
                }
            }
            finally { client.StopNetwork(); UnityEngine.Object.Destroy(host); }
            yield return null;
        }
    }
}
