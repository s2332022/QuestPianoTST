using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedQuestClient : MonoBehaviour
    {
        readonly byte[] m_SendBuffer=new byte[NetworkProtocolV1.MaximumDatagramBytes];readonly List<PoseJointSample> m_Joints=new List<PoseJointSample>(64);
        DistributedSettings m_Settings;XRHandPoseProvider m_Hands;NetworkMidiInput m_Midi;VirtualPianoKeyboard m_Keyboard;PianoCalibrationManager m_Calibration;RemoteDisplayPoseProcessor m_RemoteProcessor;UdpTransport m_Transport;IPEndPoint m_Host,m_Clock;
        uint m_Sequence,m_PoseSequence,m_TotalPosePackets;double m_LastPoseSend,m_LastHostPacket,m_LastHeartbeatSend,m_RateWindow;int m_RatePose,m_RateMidi;uint m_LastReceived;Guid m_SessionId;bool m_PreviouslyConnected;readonly HashSet<uint> m_AppliedCommands=new HashSet<uint>();
        public bool NetworkRunning=>m_Transport!=null;public bool PcConnected=>NetworkRunning&&ResearchServices.Clock.AbsoluteSeconds-m_LastHostPacket<=m_Settings.connectionTimeoutSeconds;
        public double LastHeartbeatSeconds=>m_LastHostPacket;public double RttSeconds{get;private set;}public bool ClockSynchronized{get;private set;}public float PoseSendRate{get;private set;}public float MidiReceiveRate{get;private set;}public string LastMidiEvent=>m_Midi!=null?m_Midi.LastEventText:"None";public string SessionState{get;private set;}="Idle";
        public long PoseSendFailures=>m_Transport?.SendFailures??0;public long PoseDropped{get;private set;}
        void Awake()
        {
            m_Settings=GetComponent<DistributedSettings>();if(m_Settings==null)m_Settings=gameObject.AddComponent<DistributedSettings>();m_Settings.executionMode=ResearchExecutionMode.DistributedQuestClient;m_Settings.ValidateRuntime();
            m_Hands=FindFirstObjectByType<XRHandPoseProvider>();m_Midi=FindFirstObjectByType<NetworkMidiInput>();m_Keyboard=FindFirstObjectByType<VirtualPianoKeyboard>();m_Calibration=FindFirstObjectByType<PianoCalibrationManager>();
            if(m_Midi==null)m_Midi=gameObject.AddComponent<NetworkMidiInput>();if(m_Keyboard!=null)m_Midi.MessageReceived+=OnMidi;
            if(m_Hands!=null){var count=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;m_RemoteProcessor=new RemoteDisplayPoseProcessor(count);m_Hands.SetProcessor(m_RemoteProcessor);m_Hands.RawFrameUpdated+=OnRawFrame;m_Hands.ConfigureUpdateTypes(m_Settings.poseSendPolicy!=PoseSendPolicy.BeforeRenderOnly,m_Settings.poseSendPolicy!=PoseSendPolicy.DynamicOnly);}
        }
        void Start(){StartNetwork();}
        public bool StartNetwork()
        {
            if(NetworkRunning)return true;if(!IPAddress.TryParse(m_Settings.pcIpAddress,out var ip))return false;
            try{m_Host=new IPEndPoint(ip,m_Settings.pcReceivePort);m_Clock=new IPEndPoint(ip,m_Settings.clockSyncPort);m_Transport=new UdpTransport(m_Settings.controlQueueCapacity+m_Settings.midiQueueCapacity);m_Transport.Start(m_Settings.questReceivePort);m_LastHostPacket=ResearchServices.Clock.AbsoluteSeconds;return true;}catch(Exception e){Debug.LogWarning("Quest UDP start failed: "+e.Message,this);m_Transport?.Dispose();m_Transport=null;return false;}
        }
        public void StopNetwork(){m_Transport?.Dispose();m_Transport=null;if(m_Midi!=null)m_Midi.Disconnect();if(m_RemoteProcessor!=null)m_RemoteProcessor.Connected=false;SessionState="Disconnected";}
        void Update()
        {
            Drain();var now=ResearchServices.Clock.AbsoluteSeconds;
            if(NetworkRunning&&now-m_LastHeartbeatSend>=m_Settings.heartbeatIntervalSeconds){m_LastHeartbeatSend=now;var n=NetworkProtocolV1.WriteHeartbeat(m_SendBuffer,++m_Sequence,now,m_SessionId,m_LastReceived,(byte)(SessionState=="Recording"?1:0));m_Transport.Send(m_SendBuffer,n,m_Host);n=NetworkProtocolV1.WriteDiagnostic(m_SendBuffer,++m_Sequence,now,m_SessionId,m_TotalPosePackets,(uint)m_Transport.SendFailures,(uint)PoseDropped,(uint)m_Transport.QueueDepth);m_Transport.Send(m_SendBuffer,n,m_Host);}
            var connected=PcConnected;if(m_PreviouslyConnected&&!connected){m_Midi.Disconnect();SessionState="Disconnected";}m_PreviouslyConnected=connected;if(m_RemoteProcessor!=null){m_RemoteProcessor.Connected=connected;m_RemoteProcessor.UseRemote=m_Settings.displaySource==PoseDisplaySource.PcDisplayPose;}
            if(now-m_RateWindow>=1d){var dt=now-m_RateWindow;PoseSendRate=(float)(m_RatePose/dt);MidiReceiveRate=(float)(m_RateMidi/dt);m_RatePose=m_RateMidi=0;m_RateWindow=now;}
        }
        void Drain()
        {
            if(m_Transport==null)return;while(m_Transport.TryDequeue(out var d)){try{if(!NetworkProtocolV1.TryReadHeader(d.Buffer,d.Length,out var h))continue;m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;
                if(h.Type==PacketType.Midi&&NetworkProtocolV1.TryReadMidi(d.Buffer,d.Length,out var midi)){if(m_Midi.Enqueue(in midi))++m_RateMidi;}
                else if(h.Type==PacketType.ClockSyncRequest&&NetworkProtocolV1.TryReadClockRequest(d.Buffer,d.Length,out var req)){var q1=d.ReceiveTimestamp;var q2=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WriteClockResponse(m_SendBuffer,++m_Sequence,q2,m_SessionId,req.PcT0,q1,q2);m_Transport.Send(m_SendBuffer,n,m_Clock);}
                else if(h.Type==PacketType.SessionControl&&NetworkProtocolV1.TryReadSessionControl(d.Buffer,d.Length,out var control))ApplyControl(control);
                else if(h.Type==PacketType.CorrectedPose&&NetworkProtocolV1.TryReadPose(d.Buffer,d.Length,out var corrected))m_RemoteProcessor?.Accept(corrected);
                else if(h.Type==PacketType.Heartbeat&&NetworkProtocolV1.TryReadHeartbeat(d.Buffer,d.Length,out var heartbeat)){RttSeconds=heartbeat.Rtt;ClockSynchronized=heartbeat.Rtt>0d;}
            }finally{m_Transport.Recycle(d);}}
        }
        void ApplyControl(SessionControlPacket p)
        {
            var duplicate=!m_AppliedCommands.Add(p.CommandId);m_SessionId=p.Header.SessionId;if(!duplicate)SessionState=p.Command==SessionCommand.Start?"Recording":"Idle";
            var now=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WriteSessionAck(m_SendBuffer,++m_Sequence,now,m_SessionId,p.Command,duplicate?SessionAckStatus.AlreadyApplied:SessionAckStatus.Accepted,p.CommandId);m_Transport.Send(m_SendBuffer,n,m_Host);
        }
        void OnRawFrame(HandPoseFrame frame)
        {
            if(!NetworkRunning)return;var now=ResearchServices.Clock.AbsoluteSeconds;var interval=1d/m_Settings.poseSendHz;if(now-m_LastPoseSend<interval){++PoseDropped;return;}m_LastPoseSend=now;m_Joints.Clear();
            if(m_Settings.sendLeftHand)AddJoints(0,frame.LeftJoints);if(m_Settings.sendRightHand)AddJoints(1,frame.RightJoints);
            var chunks=Math.Max(1,(m_Joints.Count+NetworkProtocolV1.MaximumJointsPerPosePacket-1)/NetworkProtocolV1.MaximumJointsPerPosePacket);var camera=Camera.main;var headValid=m_Settings.sendHeadPose&&camera!=null;var head=headValid?new Pose(camera.transform.position,camera.transform.rotation):Pose.identity;
            for(var chunk=0;chunk<chunks;++chunk){var start=chunk*NetworkProtocolV1.MaximumJointsPerPosePacket;var count=Math.Min(NetworkProtocolV1.MaximumJointsPerPosePacket,m_Joints.Count-start);var n=NetworkProtocolV1.WritePose(m_SendBuffer,++m_PoseSequence,frame.AbsoluteTimeSeconds,m_SessionId,frame.UnityFrame,frame.CallbackIndex,(byte)frame.UpdateType,(uint)frame.SuccessFlags,frame.LeftTracked,frame.RightTracked,headValid,(ushort)chunk,(ushort)chunks,frame.LeftRootPose,frame.RightRootPose,head,m_Joints,start,count);++m_TotalPosePackets;if(m_Transport.Send(m_SendBuffer,n,m_Host))++m_RatePose;}
        }
        void AddJoints(byte hand,HandJointPose[] source){for(var i=0;i<source.Length;++i){var j=source[i];m_Joints.Add(new PoseJointSample{Hand=hand,JointId=(ushort)j.JointId,PoseValid=j.PoseValid,TrackingState=(uint)j.TrackingState,Position=j.Pose.position,Rotation=j.Pose.rotation});}}
        void OnMidi(MidiMessage m){m_Keyboard?.ApplyMidi(in m);}
        void OnDestroy(){if(m_Hands!=null)m_Hands.RawFrameUpdated-=OnRawFrame;if(m_Midi!=null)m_Midi.MessageReceived-=OnMidi;StopNetwork();}
    }
}
