using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedQuestClient : MonoBehaviour
    {
        readonly QuestRawSessionLog m_Research = new QuestRawSessionLog();
        BleMidiInput m_ResearchBle;
        double m_NextResearchBleSearch;
        bool m_LocalResearchSession;
        public bool ResearchRecording => m_Research.Recording;
        public string ResearchLogError => m_Research.LastError;
        public string ResearchSessionPath => m_Research.DirectoryPath;
        public bool BeginLocalResearchSession()
        {
            if(SessionState=="Recording"||m_Research.Recording)return false;
            m_LocalResearchSession=m_Research.Begin(Guid.NewGuid(),m_Keyboard,m_Calibration?.CurrentAppliedCalibration);
            return m_LocalResearchSession;
        }
        public void EndLocalResearchSession()
        {
            if(!m_LocalResearchSession)return;
            m_Research.End("local_stop");m_LocalResearchSession=false;
        }
        void BindResearchBle()
        {
            if(m_ResearchBle!=null)return;
            var now=ResearchServices.Clock.AbsoluteSeconds;
            if(now<m_NextResearchBleSearch)return;
            m_NextResearchBleSearch=now+0.5d;
            var input=FindFirstObjectByType<BleMidiInput>();
            if(ReferenceEquals(input,m_ResearchBle))return;
            if(m_ResearchBle!=null)m_ResearchBle.ResearchEvent-=OnResearchBle;
            m_ResearchBle=input;
            if(m_ResearchBle!=null)m_ResearchBle.ResearchEvent+=OnResearchBle;
        }
        void OnResearchBle(MidiMessage message,BleMidiSession.Sample sample,double received,bool synthetic)
        {
            m_Research.RecordMidi("BLE",message,received,sample.BleTimestampMilliseconds,sample.Generation,synthetic,m_Keyboard);
        }
        readonly byte[] m_SendBuffer=new byte[NetworkProtocolV1.MaximumDatagramBytes];readonly List<PoseJointSample> m_Joints=new List<PoseJointSample>(64);
        readonly KeyboardStateSnapshotData m_KeyboardSnapshot=new KeyboardStateSnapshotData();readonly KeyboardStateSnapshotReceiver m_SnapshotReceiver=new KeyboardStateSnapshotReceiver();
        DistributedSettings m_Settings;XRHandPoseProvider m_Hands;NetworkMidiInput m_Midi;VirtualPianoKeyboard m_Keyboard;PianoCalibrationManager m_Calibration;RemoteDisplayPoseProcessor m_RemoteProcessor;UdpTransport m_Transport;IPEndPoint m_Host,m_Clock;
        uint m_Sequence,m_PoseSequence,m_TotalPosePackets,m_LastCommandId;double m_LastPoseSend,m_LastHostPacket,m_LastHeartbeatSend,m_LastHelloSend,m_LastSnapshotDiagnosticsSend,m_RateWindow,m_ConnectedAt;int m_RatePose,m_RateMidi;uint m_LastReceived,m_InstanceId;Guid m_SessionId;bool m_PreviouslyConnected,m_HandshakeComplete,m_ApplicationQuit,m_SnapshotDiagnosticsDirty;
        ulong m_SnapshotReceived,m_SnapshotApplied,m_SnapshotStaleDropped,m_SnapshotSessionMismatchDropped,m_SnapshotNoteRepairs,m_SnapshotCc64Repairs;
        const double SnapshotDiagnosticsIntervalSeconds=1d;
        readonly HashSet<uint> m_AppliedCommands=new HashSet<uint>();readonly SequenceTracker m_HostSequences=new SequenceTracker();
        public bool NetworkRunning=>m_Transport!=null;public bool PcConnected=>NetworkRunning&&m_HandshakeComplete&&m_LastHostPacket>0d&&ResearchServices.Clock.AbsoluteSeconds-m_LastHostPacket<=m_Settings.connectionTimeoutSeconds;
        public double LastHeartbeatSeconds=>m_LastHostPacket;public double RttSeconds{get;private set;}public bool ClockSynchronized{get;private set;}public float PoseSendRate{get;private set;}public float MidiReceiveRate{get;private set;}public string LastMidiEvent=>m_Midi!=null?m_Midi.LastEventText:"None";public string SessionState{get;private set;}="Idle";
        public long PoseSendFailures=>m_Transport?.SendFailures??0;public long PoseDropped{get;private set;}
        public double LastReceivedTimestamp=>m_LastHostPacket;public double LastReceivedAge=>m_LastHostPacket>0d?Math.Max(0d,ResearchServices.Clock.AbsoluteSeconds-m_LastHostPacket):-1d;public double ConnectedAt=>m_ConnectedAt;public ulong ReceivedPacketCount=>m_HostSequences.Received;public ulong MissingPacketCount=>m_HostSequences.Missing;
        public string XrLoaderName{get{var manager=XRGeneralSettings.Instance?.Manager;return manager?.activeLoader!=null?manager.activeLoader.name:"Unavailable";}}public bool XrHandsRunning=>m_Hands!=null&&m_Hands.SubsystemRunning;
        void Awake()
        {
            m_InstanceId=unchecked((uint)Guid.NewGuid().GetHashCode());
            m_Settings=GetComponent<DistributedSettings>();if(m_Settings==null)m_Settings=gameObject.AddComponent<DistributedSettings>();m_Settings.executionMode=ResearchExecutionMode.DistributedQuestClient;m_Settings.ValidateRuntime();
            m_Hands=FindFirstObjectByType<XRHandPoseProvider>();m_Midi=FindFirstObjectByType<NetworkMidiInput>();m_Keyboard=FindFirstObjectByType<VirtualPianoKeyboard>();m_Calibration=FindFirstObjectByType<PianoCalibrationManager>();
            if(m_Midi==null)m_Midi=gameObject.AddComponent<NetworkMidiInput>();if(m_Keyboard!=null)m_Midi.MessageReceived+=OnMidi;
            if(m_Hands!=null){var count=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;m_RemoteProcessor=new RemoteDisplayPoseProcessor(count);m_Hands.SetProcessor(m_RemoteProcessor);m_Hands.RawFrameUpdated+=OnRawFrame;m_Hands.ConfigureUpdateTypes(true,m_Settings.poseSendPolicy!=PoseSendPolicy.DynamicOnly);}
            ConfigurePassthroughCamera();
        }
        void Start(){StartNetwork();StartCoroutine(LogStartupAfterXrInitialization());}
        void ResetConnectionState()
        {
            m_InstanceId=unchecked((uint)Guid.NewGuid().GetHashCode());m_Sequence=0;m_PoseSequence=0;m_LastReceived=0;m_LastHostPacket=0d;
            m_LastHeartbeatSend=0d;m_LastHelloSend=0d;m_LastSnapshotDiagnosticsSend=0d;m_SessionId=Guid.Empty;m_HandshakeComplete=false;m_PreviouslyConnected=false;m_SnapshotDiagnosticsDirty=false;
            m_SnapshotReceiver.Reset();m_SnapshotReceived=m_SnapshotApplied=m_SnapshotStaleDropped=m_SnapshotSessionMismatchDropped=m_SnapshotNoteRepairs=m_SnapshotCc64Repairs=0;
            m_AppliedCommands.Clear();m_HostSequences.Reset();m_Transport?.ClearQueuedDatagrams();m_RemoteProcessor?.Reset();RttSeconds=0d;ClockSynchronized=false;
        }
        public bool StartNetwork()
        {
            if(NetworkRunning)return true;if(!IPAddress.TryParse(m_Settings.pcIpAddress,out var ip))return false;
            try{ResetConnectionState();SessionState="Idle";m_Host=new IPEndPoint(ip,m_Settings.pcReceivePort);m_Clock=new IPEndPoint(ip,m_Settings.clockSyncPort);m_Transport=new UdpTransport(m_Settings.controlQueueCapacity+m_Settings.midiQueueCapacity);m_Transport.Start(m_Settings.questReceivePort);m_RateWindow=ResearchServices.Clock.AbsoluteSeconds;SendStartupHello(m_RateWindow);return true;}catch(Exception e){Debug.LogWarning("Quest UDP start failed: "+e.Message,this);m_Transport?.Dispose();m_Transport=null;return false;}
        }
        public void StopNetwork(){if(!m_LocalResearchSession)m_Research.End("network_stopped");SendDisconnectHeartbeat();m_Transport?.Dispose();m_Transport=null;ResetConnectionState();if(m_Midi!=null)m_Midi.Disconnect();if(m_RemoteProcessor!=null){m_RemoteProcessor.Reset();m_RemoteProcessor.Connected=false;}SessionState="Disconnected";}
        void Update()
        {
            BindResearchBle();Drain();var now=ResearchServices.Clock.AbsoluteSeconds;
            if(NetworkRunning&&(!PcConnected||!m_HandshakeComplete)&&now-m_LastHelloSend>=m_Settings.heartbeatIntervalSeconds)SendStartupHello(now);
            if(NetworkRunning&&now-m_LastHeartbeatSend>=m_Settings.heartbeatIntervalSeconds){m_LastHeartbeatSend=now;var n=NetworkProtocolV1.WriteHeartbeat(m_SendBuffer,++m_Sequence,now,m_SessionId,m_LastReceived,(byte)(SessionState=="Recording"?HeartbeatState.Recording:HeartbeatState.Idle));m_Transport.Send(m_SendBuffer,n,m_Host);n=NetworkProtocolV1.WriteDiagnostic(m_SendBuffer,++m_Sequence,now,m_SessionId,m_TotalPosePackets,(uint)m_Transport.SendFailures,(uint)PoseDropped,(uint)m_Transport.QueueDepth);m_Transport.Send(m_SendBuffer,n,m_Host);}
            var connected=PcConnected;if(m_PreviouslyConnected&&!connected){HandleHostDisconnect("network_timeout");connected=false;}else if(!m_PreviouslyConnected&&connected){m_ConnectedAt=now;SessionState="Connected";Debug.Log("Quest network connected at "+now.ToString("F6")+" s to "+m_Host,this);}m_PreviouslyConnected=connected;if(m_RemoteProcessor!=null){m_RemoteProcessor.Connected=connected;m_RemoteProcessor.UseRemote=m_Settings.displaySource==PoseDisplaySource.PcDisplayPose;}
            if(connected&&(m_SnapshotDiagnosticsDirty||m_LastSnapshotDiagnosticsSend==0d||now-m_LastSnapshotDiagnosticsSend>=SnapshotDiagnosticsIntervalSeconds))SendKeyboardSnapshotDiagnostics(now);
            if(now-m_RateWindow>=1d){var dt=now-m_RateWindow;PoseSendRate=(float)(m_RatePose/dt);MidiReceiveRate=(float)(m_RateMidi/dt);m_RatePose=m_RateMidi=0;m_RateWindow=now;}
        }
        void Drain()
        {
            if(m_Transport==null)return;while(m_Transport.TryDequeue(out var d)){try
            {
                if(!NetworkProtocolV1.TryReadHeader(d.Buffer,d.Length,out var h))continue;
                if(h.Type==PacketType.StartupAck)
                {
                    if(!NetworkProtocolV1.TryReadStartupAck(d.Buffer,d.Length,out var startupAck)||startupAck.InstanceId!=m_InstanceId)continue;
                    if(!m_HandshakeComplete)m_HostSequences.Reset();var obs=m_HostSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;
                    if(!m_HandshakeComplete)m_SessionId=startupAck.Header.SessionId;m_HandshakeComplete=true;m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;continue;
                }
                if(!m_HandshakeComplete)continue;
                if(h.Type==PacketType.SessionControl)
                {
                    if(!NetworkProtocolV1.TryReadSessionControl(d.Buffer,d.Length,out var control))continue;var obs=m_HostSequences.Observe(h.Sequence);var fresh=IsFresh(obs);if(fresh){m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;}ApplyControl(control);continue;
                }
                if(h.Type==PacketType.KeyboardStateSnapshot)
                {
                    if(!NetworkProtocolV1.TryReadKeyboardStateSnapshot(d.Buffer,d.Length,m_KeyboardSnapshot))continue;
                    ++m_SnapshotReceived;m_SnapshotDiagnosticsDirty=true;
                    if(h.SessionId!=m_SessionId){++m_SnapshotSessionMismatchDropped;continue;}
                    if(!PcConnected)continue;
                    var observation=m_HostSequences.Observe(h.Sequence);
                    if(!IsFresh(observation)){++m_SnapshotStaleDropped;continue;}
                    m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;
                    m_Midi.FlushPending();
                    if(m_SnapshotReceiver.TryApply(m_KeyboardSnapshot,m_SessionId,PcConnected,m_Keyboard.State,
                        out var rejection,out var noteRepairs,out var cc64Repairs))
                    {
                        ++m_SnapshotApplied;m_SnapshotNoteRepairs+=(ulong)noteRepairs;m_SnapshotCc64Repairs+=(ulong)cc64Repairs;m_SnapshotDiagnosticsDirty=true;
                        m_Midi.SynchronizeState(m_KeyboardSnapshot.NoteBits,m_KeyboardSnapshot.Sustain);
                    }
                    else if(rejection==KeyboardSnapshotApplyRejection.SessionMismatch)++m_SnapshotSessionMismatchDropped;
                    else if(rejection==KeyboardSnapshotApplyRejection.StaleGeneration)++m_SnapshotStaleDropped;
                    continue;
                }
                if(h.SessionId!=m_SessionId)continue;
                if(h.Type==PacketType.Midi&&NetworkProtocolV1.TryReadMidi(d.Buffer,d.Length,out var midi)){var obs=m_HostSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;if(m_Midi.Enqueue(in midi))
                    {
                        ++m_RateMidi;
                        var message=new MidiMessage(midi.Header.SenderTimestamp,midi.EventIndex,"UDP",midi.EventType,midi.Channel,midi.Note,midi.Velocity,midi.Control,midi.Value);
                        m_Research.RecordMidi("UDP",message,d.ReceiveTimestamp,-1,0,false,m_Keyboard);
                    }}
                else if(h.Type==PacketType.ClockSyncRequest&&NetworkProtocolV1.TryReadClockRequest(d.Buffer,d.Length,out var req)){var obs=m_HostSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;var q1=d.ReceiveTimestamp;var q2=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WriteClockResponse(m_SendBuffer,++m_Sequence,q2,m_SessionId,req.PcT0,q1,q2);m_Transport.Send(m_SendBuffer,n,m_Clock);}
                else if(h.Type==PacketType.CorrectedPose&&NetworkProtocolV1.TryReadPose(d.Buffer,d.Length,out var corrected)){var obs=m_HostSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;m_RemoteProcessor?.Accept(corrected);}
                else if(h.Type==PacketType.Heartbeat&&NetworkProtocolV1.TryReadHeartbeat(d.Buffer,d.Length,out var heartbeat)){var obs=m_HostSequences.Observe(h.Sequence);if(!IsFresh(obs))continue;m_LastHostPacket=d.ReceiveTimestamp;m_LastReceived=h.Sequence;RttSeconds=heartbeat.Rtt;ClockSynchronized=heartbeat.Rtt>0d;if(heartbeat.State==(byte)HeartbeatState.Disconnecting)HandleHostDisconnect("host_stop_network");}
            }
            finally{m_Transport.Recycle(d);}}
        }
        void SendStartupHello(double now)
        {
            if(m_Transport==null||m_Host==null)return;m_LastHelloSend=now;var n=NetworkProtocolV1.WriteStartupHello(m_SendBuffer,++m_Sequence,now,m_SessionId,m_InstanceId,DistributedBuildInfo.Fnv1a(Application.version),DistributedBuildInfo.Fnv1a(DistributedBuildInfo.Identifier));m_Transport.Send(m_SendBuffer,n,m_Host);
        }
        void SendKeyboardSnapshotDiagnostics(double now)
        {
            if(!PcConnected||m_Transport==null||m_Host==null)return;m_LastSnapshotDiagnosticsSend=now;m_SnapshotDiagnosticsDirty=false;
            var n=NetworkProtocolV1.WriteKeyboardSnapshotDiagnostics(m_SendBuffer,++m_Sequence,now,m_SessionId,
                m_SnapshotReceived,m_SnapshotApplied,m_SnapshotStaleDropped,m_SnapshotSessionMismatchDropped,
                m_SnapshotNoteRepairs,m_SnapshotCc64Repairs,m_SnapshotReceiver.LastGeneration);
            m_Transport.Send(m_SendBuffer,n,m_Host);
        }
        IEnumerator LogStartupAfterXrInitialization()
        {
            yield return new WaitForSecondsRealtime(1f);Debug.Log("Quest startup diagnostics\nApplication.version="+Application.version+"\nBuild identifier="+DistributedBuildInfo.Identifier+"\nBuild timestamp UTC="+DistributedBuildInfo.TimestampUtc+"\nHost IP="+m_Settings.pcIpAddress+"\nPose destination port="+m_Settings.pcReceivePort+"\nMIDI receive port="+m_Settings.questReceivePort+"\nClock Sync destination port="+m_Settings.clockSyncPort+"\nINTERNET permission premise=validated by AndroidNetworkBuildGuard\nXR Loader="+XrLoaderName+"\nXRHandSubsystem.running="+XrHandsRunning,this);
        }
        static void ConfigurePassthroughCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var camera=Camera.main;if(camera!=null){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;}
#endif
        }
        void ApplyControl(SessionControlPacket p)
        {
            var duplicate=!m_AppliedCommands.Add(p.CommandId);var stale=!duplicate&&p.CommandId<m_LastCommandId;if(!duplicate&&!stale)m_LastCommandId=Math.Max(m_LastCommandId,p.CommandId);if(!duplicate&&!stale){m_SessionId=p.Header.SessionId;SessionState=p.Command==SessionCommand.Start?"Recording":"Idle";if(p.Command==SessionCommand.Start){m_SnapshotReceived=m_SnapshotApplied=m_SnapshotStaleDropped=m_SnapshotSessionMismatchDropped=m_SnapshotNoteRepairs=m_SnapshotCc64Repairs=0;m_LastSnapshotDiagnosticsSend=0d;m_SnapshotDiagnosticsDirty=true;
                try{
                    if(m_Research.Recording)m_Research.End("replaced_by_host_session");
                    m_LocalResearchSession=false;
                    if(!m_Research.Begin(m_SessionId,m_Keyboard,m_Calibration?.CurrentAppliedCalibration))
                        Debug.LogError("Quest research session failed to start: "+m_Research.LastError,this);
                }
                catch(Exception exception){Debug.LogWarning("Quest session metadata could not be saved: "+exception.Message,this);}
            }else if(!m_LocalResearchSession)m_Research.End("host_session_stop");}
            var now=ResearchServices.Clock.AbsoluteSeconds;var status=stale?SessionAckStatus.Rejected:(duplicate?SessionAckStatus.AlreadyApplied:SessionAckStatus.Accepted);var n=NetworkProtocolV1.WriteSessionAck(m_SendBuffer,++m_Sequence,now,stale?m_SessionId:p.Header.SessionId,p.Command,status,p.CommandId);m_Transport.Send(m_SendBuffer,n,m_Host);
        }
        void HandleHostDisconnect(string reason){if(m_Transport==null)return;if(!m_LocalResearchSession)m_Research.End(reason);ResetConnectionState();m_Midi?.Disconnect();if(m_RemoteProcessor!=null){m_RemoteProcessor.Reset();m_RemoteProcessor.Connected=false;}SessionState="Disconnected";Debug.LogWarning("Quest network state: Disconnected reason="+reason+". XR, passthrough and local hand visualization remain active.",this);}
        void SendDisconnectHeartbeat(){if(m_Transport==null||m_Host==null||!m_HandshakeComplete)return;var now=ResearchServices.Clock.AbsoluteSeconds;var n=NetworkProtocolV1.WriteHeartbeat(m_SendBuffer,++m_Sequence,now,m_SessionId,m_LastReceived,(byte)HeartbeatState.Disconnecting);m_Transport.Send(m_SendBuffer,n,m_Host);}
        static bool IsFresh(SequenceObservation observation)=>!observation.Duplicate&&!observation.OutOfOrder;
        void OnRawFrame(HandPoseFrame frame)
        {
            m_Research.RecordFrame(frame,m_Keyboard);
            if(m_Settings.poseSendPolicy==PoseSendPolicy.BeforeRenderOnly&&frame.UpdateType!=XRHandSubsystem.UpdateType.BeforeRender)return;
            if(!NetworkRunning)return;var now=ResearchServices.Clock.AbsoluteSeconds;var interval=1d/m_Settings.poseSendHz;if(now-m_LastPoseSend<interval){++PoseDropped;return;}m_LastPoseSend=now;m_Joints.Clear();
            if(m_Settings.sendLeftHand)AddJoints(0,frame.LeftJoints);if(m_Settings.sendRightHand)AddJoints(1,frame.RightJoints);
            var chunks=Math.Max(1,(m_Joints.Count+NetworkProtocolV1.MaximumJointsPerPosePacket-1)/NetworkProtocolV1.MaximumJointsPerPosePacket);var camera=Camera.main;var headValid=m_Settings.sendHeadPose&&camera!=null;var head=headValid?new Pose(camera.transform.position,camera.transform.rotation):Pose.identity;
            for(var chunk=0;chunk<chunks;++chunk){var start=chunk*NetworkProtocolV1.MaximumJointsPerPosePacket;var count=Math.Min(NetworkProtocolV1.MaximumJointsPerPosePacket,m_Joints.Count-start);var n=NetworkProtocolV1.WritePose(m_SendBuffer,++m_PoseSequence,frame.AbsoluteTimeSeconds,m_SessionId,frame.UnityFrame,frame.CallbackIndex,(byte)frame.UpdateType,(uint)frame.SuccessFlags,frame.LeftTracked,frame.RightTracked,headValid,(ushort)chunk,(ushort)chunks,frame.LeftRootPose,frame.RightRootPose,head,m_Joints,start,count);++m_TotalPosePackets;if(m_Transport.Send(m_SendBuffer,n,m_Host))++m_RatePose;}
        }
        void AddJoints(byte hand,HandJointPose[] source){for(var i=0;i<source.Length;++i){var j=source[i];m_Joints.Add(new PoseJointSample{Hand=hand,JointId=(ushort)j.JointId,PoseValid=j.PoseValid,TrackingState=(uint)j.TrackingState,Position=j.Pose.position,Rotation=j.Pose.rotation});}}
        void OnMidi(MidiMessage m){m_Keyboard?.ApplyMidi(in m);}
        void OnApplicationQuit(){m_Research.Dispose();m_ApplicationQuit=true;StopNetwork();}
        void OnDestroy(){m_Research.Dispose();if(m_ResearchBle!=null)m_ResearchBle.ResearchEvent-=OnResearchBle;if(m_Hands!=null)m_Hands.RawFrameUpdated-=OnRawFrame;if(m_Midi!=null)m_Midi.MessageReceived-=OnMidi;if(!m_ApplicationQuit)StopNetwork();}
    }
}
