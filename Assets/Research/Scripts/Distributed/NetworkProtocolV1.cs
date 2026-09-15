using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    public enum PacketType : ushort { Pose=1, Midi=2, ClockSyncRequest=3, ClockSyncResponse=4, SessionControl=5, SessionAck=6, Heartbeat=7, Diagnostic=8, CorrectedPose=9, StartupHello=10, StartupAck=11 }
    public enum SessionCommand : byte { Start=1, Stop=2 }
    public enum SessionAckStatus : byte { Accepted=1, AlreadyApplied=2, Rejected=3 }

    public readonly struct PacketHeader
    {
        public readonly PacketType Type; public readonly uint Sequence; public readonly double SenderTimestamp;
        public readonly Guid SessionId; public readonly ushort PayloadLength;
        public PacketHeader(PacketType type,uint sequence,double timestamp,Guid sessionId,ushort payloadLength){Type=type;Sequence=sequence;SenderTimestamp=timestamp;SessionId=sessionId;PayloadLength=payloadLength;}
    }
    public struct PoseJointSample { public byte Hand; public ushort JointId; public bool PoseValid; public uint TrackingState; public Vector3 Position; public Quaternion Rotation; }
    public sealed class PosePacket
    {
        public PacketHeader Header; public int UnityFrame; public long CallbackIndex; public byte UpdateType; public uint SuccessFlags;
        public bool LeftTracked,RightTracked,HeadValid; public ushort ChunkIndex,ChunkCount; public Pose LeftRoot,RightRoot,HeadPose;
        public readonly List<PoseJointSample> Joints=new List<PoseJointSample>(24);
    }
    public struct MidiPacket
    {
        public PacketHeader Header; public long EventIndex; public MidiEventType EventType; public byte Channel,Note,Velocity,Control,Value; public uint DeviceNameHash;
    }
    public struct ClockSyncRequestPacket { public PacketHeader Header; public double PcT0; }
    public struct ClockSyncResponsePacket { public PacketHeader Header; public double PcT0,QuestQ1,QuestQ2; }
    public struct SessionControlPacket { public PacketHeader Header; public SessionCommand Command; public uint CommandId; }
    public struct SessionAckPacket { public PacketHeader Header; public SessionCommand Command; public SessionAckStatus Status; public uint CommandId; }
    public struct HeartbeatPacket { public PacketHeader Header; public uint LastReceivedSequence; public byte State; public double ClockOffset,Rtt; }
    public struct DiagnosticPacket { public PacketHeader Header; public uint PosePackets,SendFailures,Dropped,QueueDepth; }
    public struct StartupHelloPacket { public PacketHeader Header; public uint InstanceId,ApplicationVersionHash,BuildIdHash; }
    public struct StartupAckPacket { public PacketHeader Header; public uint InstanceId; }

    public static class NetworkProtocolV1
    {
        public const uint Magic=0x51504D31; public const ushort Version=1; public const int HeaderSize=38;
        public const int MaximumDatagramBytes=1200; public const int MaximumJointsPerPosePacket=24;

        public static bool TryReadHeader(byte[] data,int length,out PacketHeader header)
        {
            header=default; if(data==null||length<HeaderSize||ReadU32(data,0)!=Magic||ReadU16(data,4)!=Version)return false;
            var rawType=ReadU16(data,6); if(!Enum.IsDefined(typeof(PacketType),rawType))return false;
            var payload=ReadU16(data,36); if(payload!=length-HeaderSize)return false;
            var guidBytes=new byte[16]; Buffer.BlockCopy(data,20,guidBytes,0,16);
            header=new PacketHeader((PacketType)rawType,ReadU32(data,8),ReadF64(data,12),new Guid(guidBytes),payload); return true;
        }

        public static int WritePose(byte[] data,uint sequence,double timestamp,Guid sessionId,int unityFrame,long callbackIndex,byte updateType,uint successFlags,bool leftTracked,bool rightTracked,bool headValid,ushort chunkIndex,ushort chunkCount,Pose leftRoot,Pose rightRoot,Pose headPose,IList<PoseJointSample> joints,int start,int count,PacketType type=PacketType.Pose)
        {
            var payload=111+count*36; var length=HeaderSize+payload; if(data==null||length>data.Length||length>MaximumDatagramBytes)throw new ArgumentException("Pose datagram buffer is too small.");
            WriteHeader(data,type,sequence,timestamp,sessionId,(ushort)payload); var o=HeaderSize;
            WriteI32(data,ref o,unityFrame); WriteI64(data,ref o,callbackIndex); data[o++]=updateType; WriteU32(data,ref o,successFlags);
            data[o++]=(byte)(leftTracked?1:0);data[o++]=(byte)(rightTracked?1:0);data[o++]=(byte)(headValid?1:0);data[o++]=0;
            WriteU16(data,ref o,chunkIndex);WriteU16(data,ref o,chunkCount);WritePose(data,ref o,leftRoot);WritePose(data,ref o,rightRoot);WritePose(data,ref o,headPose);WriteU16(data,ref o,(ushort)count);
            for(var i=0;i<count;++i){var j=joints[start+i];data[o++]=j.Hand;WriteU16(data,ref o,j.JointId);data[o++]=(byte)(j.PoseValid?1:0);WriteU32(data,ref o,j.TrackingState);WriteVector3(data,ref o,j.Position);WriteQuaternion(data,ref o,j.Rotation);}
            return length;
        }
        public static bool TryReadPose(byte[] data,int length,out PosePacket packet)
        {
            packet=null;if(!TryReadHeader(data,length,out var h)||(h.Type!=PacketType.Pose&&h.Type!=PacketType.CorrectedPose)||h.PayloadLength<111)return false;
            var o=HeaderSize;var p=new PosePacket{Header=h,UnityFrame=ReadI32(data,ref o),CallbackIndex=ReadI64(data,ref o)};p.UpdateType=data[o++];p.SuccessFlags=ReadU32(data,ref o);p.LeftTracked=data[o++]!=0;p.RightTracked=data[o++]!=0;p.HeadValid=data[o++]!=0;o++;p.ChunkIndex=ReadU16(data,ref o);p.ChunkCount=ReadU16(data,ref o);p.LeftRoot=ReadPose(data,ref o);p.RightRoot=ReadPose(data,ref o);p.HeadPose=ReadPose(data,ref o);var count=ReadU16(data,ref o);
            if(count>MaximumJointsPerPosePacket||o+count*36!=length)return false;
            for(var i=0;i<count;++i){var j=new PoseJointSample{Hand=data[o++],JointId=ReadU16(data,ref o),PoseValid=data[o++]!=0,TrackingState=ReadU32(data,ref o),Position=ReadVector3(data,ref o),Rotation=ReadQuaternion(data,ref o)};if(j.Hand>1)return false;p.Joints.Add(j);}packet=p;return true;
        }
        public static int WriteMidi(byte[] data,uint sequence,double timestamp,Guid sessionId,in MidiMessage message,uint deviceHash)
        {
            const ushort payload=18;WriteHeader(data,PacketType.Midi,sequence,timestamp,sessionId,payload);var o=HeaderSize;WriteI64(data,ref o,message.EventIndex);data[o++]=(byte)message.EventType;data[o++]=(byte)message.Channel;data[o++]=(byte)(message.NoteNumber<0?255:message.NoteNumber);data[o++]=(byte)(message.Velocity<0?0:message.Velocity);data[o++]=(byte)(message.ControlNumber<0?255:message.ControlNumber);data[o++]=(byte)(message.ControlValue<0?0:message.ControlValue);WriteU32(data,ref o,deviceHash);return o;
        }
        public static bool TryReadMidi(byte[] data,int length,out MidiPacket packet)
        {
            packet=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.Midi||h.PayloadLength!=18)return false;var o=HeaderSize;packet.Header=h;packet.EventIndex=ReadI64(data,ref o);packet.EventType=(MidiEventType)data[o++];packet.Channel=data[o++];packet.Note=data[o++];packet.Velocity=data[o++];packet.Control=data[o++];packet.Value=data[o++];packet.DeviceNameHash=ReadU32(data,ref o);if(packet.EventType==MidiEventType.NoteOn&&packet.Velocity==0)packet.EventType=MidiEventType.NoteOff;return Enum.IsDefined(typeof(MidiEventType),packet.EventType)&&packet.Channel>=1&&packet.Channel<=16;
        }
        public static int WriteClockRequest(byte[] data,uint sequence,double now,Guid sessionId,double t0){WriteHeader(data,PacketType.ClockSyncRequest,sequence,now,sessionId,8);var o=HeaderSize;WriteF64(data,ref o,t0);return o;}
        public static bool TryReadClockRequest(byte[] data,int length,out ClockSyncRequestPacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.ClockSyncRequest||h.PayloadLength!=8)return false;p.Header=h;p.PcT0=ReadF64(data,HeaderSize);return true;}
        public static int WriteClockResponse(byte[] data,uint sequence,double now,Guid sessionId,double t0,double q1,double q2){WriteHeader(data,PacketType.ClockSyncResponse,sequence,now,sessionId,24);var o=HeaderSize;WriteF64(data,ref o,t0);WriteF64(data,ref o,q1);WriteF64(data,ref o,q2);return o;}
        public static bool TryReadClockResponse(byte[] data,int length,out ClockSyncResponsePacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.ClockSyncResponse||h.PayloadLength!=24)return false;var o=HeaderSize;p.Header=h;p.PcT0=ReadF64(data,ref o);p.QuestQ1=ReadF64(data,ref o);p.QuestQ2=ReadF64(data,ref o);return true;}
        public static int WriteSessionControl(byte[] data,uint seq,double now,Guid sessionId,SessionCommand command,uint commandId){WriteHeader(data,PacketType.SessionControl,seq,now,sessionId,5);var o=HeaderSize;data[o++]=(byte)command;WriteU32(data,ref o,commandId);return o;}
        public static bool TryReadSessionControl(byte[] data,int length,out SessionControlPacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.SessionControl||h.PayloadLength!=5)return false;var o=HeaderSize;p.Header=h;p.Command=(SessionCommand)data[o++];p.CommandId=ReadU32(data,ref o);return Enum.IsDefined(typeof(SessionCommand),p.Command);}
        public static int WriteSessionAck(byte[] data,uint seq,double now,Guid sessionId,SessionCommand command,SessionAckStatus status,uint commandId){WriteHeader(data,PacketType.SessionAck,seq,now,sessionId,6);var o=HeaderSize;data[o++]=(byte)command;data[o++]=(byte)status;WriteU32(data,ref o,commandId);return o;}
        public static bool TryReadSessionAck(byte[] data,int length,out SessionAckPacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.SessionAck||h.PayloadLength!=6)return false;var o=HeaderSize;p.Header=h;p.Command=(SessionCommand)data[o++];p.Status=(SessionAckStatus)data[o++];p.CommandId=ReadU32(data,ref o);return true;}
        public static int WriteHeartbeat(byte[] data,uint seq,double now,Guid sessionId,uint lastSequence,byte state,double offset=0d,double rtt=0d){WriteHeader(data,PacketType.Heartbeat,seq,now,sessionId,21);var o=HeaderSize;WriteU32(data,ref o,lastSequence);data[o++]=state;WriteF64(data,ref o,offset);WriteF64(data,ref o,rtt);return o;}
        public static bool TryReadHeartbeat(byte[] data,int length,out HeartbeatPacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.Heartbeat||h.PayloadLength!=21)return false;var o=HeaderSize;p.Header=h;p.LastReceivedSequence=ReadU32(data,ref o);p.State=data[o++];p.ClockOffset=ReadF64(data,ref o);p.Rtt=ReadF64(data,ref o);return true;}
        public static int WriteDiagnostic(byte[] data,uint seq,double now,Guid sessionId,uint packets,uint failures,uint dropped,uint depth){WriteHeader(data,PacketType.Diagnostic,seq,now,sessionId,16);var o=HeaderSize;WriteU32(data,ref o,packets);WriteU32(data,ref o,failures);WriteU32(data,ref o,dropped);WriteU32(data,ref o,depth);return o;}
        public static bool TryReadDiagnostic(byte[] data,int length,out DiagnosticPacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.Diagnostic||h.PayloadLength!=16)return false;var o=HeaderSize;p.Header=h;p.PosePackets=ReadU32(data,ref o);p.SendFailures=ReadU32(data,ref o);p.Dropped=ReadU32(data,ref o);p.QueueDepth=ReadU32(data,ref o);return true;}
        public static int WriteStartupHello(byte[] data,uint seq,double now,Guid sessionId,uint instanceId,uint applicationVersionHash,uint buildIdHash){WriteHeader(data,PacketType.StartupHello,seq,now,sessionId,12);var o=HeaderSize;WriteU32(data,ref o,instanceId);WriteU32(data,ref o,applicationVersionHash);WriteU32(data,ref o,buildIdHash);return o;}
        public static bool TryReadStartupHello(byte[] data,int length,out StartupHelloPacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.StartupHello||h.PayloadLength!=12)return false;var o=HeaderSize;p.Header=h;p.InstanceId=ReadU32(data,ref o);p.ApplicationVersionHash=ReadU32(data,ref o);p.BuildIdHash=ReadU32(data,ref o);return true;}
        public static int WriteStartupAck(byte[] data,uint seq,double now,Guid sessionId,uint instanceId){WriteHeader(data,PacketType.StartupAck,seq,now,sessionId,4);var o=HeaderSize;WriteU32(data,ref o,instanceId);return o;}
        public static bool TryReadStartupAck(byte[] data,int length,out StartupAckPacket p){p=default;if(!TryReadHeader(data,length,out var h)||h.Type!=PacketType.StartupAck||h.PayloadLength!=4)return false;var o=HeaderSize;p.Header=h;p.InstanceId=ReadU32(data,ref o);return true;}

        static void WriteHeader(byte[] d,PacketType type,uint seq,double ts,Guid id,ushort payload){var o=0;WriteU32(d,ref o,Magic);WriteU16(d,ref o,Version);WriteU16(d,ref o,(ushort)type);WriteU32(d,ref o,seq);WriteF64(d,ref o,ts);var g=id.ToByteArray();Buffer.BlockCopy(g,0,d,o,16);o+=16;WriteU16(d,ref o,payload);}
        static void WritePose(byte[] d,ref int o,Pose p){WriteVector3(d,ref o,p.position);WriteQuaternion(d,ref o,p.rotation);}
        static Pose ReadPose(byte[] d,ref int o)=>new Pose(ReadVector3(d,ref o),ReadQuaternion(d,ref o));
        static void WriteVector3(byte[] d,ref int o,Vector3 v){WriteF32(d,ref o,v.x);WriteF32(d,ref o,v.y);WriteF32(d,ref o,v.z);}
        static Vector3 ReadVector3(byte[] d,ref int o)=>new Vector3(ReadF32(d,ref o),ReadF32(d,ref o),ReadF32(d,ref o));
        static void WriteQuaternion(byte[] d,ref int o,Quaternion q){WriteF32(d,ref o,q.x);WriteF32(d,ref o,q.y);WriteF32(d,ref o,q.z);WriteF32(d,ref o,q.w);}
        static Quaternion ReadQuaternion(byte[] d,ref int o)=>new Quaternion(ReadF32(d,ref o),ReadF32(d,ref o),ReadF32(d,ref o),ReadF32(d,ref o));
        static ushort ReadU16(byte[] d,int o)=>(ushort)((d[o]<<8)|d[o+1]); static ushort ReadU16(byte[] d,ref int o){var v=ReadU16(d,o);o+=2;return v;}
        static uint ReadU32(byte[] d,int o)=>(uint)((d[o]<<24)|(d[o+1]<<16)|(d[o+2]<<8)|d[o+3]); static uint ReadU32(byte[] d,ref int o){var v=ReadU32(d,o);o+=4;return v;}
        static int ReadI32(byte[] d,ref int o)=>unchecked((int)ReadU32(d,ref o)); static long ReadI64(byte[] d,ref int o)=>unchecked((long)ReadU64(d,ref o));
        static ulong ReadU64(byte[] d,ref int o){var hi=ReadU32(d,ref o);var lo=ReadU32(d,ref o);return ((ulong)hi<<32)|lo;}
        static double ReadF64(byte[] d,int o){var p=o;return new DoubleBits{Bits=ReadU64(d,ref p)}.Value;} static double ReadF64(byte[] d,ref int o)=>new DoubleBits{Bits=ReadU64(d,ref o)}.Value;
        static float ReadF32(byte[] d,ref int o)=>new FloatBits{Bits=ReadU32(d,ref o)}.Value;
        static void WriteU16(byte[] d,ref int o,ushort v){d[o++]=(byte)(v>>8);d[o++]=(byte)v;}
        static void WriteU32(byte[] d,ref int o,uint v){d[o++]=(byte)(v>>24);d[o++]=(byte)(v>>16);d[o++]=(byte)(v>>8);d[o++]=(byte)v;}
        static void WriteI32(byte[] d,ref int o,int v)=>WriteU32(d,ref o,unchecked((uint)v)); static void WriteI64(byte[] d,ref int o,long v)=>WriteU64(d,ref o,unchecked((ulong)v));
        static void WriteU64(byte[] d,ref int o,ulong v){WriteU32(d,ref o,(uint)(v>>32));WriteU32(d,ref o,(uint)v);}
        static void WriteF64(byte[] d,ref int o,double v)=>WriteU64(d,ref o,new DoubleBits{Value=v}.Bits); static void WriteF32(byte[] d,ref int o,float v)=>WriteU32(d,ref o,new FloatBits{Value=v}.Bits);
        [StructLayout(LayoutKind.Explicit)] struct FloatBits{[FieldOffset(0)]public float Value;[FieldOffset(0)]public uint Bits;}
        [StructLayout(LayoutKind.Explicit)] struct DoubleBits{[FieldOffset(0)]public double Value;[FieldOffset(0)]public ulong Bits;}
    }
}
