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
        DistributedSettings m_Settings;PcMidiInput m_Midi;DistributedSessionRecorder m_Recorder;UdpTransport m_Main,m_Clock;IPEndPoint m_Quest;uint m_Sequence,m_CommandId,m_QuestInstanceId,m_SnapshotGeneration;double m_LastQuest,m_LastHeartbeat,m_LastClockRequest,m_LastSnapshotSent,m_RateWindow,m_ConnectedAt,m_TimedOutUntil;int m_PosePacketsWindow;uint m_PendingCommand;SessionCommand m_PendingAction;int m_Retries;double m_NextRetry;Guid m_SessionId,m_QuestSessionId,m_ConnectionSessionId;bool m_PreviouslyConnected,m_PreviousMidiConnected,m_AcceptPackets,m_ApplicationQuit;
        ulong m_SnapshotSent,m_SnapshotReceived,m_SnapshotApplied,m_SnapshotStaleDropped,m_SnapshotSessionMismatchDropped,m_SnapshotNoteRepairs,m_SnapshotCc64Repairs;uint m_RemoteLastSnapshotGeneration;
        const double KeyboardSnapshotIntervalSeconds=0.25d;
        public DistributedNetworkState NetworkState{get;private set;}=DistributedNetworkState.Stopped;public bool NetworkRunning=>m_Main!=null&&m_Clock!=null;public bool QuestConnected=>NetworkRunning&&NetworkState==DistributedNetworkState.Connected&&IsConnectionAlive(ResearchServices.Clock.AbsoluteSeconds);
        public string QuestIpAddress=>m_Quest?.Address.ToString()??"Unknown";public double LastHeartbeat=>m_LastQuest;public double ClockOffset=>m_ClockSync.OffsetSeconds;public double Rtt=>m_ClockSync.RttSeconds;public int ClockSamples=>m_ClockSync.SampleCount;public float PoseReceiveRate{get;private set;}public double PoseLossEstimate=>m_PoseSequences.LossRatio;
        public PcMidiInput Midi=>m_Midi;public DistributedSessionRecorder Recorder=>m_Recorder;public DistributedSettings Settings=>m_Settings;public DistributedSessionState SessionState=>m_Recorder!=null?m_Recorder.State:DistributedSessionState.Idle;public string SessionStatus=>SessionState.ToString();
        public string SavePath=>m_Recorder!=null?m_Recorder.SessionPath:string.Empty;
        public double LastReceivedTimestamp=>m_LastQuest;public double LastReceivedAge=>m_LastQuest>0d?Math.Max(0d,ResearchServices.Clock.AbsoluteSeconds-m_LastQuest):-1d;public double ConnectedAt=>m_ConnectedAt;public ulong ReceivedPacketCount=>m_PoseSequences.Received+m_ControlSequences.Received;public ulong MissingPacketCount=>m_PoseSequences.Missing+m_ControlSequences.Missing;public ulong PoseMissingPacketCount=>m_PoseSequences.Missing;
        public string BuildIdentifier=>DistributedBuildInfo.Identifier;public string BindAddress=>m_Main!=null?"0.0.0.0:"+m_Main.LocalPort:"Not bound";
        void Awake()
        {
            m_Settings=FindFirstObjectByType<DistributedSettings>();if(m_Settings==null)m_Settings=gameObject.AddComponent<DistributedSettings>();m_Settings.executionMode=ResearchExecutionMode.DistributedPcHost;m_Settings.ValidateRuntime();
            m_Midi=FindFirstObjectByType<PcMidiInput>();if(m_Midi==null)m_Midi=gameObject.AddComponent<PcMidiInput>();m_Recorder=FindFirstObjectByType<DistributedSessionRecorder>();if(m_Recorder==null)m_Recorder=gameObject.AddComponent<DistributedSessionRecorder>();
            m_PreviousMidiConnected=m_Midi.IsConnected;m_Midi.MessageReceived+=OnMidi;m_KeyboardMirror.StateChanged+=OnKeyboardState;
        }
        void Start()=>StartNetwork();
        public bool StartNetwork()
        {
            if(NetworkRunning)return true;NetworkState=DistributedNetworkState.Connecting;m_TimedOutUntil=0d;ResetConnectionState(false,"network_start");
            try{m_Main=new UdpTransport(m_Settings.poseQueueCapacity+m_Settings.controlQueueCapacity);m_Main.Start(m_Settings.pcReceivePort);m_Clock=new UdpTransport(m_Settings.controlQueueCapacity);m_Clock.Start(m_Settings.clockSyncPort);m_RateWindow=ResearchServices.Clock.AbsoluteSeconds;NetworkState=DistributedNetworkState.Listening;Debug.Log("Host startup diagnostics\nBuild identifier="+BuildIdentifier+"\nBind address=0.0.0.0\nPose receive port="+m_Settings.pcReceivePort+"\nMIDI destination port="+m_Settings.questReceivePort+"\nClock Sync port="+m_Settings.clockSyncPort+"\nWindows Firewall=Allow PianoDistributedHost on Private networks. Firewall settings are not changed by this application.",this);return true;}catch(Exception e){Debug.LogWarning("Host UDP start failed: "+e.Message,this);StopNetworkInternal("socket_error",false);return false;}
        }
        public void StopNetwork()=>StopNetworkInternal("host_stop_network",true);
        void StopNetworkInternal(string reason,bool notifyQuest)
        {
            if(notifyQuest)SendDisconnectHeartbeat();NetworkState=DistributedNetworkState.Stopping;m_Main?.Dispose();m_Clock?.Dispose();m_Main=m_Clock=null;ResetConnectionState(true,reason);m_TimedOutUntil=0d;NetworkState=DistributedNetworkState.Stopped;
        }
        void ResetConnectionState(bool endRecording,string reason)
        {
            var now=ResearchServices.Clock.AbsoluteSeconds;m_KeyboardMirror.Reset(now);if(endRecording&&m_Recorder!=null&&(m_Recorder.State==DistributedSessionState.Recording||m_Recorder.State==DistributedSessionState.Faulted||m_Recorder.State==DistributedSessionState.Starting))m_Recorder.End(reason,m_LastQuest);
            m_Main?.ClearQueuedDatagrams();m_Clock?.ClearQueuedDatagrams();m_PoseSequences.Reset();m_ControlSequences.Reset();m_PoseAssembler.Reset();m_ClockSync.Reset();
            m_QuestInstanceId=0;m_QuestSessionId=Guid.Empty;m_ConnectionSessionId=Guid.Empty;m_SessionId=Guid.Empty;m_Quest=null;m_LastQuest=0d;m_LastHeartbeat=0d;
            m_LastClockRequest=0d;m_LastSnapshotSent=0d;m_SnapshotGeneration=0;m_SnapshotSent=m_SnapshotReceived=m_SnapshotApplied=m_SnapshotStaleDropped=m_SnapshotSessionMismatchDropped=m_SnapshotNoteRepairs=m_SnapshotCc64Repairs=0;m_RemoteLastSnapshotGeneration=0;
            m_PosePacketsWindow=0;m_PendingCommand=0;m_PendingAction=default;m_Sequence=0;
            m_Retries=0;m_NextRetry=0d;m_PreviouslyConnected=false;m_AcceptPackets=false;
        }
        void Update()
        {
            Drain(m_Main);Drain(m_Clock);var now=ResearchServices.Clock.AbsoluteSeconds;m_PoseAssembler.Expire(now,0.25d);
            var connected=IsConnectionAlive(now);
            if(m_PreviouslyConnected&&!connected){HandleConnectionLoss("network_timeout",now);connected=false;}
            else if(!m_PreviouslyConnected&&connected){m_ConnectedAt=now;NetworkState=DistributedNetworkState.Connected;m_Recorder.RecordNetwork(now,"Quest connected",PacketType.StartupHello,0,0,false,true,default,m_PoseAssembler.IncompleteFramesDropped,m_Main!=null?m_Main.QueueDepth:0);Debug.Log("Host network connected to Quest at "+now.ToString("F6")+" s from "+m_Quest,this);SendKeyboardSnapshot(now);}
            else if(connected)NetworkState=DistributedNetworkState.Connected;
            if(NetworkState==DistributedNetworkState.TimedOut&&now>=m_TimedOutUntil)NetworkState=DistributedNetworkState.Listening;
            m_PreviouslyConnected=connected;
            if(connected&&NetworkState==DistributedNetworkState.Connected&&now-m_LastHeartbeat>=m_Settings.heartbeatIntervalSeconds){m_LastHeartbeat=now;var n=NetworkProtocolV1.WriteHeartbeat(m_SendBuffer,++m_Sequence,now,m_QuestSessionId,0,(byte)(SessionState==DistributedSessionState.Recording?HeartbeatState.Recording:HeartbeatState.Idle),m_ClockSync.OffsetSeconds,m_ClockSync.RttSeconds);m_Main.Send(m_SendBuffer,n,m_Quest);}
            if(connected&&(m_ClockSync.SampleCount<m_Settings.initialClockSyncSamples?now-m_LastClockRequest>=0.08d:now-m_LastClockRequest>=m_Settings.clockResyncIntervalSeconds))SendClockRequest(now);
            var midiConnected=m_Midi!=null&&m_Midi.IsConnected;
            if(midiConnected!=m_PreviousMidiConnected){m_PreviousMidiConnected=midiConnected;if(!midiConnected)m_KeyboardMirror.Reset(now);if(QuestConnected)SendKeyboardSnapshot(now);}
            if(QuestConnected&&now-m_LastSnapshotSent>=KeyboardSnapshotIntervalSeconds)SendKeyboardSnapshot(now);
            if(m_PendingCommand!=0&&now>=m_NextRetry)RetryControl(now);
            if(now-m_RateWindow>=1d){PoseReceiveRate=(float)(m_PosePacketsWindow/(now-m_RateWindow));m_PosePacketsWindow=0;m_RateWindow=now;}
        }
        void Drain(UdpTransport transport)
        {
            if(transport==null)return;while(transport.TryDequeue(out var d)){try
            {
                if(!NetworkProtocolV1.TryReadHeader(d.Buffer,d.Length,out var h))continue;
                if(h.Type==PacketType.StartupHello&&NetworkProtocolV1.TryReadStartupHello(d.Buffer,d.Length,out var hello)){HandleStartupHello(hello,d.ReceiveTimestamp,d.Remote);continue;}
                if(!m_AcceptPackets||m_QuestInstanceId==0||(h.SessionId!=m_QuestSessionId&&!(h.Type==PacketType.SessionAck&&m_PendingCommand!=0&&h.SessionId==m_SessionId)))continue;
                if(h.Type==PacketType.Pose&&NetworkProtocolV1.TryReadPose(d.Buffer,d.Length,out var pose)){var obs=m_PoseSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;MarkNormalPacket(d.ReceiveTimestamp,d.Remote);HandlePose(pose,d.ReceiveTimestamp,d.Length,obs);}
                else if(h.Type==PacketType.ClockSyncResponse&&NetworkProtocolV1.TryReadClockResponse(d.Buffer,d.Length,out var response)){var obs=m_ControlSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;MarkNormalPacket(d.ReceiveTimestamp,d.Remote);if(m_ClockSync.TryAdd(response.PcT0,response.QuestQ1,response.QuestQ2,d.ReceiveTimestamp,out var sample))m_Recorder.RecordClock(sample,m_ClockSync.Selected.Index==sample.Index);}
                else if(h.Type==PacketType.SessionAck&&NetworkProtocolV1.TryReadSessionAck(d.Buffer,d.Length,out var ack)){if(ack.Header.SessionId!=m_QuestSessionId&&!(m_PendingCommand!=0&&ack.Header.SessionId==m_SessionId))continue;var obs=m_ControlSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;MarkNormalPacket(d.ReceiveTimestamp,d.Remote);HandleAck(ack);}
                else if(h.Type==PacketType.Heartbeat&&NetworkProtocolV1.TryReadHeartbeat(d.Buffer,d.Length,out var heartbeat)){var obs=m_ControlSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;MarkNormalPacket(d.ReceiveTimestamp,d.Remote);m_Recorder.RecordNetwork(d.ReceiveTimestamp,"Quest->PC",PacketType.Heartbeat,h.Sequence,d.Length,false,true,obs,m_PoseAssembler.IncompleteFramesDropped,transport.QueueDepth);if(heartbeat.State==(byte)HeartbeatState.Disconnecting)HandleConnectionLoss("quest_disconnect",d.ReceiveTimestamp);}
                else if(h.Type==PacketType.Diagnostic&&NetworkProtocolV1.TryReadDiagnostic(d.Buffer,d.Length,out var diagnostic)){var obs=m_ControlSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;MarkNormalPacket(d.ReceiveTimestamp,d.Remote);m_Recorder.RecordNetwork(d.ReceiveTimestamp,"Quest->PC",PacketType.Diagnostic,h.Sequence,d.Length,false,true,obs,m_PoseAssembler.IncompleteFramesDropped,transport.QueueDepth);}
                else if(h.Type==PacketType.KeyboardSnapshotDiagnostics&&NetworkProtocolV1.TryReadKeyboardSnapshotDiagnostics(d.Buffer,d.Length,out var snapshotDiagnostics)){var obs=m_ControlSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;MarkNormalPacket(d.ReceiveTimestamp,d.Remote);m_SnapshotReceived=snapshotDiagnostics.Received;m_SnapshotApplied=snapshotDiagnostics.Applied;m_SnapshotStaleDropped=snapshotDiagnostics.StaleDropped;m_SnapshotSessionMismatchDropped=snapshotDiagnostics.SessionMismatchDropped;m_SnapshotNoteRepairs=snapshotDiagnostics.NoteRepairs;m_SnapshotCc64Repairs=snapshotDiagnostics.Cc64Repairs;m_RemoteLastSnapshotGeneration=snapshotDiagnostics.LastGeneration;PublishKeyboardSnapshotDiagnostics();m_Recorder.RecordNetwork(d.ReceiveTimestamp,"Quest->PC",PacketType.KeyboardSnapshotDiagnostics,h.Sequence,d.Length,false,true,obs,m_PoseAssembler.IncompleteFramesDropped,transport.QueueDepth);}
            }
            finally{transport.Recycle(d);}}
        }
        void HandleStartupHello(StartupHelloPacket hello,double receiveTimestamp,IPEndPoint remote)
        {
            if(m_QuestInstanceId!=hello.InstanceId){ResetConnectionState(true,"quest_disconnect");m_TimedOutUntil=0d;m_QuestInstanceId=hello.InstanceId;m_ConnectionSessionId=Guid.NewGuid();m_SessionId=m_ConnectionSessionId;m_QuestSessionId=m_ConnectionSessionId;m_AcceptPackets=true;NetworkState=DistributedNetworkState.Connecting;}
            m_Quest=new IPEndPoint(remote.Address,m_Settings.questReceivePort);var obs=m_ControlSequences.Observe(hello.Header.Sequence);var sent=false;var now=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WriteStartupAck(m_SendBuffer,++m_Sequence,now,m_ConnectionSessionId,hello.InstanceId);if(m_Main!=null)sent=m_Main.Send(m_SendBuffer,n,m_Quest);if(IsFresh(obs)){MarkNormalPacket(receiveTimestamp,remote);m_Recorder.RecordNetwork(receiveTimestamp,"Quest startup hello",PacketType.StartupHello,hello.Header.Sequence,NetworkProtocolV1.HeaderSize+hello.Header.PayloadLength,sent,true,obs,0,m_Main!=null?m_Main.QueueDepth:0);}
        }
        void HandlePose(PosePacket p,double receive,int bytes,SequenceObservation obs)
        {
            ++m_PosePacketsWindow;var estimated=m_ClockSync.HasEstimate?m_ClockSync.QuestToPc(p.Header.SenderTimestamp):receive;
            if(m_PoseAssembler.Accept(p,receive,out var frame))for(var i=0;i<frame.Count;++i)m_Recorder.RecordPose(frame[i],receive,estimated,m_ClockSync.OffsetSeconds,m_ClockSync.RttSeconds,m_ClockSync.SampleCount);m_Recorder.RecordNetwork(receive,"Quest->PC",PacketType.Pose,p.Header.Sequence,bytes,false,true,obs,(long)m_PoseSequences.Missing+m_PoseAssembler.IncompleteFramesDropped,m_Main!=null?m_Main.QueueDepth:0);
            if(m_Quest!=null){var now=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WritePose(m_SendBuffer,++m_Sequence,now,m_QuestSessionId,p.UnityFrame,p.CallbackIndex,p.UpdateType,p.SuccessFlags,p.LeftTracked,p.RightTracked,p.HeadValid,p.ChunkIndex,p.ChunkCount,p.LeftRoot,p.RightRoot,p.HeadPose,p.Joints,0,p.Joints.Count,PacketType.CorrectedPose);m_Main.Send(m_SendBuffer,n,m_Quest);}
        }
        void OnMidi(MidiMessage message)
        {
            uint sequence=0;if(QuestConnected){sequence=++m_Sequence;var n=NetworkProtocolV1.WriteMidi(m_SendBuffer,sequence,message.AbsoluteTimeSeconds,m_QuestSessionId,in message,Fnv1a(message.DeviceName));var sent=m_Main.Send(m_SendBuffer,n,m_Quest);m_Recorder.RecordNetwork(message.AbsoluteTimeSeconds,"PC->Quest",PacketType.Midi,sequence,n,sent,false,default,0,m_Main.QueueDepth);}
            m_Recorder.RecordMidi(in message,sequence);m_KeyboardMirror.Apply(in message);
            if(message.EventType==MidiEventType.ControlChange&&message.ControlNumber==123)SendKeyboardSnapshot(message.AbsoluteTimeSeconds);
        }
        void OnKeyboardState(KeyboardStateChange c)=>m_Recorder.RecordKeyboard(c.AbsoluteTimeSeconds,c.NoteNumber,c.Pressed,c.Velocity,c.SustainActive);
        void SendKeyboardSnapshot(double now)
        {
            if(!QuestConnected||m_Main==null||m_Quest==null)return;
            m_LastSnapshotSent=now;m_SnapshotGeneration=unchecked(m_SnapshotGeneration+1);
            var n=NetworkProtocolV1.WriteKeyboardStateSnapshot(m_SendBuffer,++m_Sequence,now,m_QuestSessionId,m_SnapshotGeneration,m_KeyboardMirror);
            var sent=m_Main.Send(m_SendBuffer,n,m_Quest);if(sent)++m_SnapshotSent;
            m_Recorder.RecordNetwork(now,"PC->Quest",PacketType.KeyboardStateSnapshot,m_Sequence,n,sent,false,default,0,m_Main.QueueDepth);
            PublishKeyboardSnapshotDiagnostics();
        }
        void PublishKeyboardSnapshotDiagnostics()=>m_Recorder.SetKeyboardSnapshotDiagnostics(m_SnapshotSent,m_SnapshotReceived,
            m_SnapshotApplied,m_SnapshotStaleDropped,m_SnapshotSessionMismatchDropped,m_SnapshotNoteRepairs,
            m_SnapshotCc64Repairs,m_RemoteLastSnapshotGeneration);
        void SendClockRequest(double now){if(m_Quest==null)return;m_LastClockRequest=now;var n=NetworkProtocolV1.WriteClockRequest(m_SendBuffer,++m_Sequence,now,m_QuestSessionId,now);m_Main.Send(m_SendBuffer,n,m_Quest);}
        public bool StartSession()
        {
            if(!QuestConnected||m_PendingCommand!=0||SessionState==DistributedSessionState.Recording)return false;m_SessionId=Guid.NewGuid();m_PendingAction=SessionCommand.Start;m_PendingCommand=++m_CommandId;m_Retries=0;RetryControl(ResearchServices.Clock.AbsoluteSeconds);return true;
        }
        public bool StopSession(){if(m_PendingCommand!=0||SessionState!=DistributedSessionState.Recording)return false;m_PendingAction=SessionCommand.Stop;m_PendingCommand=++m_CommandId;m_Retries=0;RetryControl(ResearchServices.Clock.AbsoluteSeconds);return true;}
        void RetryControl(double now)
        {
            if(m_Quest==null||m_Retries>=m_Settings.controlRetryCount){if(m_PendingAction==SessionCommand.Stop&&SessionState==DistributedSessionState.Recording)HandleConnectionLoss("socket_error",now);m_PendingCommand=0;return;}
            var n=NetworkProtocolV1.WriteSessionControl(m_SendBuffer,++m_Sequence,now,m_SessionId,m_PendingAction,m_PendingCommand);m_Main.Send(m_SendBuffer,n,m_Quest);++m_Retries;m_NextRetry=now+m_Settings.controlRetrySeconds;
        }
        void HandleAck(SessionAckPacket ack)
        {
            if(ack.CommandId!=m_PendingCommand||ack.Command!=m_PendingAction)return;m_PendingCommand=0;if(ack.Status==SessionAckStatus.Rejected){m_SessionId=m_QuestSessionId;return;}m_QuestSessionId=ack.Header.SessionId;
            if(ack.Command==SessionCommand.Start)
            {
                if(m_Recorder.Begin(m_SessionId,QuestIpAddress,m_Midi.ConnectedDeviceName))
                {
                    m_SnapshotSent=m_SnapshotReceived=m_SnapshotApplied=m_SnapshotStaleDropped=m_SnapshotSessionMismatchDropped=m_SnapshotNoteRepairs=m_SnapshotCc64Repairs=0;m_RemoteLastSnapshotGeneration=0;PublishKeyboardSnapshotDiagnostics();
                    for(var i=0;i<m_ClockSync.History.Count;++i)m_Recorder.RecordClock(m_ClockSync.History[i],m_ClockSync.Selected.Index==m_ClockSync.History[i].Index);
                }
                SendKeyboardSnapshot(ResearchServices.Clock.AbsoluteSeconds);
            }
            else if(SessionState==DistributedSessionState.Recording)m_Recorder.End("normal_stop",m_LastQuest);
        }
        void MarkNormalPacket(double receiveTimestamp,IPEndPoint remote){m_LastQuest=receiveTimestamp;m_Quest=new IPEndPoint(remote.Address,m_Settings.questReceivePort);m_AcceptPackets=true;}
        bool IsConnectionAlive(double now)=>NetworkRunning&&m_AcceptPackets&&m_Quest!=null&&m_LastQuest>0d&&now-m_LastQuest<=m_Settings.connectionTimeoutSeconds;
        static bool IsFresh(SequenceObservation observation)=>!observation.Duplicate&&!observation.OutOfOrder;
        void HandleConnectionLoss(string reason,double now)
        {
            if(!m_AcceptPackets&&NetworkState!=DistributedNetworkState.Connected)return;NetworkState=DistributedNetworkState.TimedOut;m_TimedOutUntil=now+Math.Max(0.5d,m_Settings.heartbeatIntervalSeconds);m_Recorder.RecordNetwork(now,"Quest disconnected",PacketType.Heartbeat,0,0,false,false,default,m_PoseAssembler.IncompleteFramesDropped,m_Main!=null?m_Main.QueueDepth:0);Debug.LogWarning("Host network state: TimedOut reason="+reason+" at "+now.ToString("F6")+" s. Listening continues for a new connection.",this);ResetConnectionState(true,reason);
        }
        void SendDisconnectHeartbeat(){if(!m_AcceptPackets||m_Main==null||m_Quest==null)return;var now=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WriteHeartbeat(m_SendBuffer,++m_Sequence,now,m_QuestSessionId,0,(byte)HeartbeatState.Disconnecting,m_ClockSync.OffsetSeconds,m_ClockSync.RttSeconds);m_Main.Send(m_SendBuffer,n,m_Quest);}
        public string ExportDiagnostics(){var path=Path.Combine(Application.persistentDataPath,"PianoResearch","network_diagnostics_snapshot_"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".txt");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,$"quest={QuestIpAddress}\nconnected={QuestConnected}\nclock_offset={ClockOffset:R}\nrtt={Rtt:R}\npose_rate={PoseReceiveRate:R}\nloss={PoseLossEstimate:R}\n");return path;}
        static uint Fnv1a(string value){uint hash=2166136261;for(var i=0;i<(value?.Length??0);++i){hash^=value[i];hash*=16777619;}return hash;}
        void OnApplicationQuit(){m_ApplicationQuit=true;StopNetworkInternal("application_quit",true);}
        void OnDestroy(){if(m_Midi!=null)m_Midi.MessageReceived-=OnMidi;m_KeyboardMirror.StateChanged-=OnKeyboardState;if(!m_ApplicationQuit)StopNetworkInternal("host_stop_network",true);}
    }
}
