using System;
using System.Collections.Generic;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class DistributedProtocolTests
    {
        [Test] public void AndroidMidiInput_ImplementsSharedMidiContract(){Assert.That(typeof(IMidiInput).IsAssignableFrom(typeof(AndroidMidiInput)),Is.True);}
        [Test] public void PosePacket_RoundTrips()
        {
            var data=new byte[1200];var joints=new List<PoseJointSample>{new PoseJointSample{Hand=0,JointId=7,PoseValid=true,TrackingState=3,Position=new Vector3(1,2,3),Rotation=new Quaternion(.1f,.2f,.3f,.9f)}};var id=Guid.NewGuid();var n=NetworkProtocolV1.WritePose(data,12,1.25,id,42,99,1,7,true,false,true,0,1,new Pose(Vector3.one,Quaternion.identity),Pose.identity,new Pose(Vector3.forward,Quaternion.identity),joints,0,1);
            Assert.That(n,Is.LessThanOrEqualTo(NetworkProtocolV1.MaximumDatagramBytes));Assert.That(NetworkProtocolV1.TryReadPose(data,n,out var p),Is.True);Assert.That(p.Header.Sequence,Is.EqualTo(12));Assert.That(p.Header.SessionId,Is.EqualTo(id));Assert.That(p.UnityFrame,Is.EqualTo(42));Assert.That(p.Joints.Count,Is.EqualTo(1));Assert.That(p.Joints[0].Position,Is.EqualTo(new Vector3(1,2,3)));
        }
        [Test] public void MidiPacket_RoundTrips_AndNormalizesVelocityZero()
        {
            var data=new byte[128];var m=new MidiMessage(2.5,8,"device",MidiEventType.NoteOn,2,60,0,-1,-1);var n=NetworkProtocolV1.WriteMidi(data,4,2.5,Guid.Empty,in m,123);Assert.That(NetworkProtocolV1.TryReadMidi(data,n,out var p),Is.True);Assert.That(p.EventType,Is.EqualTo(MidiEventType.NoteOff));Assert.That(p.Channel,Is.EqualTo(2));Assert.That(p.Note,Is.EqualTo(60));
        }
        [Test] public void MidiPacket_PreservesCc64(){var d=new byte[128];var m=new MidiMessage(1,1,"d",MidiEventType.ControlChange,1,-1,-1,64,127);var n=NetworkProtocolV1.WriteMidi(d,1,1,Guid.Empty,in m,0);Assert.That(NetworkProtocolV1.TryReadMidi(d,n,out var p),Is.True);Assert.That(p.Control,Is.EqualTo(64));Assert.That(p.Value,Is.EqualTo(127));}
        [Test] public void KeyboardSnapshot_SerializesAllChannelsNotesAndSustainWithoutChangingV1MidiLayout()
        {
            var state=new KeyboardStateTracker();
            for(var channel=1;channel<=16;++channel)
            {
                for(var note=0;note<128;++note)if(((channel*17+note*3)%7)<3)state.Apply(new MidiMessage(1,1,"d",MidiEventType.NoteOn,channel,note,90,-1,-1));
                state.Apply(new MidiMessage(1,1,"d",MidiEventType.ControlChange,channel,-1,-1,64,channel%2==0?127:0));
            }
            var session=Guid.NewGuid();var data=new byte[NetworkProtocolV1.MaximumDatagramBytes];
            var length=NetworkProtocolV1.WriteKeyboardStateSnapshot(data,42,123.5,session,0x10203040,state);
            var decoded=new KeyboardStateSnapshotData();
            Assert.That(length,Is.EqualTo(314));Assert.That(NetworkProtocolV1.KeyboardStateSnapshotPayloadBytes,Is.EqualTo(276));
            Assert.That(NetworkProtocolV1.TryReadKeyboardStateSnapshot(data,length,decoded),Is.True);
            Assert.That(decoded.Header.SessionId,Is.EqualTo(session));Assert.That(decoded.Header.SenderTimestamp,Is.EqualTo(123.5));
            Assert.That(decoded.Generation,Is.EqualTo(0x10203040));Assert.That(NetworkProtocolV1.Version,Is.EqualTo(1));
            for(var channel=1;channel<=16;++channel)
            {
                Assert.That(decoded.Sustain[channel-1],Is.EqualTo(channel%2==0?1:0));
                for(var note=0;note<128;++note)Assert.That((decoded.NoteBits[(channel-1)*16+(note>>3)]&(1<<(note&7)))!=0,Is.EqualTo(((channel*17+note*3)%7)<3));
            }
            Assert.That(NetworkProtocolV1.HeaderSize,Is.EqualTo(38));
        }
        [Test] public void KeyboardSnapshot_RejectsMalformedShortLargeAndInvalidValues()
        {
            var data=new byte[NetworkProtocolV1.MaximumDatagramBytes];var scratch=new KeyboardStateSnapshotData();
            var length=NetworkProtocolV1.WriteKeyboardStateSnapshot(data,1,1,Guid.NewGuid(),1,new KeyboardStateTracker());
            Assert.That(NetworkProtocolV1.TryReadKeyboardStateSnapshot(data,length-1,scratch),Is.False);
            var oversized=new byte[NetworkProtocolV1.MaximumDatagramBytes+1];Buffer.BlockCopy(data,0,oversized,0,length);
            Assert.That(NetworkProtocolV1.TryReadKeyboardStateSnapshot(oversized,oversized.Length,scratch),Is.False);
            data[NetworkProtocolV1.HeaderSize+4+KeyboardStateSnapshotData.NoteBitsLength+3]=2;
            Assert.That(NetworkProtocolV1.TryReadKeyboardStateSnapshot(data,length,scratch),Is.False);
            data[NetworkProtocolV1.HeaderSize+4+KeyboardStateSnapshotData.NoteBitsLength+3]=0;
            WriteU64BigEndian(data,12,0x7ff8000000000000UL);
            Assert.That(NetworkProtocolV1.TryReadKeyboardStateSnapshot(data,length,scratch),Is.False);
        }
        [Test] public void KeyboardSnapshot_RejectsUnknownPacketTypeWithoutBreakingDatagramProcessing()
        {
            var data=new byte[128];var message=new MidiMessage(1,1,"d",MidiEventType.NoteOn,1,60,90,-1,-1);
            var length=NetworkProtocolV1.WriteMidi(data,1,1,Guid.Empty,in message,0);data[6]=0x7f;data[7]=0xff;
            Assert.That(NetworkProtocolV1.TryReadHeader(data,length,out _),Is.False);
            Assert.That(NetworkProtocolV1.TryReadMidi(data,length,out _),Is.False);
        }
        [Test] public void KeyboardSnapshotDiagnostics_RoundTripsCounters()
        {
            var data=new byte[128];var session=Guid.NewGuid();var length=NetworkProtocolV1.WriteKeyboardSnapshotDiagnostics(data,5,7.5,session,11,9,2,1,4,3,0xfffffffe);
            Assert.That(NetworkProtocolV1.TryReadKeyboardSnapshotDiagnostics(data,length,out var decoded),Is.True);
            Assert.That(decoded.Header.SessionId,Is.EqualTo(session));Assert.That(decoded.Received,Is.EqualTo(11));
            Assert.That(decoded.Applied,Is.EqualTo(9));Assert.That(decoded.StaleDropped,Is.EqualTo(2));
            Assert.That(decoded.SessionMismatchDropped,Is.EqualTo(1));Assert.That(decoded.NoteRepairs,Is.EqualTo(4));
            Assert.That(decoded.Cc64Repairs,Is.EqualTo(3));Assert.That(decoded.LastGeneration,Is.EqualTo(0xfffffffe));
        }
        [Test] public void KeyboardSnapshot_RejectsSessionMismatchAndDisconnectedPeer()
        {
            var current=Guid.NewGuid();var other=Guid.NewGuid();var decoded=CreateSnapshot(new KeyboardStateTracker(),other,1);
            var receiver=new KeyboardStateSnapshotReceiver();var state=new KeyboardStateTracker();
            Assert.That(receiver.TryApply(decoded,current,true,state,out var rejection,out _,out _),Is.False);
            Assert.That(rejection,Is.EqualTo(KeyboardSnapshotApplyRejection.SessionMismatch));
            decoded=CreateSnapshot(new KeyboardStateTracker(),current,1);
            Assert.That(receiver.TryApply(decoded,current,false,state,out rejection,out _,out _),Is.False);
            Assert.That(rejection,Is.EqualTo(KeyboardSnapshotApplyRejection.Disconnected));
            Assert.That(receiver.HasGeneration,Is.False);
        }
        [Test] public void KeyboardSnapshot_RejectsDuplicateAndOlderGeneration()
        {
            var session=Guid.NewGuid();var receiver=new KeyboardStateSnapshotReceiver();var state=new KeyboardStateTracker();
            var first=CreateSnapshot(new KeyboardStateTracker(),session,10);
            Assert.That(receiver.TryApply(first,session,true,state,out _,out _,out _),Is.True);
            Assert.That(receiver.TryApply(first,session,true,state,out var rejection,out _,out _),Is.False);
            Assert.That(rejection,Is.EqualTo(KeyboardSnapshotApplyRejection.StaleGeneration));
            var older=CreateSnapshot(new KeyboardStateTracker(),session,9);
            Assert.That(receiver.TryApply(older,session,true,state,out rejection,out _,out _),Is.False);
            Assert.That(rejection,Is.EqualTo(KeyboardSnapshotApplyRejection.StaleGeneration));
            Assert.That(receiver.LastGeneration,Is.EqualTo(10));
        }
        [Test] public void KeyboardSnapshot_RepairsMissingNoteOnAndNoteOff()
        {
            var session=Guid.NewGuid();var receiver=new KeyboardStateSnapshotReceiver();var actual=new KeyboardStateTracker();
            var pressed=new KeyboardStateTracker();pressed.Apply(Note(MidiEventType.NoteOn,2,67));
            Assert.That(receiver.TryApply(CreateSnapshot(pressed,session,1),session,true,actual,out _,out var onRepairs,out _),Is.True);
            Assert.That(actual.IsChannelNotePressed(2,67),Is.True);Assert.That(actual.IsPressed(67),Is.True);Assert.That(onRepairs,Is.EqualTo(1));
            Assert.That(receiver.TryApply(CreateSnapshot(new KeyboardStateTracker(),session,2),session,true,actual,out _,out var offRepairs,out _),Is.True);
            Assert.That(actual.IsChannelNotePressed(2,67),Is.False);Assert.That(actual.IsPressed(67),Is.False);Assert.That(offRepairs,Is.EqualTo(1));
        }
        [Test] public void KeyboardSnapshot_RepairsChannelSpecificCc64OnAndOff()
        {
            var session=Guid.NewGuid();var receiver=new KeyboardStateSnapshotReceiver();var actual=new KeyboardStateTracker();
            var sustainOn=new KeyboardStateTracker();sustainOn.Apply(new MidiMessage(1,1,"d",MidiEventType.ControlChange,9,-1,-1,64,127));
            Assert.That(receiver.TryApply(CreateSnapshot(sustainOn,session,1),session,true,actual,out _,out _,out var onRepairs),Is.True);
            Assert.That(actual.IsSustainActive(9),Is.True);Assert.That(actual.SustainActive,Is.True);Assert.That(onRepairs,Is.EqualTo(1));
            Assert.That(receiver.TryApply(CreateSnapshot(new KeyboardStateTracker(),session,2),session,true,actual,out _,out _,out var offRepairs),Is.True);
            Assert.That(actual.IsSustainActive(9),Is.False);Assert.That(actual.SustainActive,Is.False);Assert.That(offRepairs,Is.EqualTo(1));
        }
        [Test] public void KeyboardSnapshot_NoteRepairEventsUseSnapshotSustainState()
        {
            var session=Guid.NewGuid();var source=new KeyboardStateTracker();source.Apply(Note(MidiEventType.NoteOn,3,62));
            source.Apply(new MidiMessage(1,2,"d",MidiEventType.ControlChange,3,-1,-1,64,127));
            var changes=new List<KeyboardStateChange>();var actual=new KeyboardStateTracker();actual.StateChanged+=changes.Add;
            var receiver=new KeyboardStateSnapshotReceiver();
            Assert.That(receiver.TryApply(CreateSnapshot(source,session,1),session,true,actual,out _,out _,out _),Is.True);
            Assert.That(changes.Find(change=>change.NoteNumber==62).SustainActive,Is.True);
        }
        [Test] public void KeyboardSnapshot_RepairsChordsWithoutMixingChannels()
        {
            var session=Guid.NewGuid();var receiver=new KeyboardStateSnapshotReceiver();var actual=new KeyboardStateTracker();
            actual.Apply(Note(MidiEventType.NoteOn,1,60));actual.Apply(Note(MidiEventType.NoteOn,2,60));
            var chord=new KeyboardStateTracker();chord.Apply(Note(MidiEventType.NoteOn,2,60));chord.Apply(Note(MidiEventType.NoteOn,2,64));chord.Apply(Note(MidiEventType.NoteOn,3,67));
            Assert.That(receiver.TryApply(CreateSnapshot(chord,session,1),session,true,actual,out _,out var repairs,out _),Is.True);
            Assert.That(repairs,Is.EqualTo(3));Assert.That(actual.IsChannelNotePressed(1,60),Is.False);
            Assert.That(actual.IsChannelNotePressed(2,60),Is.True);Assert.That(actual.IsChannelNotePressed(2,64),Is.True);
            Assert.That(actual.IsChannelNotePressed(3,67),Is.True);Assert.That(actual.IsChannelNotePressed(1,64),Is.False);
        }
        [Test] public void KeyboardSnapshot_IsIdempotentAndDoesNotDispatchMidiEvents()
        {
            var session=Guid.NewGuid();var source=new KeyboardStateTracker();source.Apply(Note(MidiEventType.NoteOn,1,60));
            var snapshot=CreateSnapshot(source,session,1);var tracker=new KeyboardStateTracker();var receiver=new KeyboardStateSnapshotReceiver();
            var go=new GameObject("Snapshot MIDI event guard");var input=go.AddComponent<NetworkMidiInput>();var midiEvents=0;
            input.MessageReceived+=_=>++midiEvents;var stateEvents=0;tracker.StateChanged+=_=>++stateEvents;
            Assert.That(receiver.TryApply(snapshot,session,true,tracker,out _,out _,out _),Is.True);
            Assert.That(receiver.TryApply(snapshot,session,true,tracker,out _,out _,out _),Is.False);
            Assert.That(midiEvents,Is.Zero);Assert.That(stateEvents,Is.EqualTo(1));Assert.That(tracker.IsPressed(60),Is.True);
            UnityEngine.Object.DestroyImmediate(go);
        }
        [Test] public void KeyboardSnapshot_ResetAllowsNewSessionAndRejectsOldSession()
        {
            var oldSession=Guid.NewGuid();var newSession=Guid.NewGuid();var receiver=new KeyboardStateSnapshotReceiver();var keyboard=new KeyboardStateTracker();
            Assert.That(receiver.TryApply(CreateSnapshot(new KeyboardStateTracker(),oldSession,31),oldSession,true,keyboard,out _,out _,out _),Is.True);
            receiver.Reset();Assert.That(receiver.HasGeneration,Is.False);Assert.That(receiver.LastGeneration,Is.Zero);
            Assert.That(receiver.TryApply(CreateSnapshot(new KeyboardStateTracker(),oldSession,32),newSession,true,keyboard,out var rejection,out _,out _),Is.False);
            Assert.That(rejection,Is.EqualTo(KeyboardSnapshotApplyRejection.SessionMismatch));
            Assert.That(receiver.TryApply(CreateSnapshot(new KeyboardStateTracker(),newSession,0),newSession,true,keyboard,out _,out _,out _),Is.True);
        }
        [Test] public void KeyboardSnapshot_SynchronizesAllNotesOffState()
        {
            var session=Guid.NewGuid();var actual=new KeyboardStateTracker();actual.Apply(Note(MidiEventType.NoteOn,4,60));actual.Apply(Note(MidiEventType.NoteOn,4,64));
            var source=new KeyboardStateTracker();source.Apply(Note(MidiEventType.NoteOn,4,60));source.Apply(Note(MidiEventType.NoteOn,4,64));
            source.Apply(new MidiMessage(2,2,"d",MidiEventType.ControlChange,4,-1,-1,123,0));
            Assert.That(source.IsPressed(60),Is.False);Assert.That(source.IsPressed(64),Is.False);
            var receiver=new KeyboardStateSnapshotReceiver();Assert.That(receiver.TryApply(CreateSnapshot(source,session,1),session,true,actual,out _,out var repairs,out _),Is.True);
            Assert.That(actual.IsPressed(60),Is.False);Assert.That(actual.IsPressed(64),Is.False);Assert.That(repairs,Is.EqualTo(2));
        }
        [Test] public void SnapshotAndPacketSequencesHandleUintWraparound()
        {
            Assert.That(SequenceTracker.IsNewer(0,uint.MaxValue),Is.True);
            Assert.That(SequenceTracker.IsNewer(uint.MaxValue,0),Is.False);
            var tracker=new SequenceTracker();tracker.Observe(uint.MaxValue);Assert.That(tracker.Observe(0).OutOfOrder,Is.False);
            var session=Guid.NewGuid();var receiver=new KeyboardStateSnapshotReceiver();var keyboard=new KeyboardStateTracker();
            Assert.That(receiver.TryApply(CreateSnapshot(new KeyboardStateTracker(),session,uint.MaxValue),session,true,keyboard,out _,out _,out _),Is.True);
            Assert.That(receiver.TryApply(CreateSnapshot(new KeyboardStateTracker(),session,0),session,true,keyboard,out _,out _,out _),Is.True);
        }
        [Test] public void StartupHelloAndAck_RoundTrip(){var d=new byte[128];var id=Guid.NewGuid();var n=NetworkProtocolV1.WriteStartupHello(d,7,2.5,id,123,456,789);Assert.That(NetworkProtocolV1.TryReadStartupHello(d,n,out var hello),Is.True);Assert.That(hello.Header.Sequence,Is.EqualTo(7));Assert.That(hello.InstanceId,Is.EqualTo(123));Assert.That(hello.ApplicationVersionHash,Is.EqualTo(456));Assert.That(hello.BuildIdHash,Is.EqualTo(789));n=NetworkProtocolV1.WriteStartupAck(d,8,2.6,id,123);Assert.That(NetworkProtocolV1.TryReadStartupAck(d,n,out var ack),Is.True);Assert.That(ack.Header.Sequence,Is.EqualTo(8));Assert.That(ack.InstanceId,Is.EqualTo(123));}
        [Test] public void InvalidMagicVersionAndLengthAreRejected()
        {
            var d=new byte[128];var m=new MidiMessage(1,1,"d",MidiEventType.NoteOn,1,60,1,-1,-1);var n=NetworkProtocolV1.WriteMidi(d,1,1,Guid.Empty,in m,0);var copy=(byte[])d.Clone();copy[0]=0;Assert.That(NetworkProtocolV1.TryReadHeader(copy,n,out _),Is.False);copy=(byte[])d.Clone();copy[5]=2;Assert.That(NetworkProtocolV1.TryReadHeader(copy,n,out _),Is.False);Assert.That(NetworkProtocolV1.TryReadHeader(d,n-1,out _),Is.False);
        }
        [Test] public void MalformedPoseLengthChunksAndNonFiniteValuesAreRejected()
        {
            Assert.DoesNotThrow(() => Assert.That(NetworkProtocolV1.TryReadHeader(new byte[1],NetworkProtocolV1.HeaderSize,out _),Is.False));
            var data=new byte[NetworkProtocolV1.MaximumDatagramBytes];
            var joints=new List<PoseJointSample>();
            var n=NetworkProtocolV1.WritePose(data,1,1d,Guid.Empty,1,1,1,0,true,true,false,0,1,Pose.identity,Pose.identity,Pose.identity,joints,0,0);
            WriteU16BigEndian(data,61,0);
            Assert.That(NetworkProtocolV1.TryReadPose(data,n,out _),Is.False);
            n=NetworkProtocolV1.WritePose(data,1,1d,Guid.Empty,1,1,1,0,true,true,false,0,1,Pose.identity,Pose.identity,Pose.identity,joints,0,0);
            WriteU64BigEndian(data,12,0x7ff8000000000000UL);
            Assert.That(NetworkProtocolV1.TryReadPose(data,n,out _),Is.False);
            n=NetworkProtocolV1.WritePose(data,1,1d,Guid.Empty,1,1,1,0,true,true,false,0,1,Pose.identity,Pose.identity,Pose.identity,joints,0,0);
            WriteU32BigEndian(data,63,0x7fc00000U);
            Assert.That(NetworkProtocolV1.TryReadPose(data,n,out _),Is.False);
        }
        [Test] public void InvalidClockValuesAndNegativeRttAreRejected()
        {
            var data=new byte[128];
            var n=NetworkProtocolV1.WriteClockResponse(data,1,1d,Guid.Empty,2d,3d,4d);
            WriteU64BigEndian(data,NetworkProtocolV1.HeaderSize,0x7ff8000000000000UL);
            Assert.That(NetworkProtocolV1.TryReadClockResponse(data,n,out _),Is.False);
            n=NetworkProtocolV1.WriteHeartbeat(data,1,1d,Guid.Empty,0,0,0d,-1d);
            Assert.That(NetworkProtocolV1.TryReadHeartbeat(data,n,out _),Is.False);
            var synchronizer=new ClockSynchronizer();
            Assert.That(synchronizer.TryAdd(2d,3d,2.5d,2.4d,out _),Is.False);
            Assert.That(synchronizer.TryAdd(2d,3d,3.1d,double.NaN,out _),Is.False);
            Assert.That(synchronizer.SampleCount,Is.EqualTo(0));
        }
        [Test] public void SequenceTracker_DetectsLossDuplicateAndOutOfOrder(){var t=new SequenceTracker();t.Observe(10);var gap=t.Observe(13);var dup=t.Observe(13);var old=t.Observe(12);Assert.That(gap.Missing,Is.EqualTo(2));Assert.That(dup.Duplicate,Is.True);Assert.That(old.OutOfOrder,Is.True);Assert.That(t.Missing,Is.EqualTo(2));}
        [Test] public void ClockMath_ComputesRttOffsetAndSelectsMinimumRtt(){var c=new ClockSynchronizer();var first=c.Add(10,11,11.1,10.5);Assert.That(first.Rtt,Is.EqualTo(.4).Within(1e-9));Assert.That(first.Offset,Is.EqualTo(.8).Within(1e-9));var second=c.Add(20,20.6,20.65,20.2);Assert.That(second.Rtt,Is.EqualTo(.15).Within(1e-9));Assert.That(c.Selected.Index,Is.EqualTo(1));}
        [Test] public void ClockSynchronizer_ResetDropsPreviousConnectionEstimate(){var c=new ClockSynchronizer();c.Add(10,11,11.1,10.5);c.Reset();Assert.That(c.SampleCount,Is.EqualTo(0));Assert.That(c.HasEstimate,Is.False);Assert.That(c.OffsetSeconds,Is.EqualTo(0d));}
        [Test] public void PoseFrameAssembler_ResetDropsIncompletePreviousConnectionFrame(){var a=new PoseFrameAssembler();var first=new PosePacket{CallbackIndex=7,ChunkIndex=0,ChunkCount=2,Header=new PacketHeader(PacketType.Pose,1,1d,Guid.Empty,0)};Assert.That(a.Accept(first,1d,out var ignored),Is.False);a.Reset();var second=new PosePacket{CallbackIndex=1,ChunkIndex=0,ChunkCount=1,Header=new PacketHeader(PacketType.Pose,2,2d,Guid.Empty,0)};Assert.That(a.Accept(second,2d,out var complete),Is.True);Assert.That(complete.Count,Is.EqualTo(1));}
        [Test] public void Heartbeat_DisconnectingStateRoundTrips_AndUnknownStateIsRejected(){var d=new byte[128];var n=NetworkProtocolV1.WriteHeartbeat(d,1,1d,Guid.NewGuid(),0,(byte)HeartbeatState.Disconnecting);Assert.That(NetworkProtocolV1.TryReadHeartbeat(d,n,out var heartbeat),Is.True);Assert.That(heartbeat.State,Is.EqualTo((byte)HeartbeatState.Disconnecting));d[NetworkProtocolV1.HeaderSize+4]=3;Assert.That(NetworkProtocolV1.TryReadHeartbeat(d,n,out _),Is.False);}
        [Test] public void KeyboardReset_ReleasesNotesAndSustain(){var tracker=new KeyboardStateTracker();var changes=new List<KeyboardStateChange>();tracker.StateChanged+=changes.Add;tracker.Apply(new MidiMessage(1,1,"test",MidiEventType.NoteOn,1,60,100,-1,-1));tracker.Apply(new MidiMessage(1,2,"test",MidiEventType.ControlChange,1,-1,0,64,127));tracker.Reset(2);Assert.That(tracker.IsPressed(60),Is.False);Assert.That(tracker.SustainActive,Is.False);Assert.That(changes.Exists(x=>x.NoteNumber==60&&!x.Pressed),Is.True);Assert.That(changes.Exists(x=>x.NoteNumber==-1&&!x.SustainActive),Is.True);}
        [Test] public void SessionStateTransitionsRequireAcknowledgement(){var s=new SessionStateMachine();Assert.That(s.RequestStart(),Is.True);Assert.That(s.State,Is.EqualTo(DistributedSessionState.Starting));Assert.That(s.AcknowledgeStart(true),Is.True);Assert.That(s.RequestStop(),Is.True);Assert.That(s.CompleteStop(),Is.True);Assert.That(s.State,Is.EqualTo(DistributedSessionState.Completed));}

        static MidiMessage Note(MidiEventType type,int channel,int note)=>new MidiMessage(1,1,"test",type,channel,note,type==MidiEventType.NoteOn?100:0,-1,-1);
        static KeyboardStateSnapshotData CreateSnapshot(KeyboardStateTracker state,Guid session,uint generation)
        {
            var buffer=new byte[NetworkProtocolV1.MaximumDatagramBytes];
            var length=NetworkProtocolV1.WriteKeyboardStateSnapshot(buffer,1,2d,session,generation,state);
            var decoded=new KeyboardStateSnapshotData();
            if(!NetworkProtocolV1.TryReadKeyboardStateSnapshot(buffer,length,decoded))throw new InvalidOperationException("Test Snapshot failed to decode.");
            return decoded;
        }

        static void WriteU16BigEndian(byte[] data,int offset,ushort value)
        {
            data[offset]=(byte)(value>>8); data[offset+1]=(byte)value;
        }

        static void WriteU32BigEndian(byte[] data,int offset,uint value)
        {
            data[offset]=(byte)(value>>24); data[offset+1]=(byte)(value>>16);
            data[offset+2]=(byte)(value>>8); data[offset+3]=(byte)value;
        }

        static void WriteU64BigEndian(byte[] data,int offset,ulong value)
        {
            for(var i=0;i<8;++i) data[offset+i]=(byte)(value>>((7-i)*8));
        }
    }
}
