using System;
using System.IO;
using System.Net;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedPcHost : MonoBehaviour
    {
        readonly byte[] m_SendBuffer=new byte[NetworkProtocolV1.MaximumDatagramBytes];readonly ClockSynchronizer m_ClockSync=new ClockSynchronizer();readonly SequenceTracker m_PoseSequences=new SequenceTracker();readonly SequenceTracker m_ControlSequences=new SequenceTracker();readonly PoseFrameAssembler m_PoseAssembler=new PoseFrameAssembler();readonly KeyboardStateTracker m_KeyboardMirror=new KeyboardStateTracker();
        readonly IHandPoseProcessor m_Processor=new IdentityHandPoseProcessor();
        DistributedSettings m_Settings;PcMidiInput m_Midi;DistributedSessionRecorder m_Recorder;UdpTransport m_Main,m_Clock;IPEndPoint m_Quest;uint m_Sequence,m_CommandId,m_QuestInstanceId;double m_LastQuest,m_LastHeartbeat,m_LastClockRequest,m_RateWindow,m_ConnectedAt;int m_PosePacketsWindow;uint m_PendingCommand;SessionCommand m_PendingAction;int m_Retries;double m_NextRetry;Guid m_SessionId;bool m_PreviouslyConnected;
        public bool NetworkRunning=>m_Main!=null;public bool QuestConnected=>NetworkRunning&&m_Quest!=null&&m_LastQuest>0d&&ResearchServices.Clock.AbsoluteSeconds-m_LastQuest<=m_Settings.connectionTimeoutSeconds;
        public string QuestIpAddress=>m_Quest?.Address.ToString()??"Unknown";public double LastHeartbeat=>m_LastQuest;public double ClockOffset=>m_ClockSync.OffsetSeconds;public double Rtt=>m_ClockSync.RttSeconds;public int ClockSamples=>m_ClockSync.SampleCount;public float PoseReceiveRate{get;private set;}public double PoseLossEstimate=>m_PoseSequences.LossRatio;
        public PcMidiInput Midi=>m_Midi;public DistributedSessionRecorder Recorder=>m_Recorder;public DistributedSettings Settings=>m_Settings;public DistributedSessionState SessionState=>m_Recorder!=null?m_Recorder.State:DistributedSessionState.Idle;
        public string SavePath=>m_Recorder!=null?m_Recorder.SessionPath:string.Empty;
        public double LastReceivedTimestamp=>m_LastQuest;public double LastReceivedAge=>m_LastQuest>0d?Math.Max(0d,ResearchServices.Clock.AbsoluteSeconds-m_LastQuest):-1d;public double ConnectedAt=>m_ConnectedAt;public ulong ReceivedPacketCount=>m_PoseSequences.Received+m_ControlSequences.Received;public ulong MissingPacketCount=>m_PoseSequences.Missing+m_ControlSequences.Missing;public ulong PoseMissingPacketCount=>m_PoseSequences.Missing;
        public string BuildIdentifier=>DistributedBuildInfo.Identifier;public string BindAddress=>m_Main!=null?"0.0.0.0:"+m_Main.LocalPort:"Not bound";
        void Awake()
        {
            m_Settings=FindFirstObjectByType<DistributedSettings>();if(m_Settings==null)m_Settings=gameObject.AddComponent<DistributedSettings>();m_Settings.executionMode=ResearchExecutionMode.DistributedPcHost;m_Settings.ValidateRuntime();
            m_Midi=FindFirstObjectByType<PcMidiInput>();if(m_Midi==null)m_Midi=gameObject.AddComponent<PcMidiInput>();m_Recorder=FindFirstObjectByType<DistributedSessionRecorder>();if(m_Recorder==null)m_Recorder=gameObject.AddComponent<DistributedSessionRecorder>();
            m_Midi.MessageReceived+=OnMidi;m_KeyboardMirror.StateChanged+=OnKeyboardState;
        }
        void Start()=>StartNetwork();
        public bool StartNetwork()
        {
            if(NetworkRunning)return true;try{m_Main=new UdpTransport(m_Settings.poseQueueCapacity+m_Settings.controlQueueCapacity);m_Main.Start(m_Settings.pcReceivePort);m_Clock=new UdpTransport(m_Settings.controlQueueCapacity);m_Clock.Start(m_Settings.clockSyncPort);m_RateWindow=ResearchServices.Clock.AbsoluteSeconds;Debug.Log("Host startup diagnostics\nBuild identifier="+BuildIdentifier+"\nBind address=0.0.0.0\nPose receive port="+m_Settings.pcReceivePort+"\nMIDI destination port="+m_Settings.questReceivePort+"\nClock Sync port="+m_Settings.clockSyncPort+"\nWindows Firewall=Allow PianoDistributedHost on Private networks. Firewall settings are not changed by this application.",this);return true;}catch(Exception e){Debug.LogWarning("Host UDP start failed: "+e.Message,this);StopNetwork();return false;}
        }
        public void StopNetwork(){m_Main?.Dispose();m_Clock?.Dispose();m_Main=m_Clock=null;m_Quest=null;if(m_Recorder!=null&&m_Recorder.State==DistributedSessionState.Recording)m_Recorder.End("network_stopped");}
        void Update()
        {
            Drain(m_Main);Drain(m_Clock);var now=ResearchServices.Clock.AbsoluteSeconds;
            if(QuestConnected&&now-m_LastHeartbeat>=m_Settings.heartbeatIntervalSeconds){m_LastHeartbeat=now;var n=NetworkProtocolV1.WriteHeartbeat(m_SendBuffer,++m_Sequence,now,m_SessionId,0,(byte)(SessionState==DistributedSessionState.Recording?1:0),m_ClockSync.OffsetSeconds,m_ClockSync.RttSeconds);m_Main.Send(m_SendBuffer,n,m_Quest);}
            if(QuestConnected&&(m_ClockSync.SampleCount<m_Settings.initialClockSyncSamples?now-m_LastClockRequest>=0.08d:now-m_LastClockRequest>=m_Settings.clockResyncIntervalSeconds))SendClockRequest(now);
            if(m_PendingCommand!=0&&now>=m_NextRetry)RetryControl(now);m_PoseAssembler.Expire(now,0.25d);
            var connected=QuestConnected;if(m_PreviouslyConnected&&!connected){m_Recorder.RecordNetwork(now,"Quest disconnected",PacketType.Heartbeat,0,0,false,false,default,m_PoseAssembler.IncompleteFramesDropped,m_Main!=null?m_Main.QueueDepth:0);Debug.LogWarning("Host network state: Quest Disconnected at "+now.ToString("F6")+" s. Listening continues for automatic reconnection.",this);}else if(!m_PreviouslyConnected&&connected){m_ConnectedAt=now;m_Recorder.RecordNetwork(now,"Quest connected",PacketType.StartupHello,0,0,false,true,default,m_PoseAssembler.IncompleteFramesDropped,m_Main.QueueDepth);Debug.Log("Host network connected to Quest at "+now.ToString("F6")+" s from "+m_Quest,this);}m_PreviouslyConnected=connected;
            if(now-m_RateWindow>=1d){PoseReceiveRate=(float)(m_PosePacketsWindow/(now-m_RateWindow));m_PosePacketsWindow=0;m_RateWindow=now;}
        }
        void Drain(UdpTransport transport)
        {
            if(transport==null)return;while(transport.TryDequeue(out var d)){try{if(!NetworkProtocolV1.TryReadHeader(d.Buffer,d.Length,out var h))continue;m_LastQuest=d.ReceiveTimestamp;m_Quest=new IPEndPoint(d.Remote.Address,m_Settings.questReceivePort);
                if(h.Type==PacketType.StartupHello&&NetworkProtocolV1.TryReadStartupHello(d.Buffer,d.Length,out var hello)){HandleStartupHello(hello,d.ReceiveTimestamp);continue;}
                if(h.Type==PacketType.Pose&&NetworkProtocolV1.TryReadPose(d.Buffer,d.Length,out var pose))HandlePose(pose,d.ReceiveTimestamp,d.Length);
                else if(h.Type==PacketType.ClockSyncResponse&&NetworkProtocolV1.TryReadClockResponse(d.Buffer,d.Length,out var response)){m_ControlSequences.Observe(h.Sequence);var sample=m_ClockSync.Add(response.PcT0,response.QuestQ1,response.QuestQ2,d.ReceiveTimestamp);m_Recorder.RecordClock(sample,m_ClockSync.Selected.Index==sample.Index);}
                else if(h.Type==PacketType.SessionAck&&NetworkProtocolV1.TryReadSessionAck(d.Buffer,d.Length,out var ack)){m_ControlSequences.Observe(h.Sequence);HandleAck(ack);}
                else {m_ControlSequences.Observe(h.Sequence);if(h.Type==PacketType.Diagnostic)m_Recorder.RecordNetwork(d.ReceiveTimestamp,"Quest->PC",PacketType.Diagnostic,h.Sequence,d.Length,false,true,default,m_PoseAssembler.IncompleteFramesDropped,transport.QueueDepth);}
            }finally{transport.Recycle(d);}}
        }
        void HandleStartupHello(StartupHelloPacket hello,double receiveTimestamp)
        {
            if(m_QuestInstanceId!=hello.InstanceId){m_QuestInstanceId=hello.InstanceId;m_PoseSequences.Reset();m_ControlSequences.Reset();}m_ControlSequences.Observe(hello.Header.Sequence);var now=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WriteStartupAck(m_SendBuffer,++m_Sequence,now,m_SessionId,hello.InstanceId);var sent=m_Main.Send(m_SendBuffer,n,m_Quest);m_Recorder.RecordNetwork(receiveTimestamp,"Quest startup hello",PacketType.StartupHello,hello.Header.Sequence,NetworkProtocolV1.HeaderSize+hello.Header.PayloadLength,sent,true,default,0,m_Main.QueueDepth);
        }
        void HandlePose(PosePacket p,double receive,int bytes)
        {
            ++m_PosePacketsWindow;var obs=m_PoseSequences.Observe(p.Header.Sequence);var estimated=m_ClockSync.HasEstimate?m_ClockSync.QuestToPc(p.Header.SenderTimestamp):receive;
            if(m_PoseAssembler.Accept(p,receive,out var frame))for(var i=0;i<frame.Count;++i)m_Recorder.RecordPose(frame[i],receive,estimated,m_ClockSync.OffsetSeconds,m_ClockSync.RttSeconds,m_ClockSync.SampleCount);m_Recorder.RecordNetwork(receive,"Quest->PC",PacketType.Pose,p.Header.Sequence,bytes,false,true,obs,(long)m_PoseSequences.Missing+m_PoseAssembler.IncompleteFramesDropped,m_Main.QueueDepth);
            if(m_Quest!=null){var now=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WritePose(m_SendBuffer,++m_Sequence,now,m_SessionId,p.UnityFrame,p.CallbackIndex,p.UpdateType,p.SuccessFlags,p.LeftTracked,p.RightTracked,p.HeadValid,p.ChunkIndex,p.ChunkCount,p.LeftRoot,p.RightRoot,p.HeadPose,p.Joints,0,p.Joints.Count,PacketType.CorrectedPose);m_Main.Send(m_SendBuffer,n,m_Quest);}
        }
        void OnMidi(MidiMessage message)
        {
            uint sequence=0;if(QuestConnected){sequence=++m_Sequence;var n=NetworkProtocolV1.WriteMidi(m_SendBuffer,sequence,message.AbsoluteTimeSeconds,m_SessionId,in message,Fnv1a(message.DeviceName));var sent=m_Main.Send(m_SendBuffer,n,m_Quest);m_Recorder.RecordNetwork(message.AbsoluteTimeSeconds,"PC->Quest",PacketType.Midi,sequence,n,sent,false,default,0,m_Main.QueueDepth);}
            m_Recorder.RecordMidi(in message,sequence);m_KeyboardMirror.Apply(in message);
        }
        void OnKeyboardState(KeyboardStateChange c)=>m_Recorder.RecordKeyboard(c.AbsoluteTimeSeconds,c.NoteNumber,c.Pressed,c.Velocity,c.SustainActive);
        void SendClockRequest(double now){if(m_Quest==null)return;m_LastClockRequest=now;var n=NetworkProtocolV1.WriteClockRequest(m_SendBuffer,++m_Sequence,now,m_SessionId,now);m_Main.Send(m_SendBuffer,n,m_Quest);}
        public bool StartSession()
        {
            if(!QuestConnected||m_PendingCommand!=0||SessionState==DistributedSessionState.Recording)return false;m_SessionId=Guid.NewGuid();m_PendingAction=SessionCommand.Start;m_PendingCommand=++m_CommandId;m_Retries=0;RetryControl(ResearchServices.Clock.AbsoluteSeconds);return true;
        }
        public bool StopSession(){if(m_PendingCommand!=0||SessionState!=DistributedSessionState.Recording)return false;m_PendingAction=SessionCommand.Stop;m_PendingCommand=++m_CommandId;m_Retries=0;RetryControl(ResearchServices.Clock.AbsoluteSeconds);return true;}
        void RetryControl(double now)
        {
            if(m_Quest==null||m_Retries>=m_Settings.controlRetryCount){if(m_PendingAction==SessionCommand.Stop&&SessionState==DistributedSessionState.Recording)m_Recorder.End("session_stop_ack_timeout");m_PendingCommand=0;return;}
            var n=NetworkProtocolV1.WriteSessionControl(m_SendBuffer,++m_Sequence,now,m_SessionId,m_PendingAction,m_PendingCommand);m_Main.Send(m_SendBuffer,n,m_Quest);++m_Retries;m_NextRetry=now+m_Settings.controlRetrySeconds;
        }
        void HandleAck(SessionAckPacket ack)
        {
            if(ack.CommandId!=m_PendingCommand||ack.Command!=m_PendingAction)return;m_PendingCommand=0;
            if(ack.Status==SessionAckStatus.Rejected)return;if(ack.Command==SessionCommand.Start){if(m_Recorder.Begin(m_SessionId,QuestIpAddress,m_Midi.ConnectedDeviceName))for(var i=0;i<m_ClockSync.History.Count;++i)m_Recorder.RecordClock(m_ClockSync.History[i],m_ClockSync.Selected.Index==m_ClockSync.History[i].Index);}else if(SessionState==DistributedSessionState.Recording)m_Recorder.End("normal");
        }
        public string ExportDiagnostics(){var path=Path.Combine(Application.persistentDataPath,"PianoResearch","network_diagnostics_snapshot_"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".txt");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,$"quest={QuestIpAddress}\nconnected={QuestConnected}\nclock_offset={ClockOffset:R}\nrtt={Rtt:R}\npose_rate={PoseReceiveRate:R}\nloss={PoseLossEstimate:R}\n");return path;}
        static uint Fnv1a(string value){uint hash=2166136261;for(var i=0;i<(value?.Length??0);++i){hash^=value[i];hash*=16777619;}return hash;}
        void OnDestroy(){if(m_Midi!=null)m_Midi.MessageReceived-=OnMidi;m_KeyboardMirror.StateChanged-=OnKeyboardState;StopNetwork();}
    }
}
