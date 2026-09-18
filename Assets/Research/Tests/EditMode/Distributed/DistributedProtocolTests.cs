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
        [Test] public void SessionStateTransitionsRequireAcknowledgement(){var s=new SessionStateMachine();Assert.That(s.RequestStart(),Is.True);Assert.That(s.State,Is.EqualTo(DistributedSessionState.Starting));Assert.That(s.AcknowledgeStart(true),Is.True);Assert.That(s.RequestStop(),Is.True);Assert.That(s.CompleteStop(),Is.True);Assert.That(s.State,Is.EqualTo(DistributedSessionState.Completed));}

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
