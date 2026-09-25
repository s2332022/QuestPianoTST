using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    public enum DistributedSessionState { Idle, Starting, Recording, Finalizing, Stopping, Completed, Aborted, Faulted }
    enum LogFileKind { Hand,Head,Midi,Keyboard,Clock,Network }
    readonly struct LogLine { public readonly LogFileKind Kind;public readonly string Text;public LogLine(LogFileKind kind,string text){Kind=kind;Text=text;} }

    [DisallowMultipleComponent]
    public sealed class DistributedSessionRecorder : MonoBehaviour
    {
        BlockingCollection<LogLine> m_Queue;Thread m_Thread;ManualResetEventSlim m_Ready;Guid m_SessionId;double m_Start;long m_Lines,m_Overflows;string m_Error=string.Empty;volatile bool m_WritersClosed;
        ulong m_SnapshotSent,m_SnapshotReceived,m_SnapshotApplied,m_SnapshotStaleDropped,m_SnapshotSessionMismatchDropped,m_SnapshotNoteRepairs,m_SnapshotCc64Repairs;uint m_SnapshotLastGeneration;
        public DistributedSessionState State{get;private set;}=DistributedSessionState.Idle;public Guid SessionId=>m_SessionId;public string SessionPath{get;private set;}=string.Empty;
        public double RecordingSeconds=>State==DistributedSessionState.Recording?ResearchServices.Clock.AbsoluteSeconds-m_Start:0d;public long QueueOverflows=>Interlocked.Read(ref m_Overflows);public long RecordedLineCount=>Interlocked.Read(ref m_Lines);public bool WritersClosed=>m_WritersClosed;public string LastError=>m_Error;
        public bool Begin(Guid sessionId,string questIp,string midiDevice)
        {
            if(State==DistributedSessionState.Recording||State==DistributedSessionState.Starting||State==DistributedSessionState.Finalizing||State==DistributedSessionState.Stopping)return false;
            DisposeCompletedResources();State=DistributedSessionState.Starting;m_SessionId=sessionId;m_Start=ResearchServices.Clock.AbsoluteSeconds;m_Lines=0;m_Overflows=0;m_Error=string.Empty;m_WritersClosed=false;
            m_SnapshotSent=m_SnapshotReceived=m_SnapshotApplied=m_SnapshotStaleDropped=m_SnapshotSessionMismatchDropped=m_SnapshotNoteRepairs=m_SnapshotCc64Repairs=0;m_SnapshotLastGeneration=0;
            SessionPath=Path.Combine(Application.persistentDataPath,"PianoResearch","DistributedSessions",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"_"+sessionId.ToString("N"));Directory.CreateDirectory(SessionPath);
            File.WriteAllText(Path.Combine(SessionPath,"session_metadata.json"),"{\n  \"protocol_version\": 1,\n  \"session_id\": \""+sessionId+"\",\n  \"start_utc\": \""+DateTime.UtcNow.ToString("O")+"\",\n  \"quest_ip\": \""+Json(questIp)+"\",\n  \"midi_device\": \""+Json(midiDevice)+"\",\n  \"application_version\": \""+Json(Application.version)+"\",\n  \"build_guid\": \""+Json(Application.buildGUID)+"\",\n  \"build_timestamp_utc\": \""+Json(DistributedBuildInfo.TimestampUtc)+"\",\n  \"build_identifier\": \""+Json(DistributedBuildInfo.Identifier)+"\",\n  \"scene\": \""+Json(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path)+"\",\n  \"execution_mode\": \"DistributedPcHost\"\n}\n");
            m_Queue=new BlockingCollection<LogLine>(65536);m_Ready=new ManualResetEventSlim(false);m_Thread=new Thread(WriterLoop){IsBackground=true,Name="Distributed session CSV writer"};m_Thread.Start();
            if(!m_Ready.Wait(3000)||!string.IsNullOrEmpty(m_Error)){State=DistributedSessionState.Faulted;End("socket_error");return false;}ResearchServices.Clock.StartSession();State=DistributedSessionState.Recording;return true;
        }
        public void RecordPose(PosePacket p,double receivePc,double estimatedPc,double offset,double rtt,int samples)
        {
            if(State!=DistributedSessionState.Recording)return;var f=CultureInfo.InvariantCulture;
            for(var i=0;i<p.Joints.Count;++i){var j=p.Joints[i];Enqueue(LogFileKind.Hand,string.Join(",",m_SessionId,p.Header.SenderTimestamp.ToString("R",f),estimatedPc.ToString("R",f),receivePc.ToString("R",f),p.Header.Sequence,p.UnityFrame,p.CallbackIndex,p.UpdateType,j.Hand==0?"Left":"Right",j.Hand==0?p.LeftTracked:p.RightTracked,j.JointId,j.PoseValid,j.TrackingState,j.Position.x.ToString("R",f),j.Position.y.ToString("R",f),j.Position.z.ToString("R",f),j.Rotation.x.ToString("R",f),j.Rotation.y.ToString("R",f),j.Rotation.z.ToString("R",f),j.Rotation.w.ToString("R",f)));}
            if(p.HeadValid){var v=p.HeadPose.position;var q=p.HeadPose.rotation;Enqueue(LogFileKind.Head,string.Join(",",m_SessionId,p.Header.SenderTimestamp.ToString("R",f),estimatedPc.ToString("R",f),receivePc.ToString("R",f),p.Header.Sequence,p.UnityFrame,v.x.ToString("R",f),v.y.ToString("R",f),v.z.ToString("R",f),q.x.ToString("R",f),q.y.ToString("R",f),q.z.ToString("R",f),q.w.ToString("R",f)));}
        }
        public void RecordMidi(in MidiMessage m,uint sequence){if(State!=DistributedSessionState.Recording)return;var f=CultureInfo.InvariantCulture;Enqueue(LogFileKind.Midi,string.Join(",",m_SessionId,m.AbsoluteTimeSeconds.ToString("R",f),m.EventIndex,Csv(m.DeviceName),m.EventType,m.Channel,m.NoteNumber,m.Velocity,m.ControlNumber,m.ControlValue,sequence));}
        public void RecordKeyboard(double pcTime,int note,bool pressed,int velocity,bool sustain){if(State!=DistributedSessionState.Recording)return;Enqueue(LogFileKind.Keyboard,string.Join(",",m_SessionId,pcTime.ToString("R",CultureInfo.InvariantCulture),note,pressed,velocity,sustain));}
        public void RecordClock(ClockSyncSample s,bool selected){if(State!=DistributedSessionState.Recording)return;var f=CultureInfo.InvariantCulture;Enqueue(LogFileKind.Clock,string.Join(",",s.PcT0.ToString("R",f),s.QuestQ1.ToString("R",f),s.QuestQ2.ToString("R",f),s.PcT3.ToString("R",f),s.Rtt.ToString("R",f),s.Offset.ToString("R",f),selected,s.Index));}
        public void RecordNetwork(double now,string direction,PacketType type,uint sequence,int bytes,bool send,bool receive,SequenceObservation obs,long dropped,int queue){if(State!=DistributedSessionState.Recording)return;Enqueue(LogFileKind.Network,string.Join(",",now.ToString("R",CultureInfo.InvariantCulture),direction,type,sequence,bytes,send,receive,obs.OutOfOrder,obs.Duplicate,dropped,queue));}
        public void SetKeyboardSnapshotDiagnostics(ulong sent,ulong received,ulong applied,ulong staleDropped,
            ulong sessionMismatchDropped,ulong noteRepairs,ulong cc64Repairs,uint lastGeneration)
        {
            m_SnapshotSent=sent;m_SnapshotReceived=received;m_SnapshotApplied=applied;m_SnapshotStaleDropped=staleDropped;
            m_SnapshotSessionMismatchDropped=sessionMismatchDropped;m_SnapshotNoteRepairs=noteRepairs;
            m_SnapshotCc64Repairs=cc64Repairs;m_SnapshotLastGeneration=lastGeneration;
        }
        public void End(string reason,double lastNormalPacketTimestamp=-1d)
        {
            if(State==DistributedSessionState.Finalizing||State==DistributedSessionState.Stopping||State==DistributedSessionState.Completed||State==DistributedSessionState.Aborted)return;
            if(State!=DistributedSessionState.Recording&&State!=DistributedSessionState.Faulted&&State!=DistributedSessionState.Starting)return;
            var aborted=IsAbortedReason(reason);State=DistributedSessionState.Finalizing;try{m_Queue?.CompleteAdding();}catch(InvalidOperationException){}
            var writerJoined=m_Thread==null||!m_Thread.IsAlive||m_Thread.Join(5000);ResearchServices.Clock.StopSession();
            try
            {
                var timestamp=lastNormalPacketTimestamp>=0d?lastNormalPacketTimestamp.ToString("R",CultureInfo.InvariantCulture):"null";
                File.WriteAllText(Path.Combine(SessionPath,"session_summary.json"),"{\n  \"session_id\": \""+m_SessionId+"\",\n  \"end_utc\": \""+DateTime.UtcNow.ToString("O")+"\",\n  \"reason\": \""+Json(reason)+"\",\n  \"final_state\": \""+(aborted?"Aborted":"Completed")+"\",\n  \"last_normal_packet_timestamp_sec\": "+timestamp+",\n  \"recorded_rows\": "+RecordedLineCount+",\n  \"log_lines\": "+RecordedLineCount+",\n  \"queue_overflows\": "+QueueOverflows+",\n  \"keyboard_snapshot_sent\": "+m_SnapshotSent.ToString(CultureInfo.InvariantCulture)+",\n  \"keyboard_snapshot_received\": "+m_SnapshotReceived.ToString(CultureInfo.InvariantCulture)+",\n  \"keyboard_snapshot_applied\": "+m_SnapshotApplied.ToString(CultureInfo.InvariantCulture)+",\n  \"keyboard_snapshot_stale_dropped\": "+m_SnapshotStaleDropped.ToString(CultureInfo.InvariantCulture)+",\n  \"keyboard_snapshot_session_mismatch_dropped\": "+m_SnapshotSessionMismatchDropped.ToString(CultureInfo.InvariantCulture)+",\n  \"keyboard_snapshot_note_repairs\": "+m_SnapshotNoteRepairs.ToString(CultureInfo.InvariantCulture)+",\n  \"keyboard_snapshot_cc64_repairs\": "+m_SnapshotCc64Repairs.ToString(CultureInfo.InvariantCulture)+",\n  \"keyboard_snapshot_last_generation\": "+m_SnapshotLastGeneration.ToString(CultureInfo.InvariantCulture)+",\n  \"writer_flush_close_completed\": "+(writerJoined&&m_WritersClosed?"true":"false")+"\n}\n");
                State=aborted?DistributedSessionState.Aborted:DistributedSessionState.Completed;
            }
            catch(Exception e){m_Error=e.ToString();State=DistributedSessionState.Faulted;}
        }
        void OnApplicationQuit(){if(State==DistributedSessionState.Recording||State==DistributedSessionState.Faulted||State==DistributedSessionState.Starting)End("application_quit");}
        void OnDestroy(){if(State==DistributedSessionState.Recording||State==DistributedSessionState.Faulted||State==DistributedSessionState.Starting)End("application_quit");m_Ready?.Dispose();m_Queue?.Dispose();}
        void Enqueue(LogFileKind kind,string text){try{if(m_Queue==null||!m_Queue.TryAdd(new LogLine(kind,text))){Interlocked.Increment(ref m_Overflows);m_Error="CRITICAL: session log queue overflow";}}catch(InvalidOperationException){Interlocked.Increment(ref m_Overflows);m_Error="CRITICAL: session log queue was closed";}}
        void WriterLoop()
        {
            var writers=new Dictionary<LogFileKind,StreamWriter>();var writerLoopCompleted=false;var closeSucceeded=true;try
            {
                writers.Add(LogFileKind.Hand,Open("quest_hand_joints.csv","session_id,quest_timestamp_sec,estimated_pc_timestamp_sec,pc_receive_timestamp_sec,sequence_number,unity_frame,callback_index,update_type,hand,hand_tracked,joint_id,pose_valid,tracking_state,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w"));
                writers.Add(LogFileKind.Head,Open("quest_head_pose.csv","session_id,quest_timestamp_sec,estimated_pc_timestamp_sec,pc_receive_timestamp_sec,sequence_number,unity_frame,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w"));
                writers.Add(LogFileKind.Midi,Open("pc_midi_events.csv","session_id,pc_timestamp_sec,event_index,device_name,event_type,channel,note_number,velocity,control_number,control_value,network_sequence_number"));
                writers.Add(LogFileKind.Keyboard,Open("quest_keyboard_state.csv","session_id,estimated_pc_timestamp_sec,note_number,pressed,velocity,sustain_active"));
                writers.Add(LogFileKind.Clock,Open("clock_sync.csv","pc_t0_sec,quest_q1_sec,quest_q2_sec,pc_t3_sec,rtt_sec,offset_sec,selected,sample_index"));
                writers.Add(LogFileKind.Network,Open("network_diagnostics.csv","pc_timestamp_sec,direction,packet_type,sequence_number,payload_bytes,send_success,receive_success,out_of_order,duplicate,dropped_estimate,queue_depth"));m_Ready.Set();
                foreach(var line in m_Queue.GetConsumingEnumerable()){writers[line.Kind].WriteLine(line.Text);Interlocked.Increment(ref m_Lines);if((m_Lines&255)==0)foreach(var w in writers.Values)w.Flush();}
                writerLoopCompleted=true;
            }catch(Exception e){m_Error=e.ToString();m_Ready.Set();writerLoopCompleted=false;}finally{foreach(var w in writers.Values){try{w.Flush();}catch(Exception e){m_Error=e.ToString();closeSucceeded=false;}try{w.Dispose();}catch(Exception e){m_Error=e.ToString();closeSucceeded=false;}}m_WritersClosed=writerLoopCompleted&&closeSucceeded;}
        }
        StreamWriter Open(string name,string header){var w=new StreamWriter(Path.Combine(SessionPath,name),false,new UTF8Encoding(false),65536);w.WriteLine(header);return w;}
        void DisposeCompletedResources(){if(m_Thread!=null&&m_Thread.IsAlive)return;m_Ready?.Dispose();m_Queue?.Dispose();m_Ready=null;m_Queue=null;m_Thread=null;}
        static bool IsAbortedReason(string reason){return string.Equals(reason,"quest_disconnect",StringComparison.Ordinal)||string.Equals(reason,"host_stop_network",StringComparison.Ordinal)||string.Equals(reason,"network_timeout",StringComparison.Ordinal)||string.Equals(reason,"application_quit",StringComparison.Ordinal)||string.Equals(reason,"socket_error",StringComparison.Ordinal)||string.Equals(reason,"session_stop_ack_timeout",StringComparison.Ordinal);}
        static string Csv(string s)=>"\""+(s??string.Empty).Replace("\"","\"\"")+"\"";static string Json(string s)=>(s??string.Empty).Replace("\\","\\\\").Replace("\"","\\\"");
    }
}
