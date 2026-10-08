using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Distributed
{
    /// <summary>Additional Quest-side research files. Never modifies provider frames or legacy CSVs.</summary>
    public sealed class QuestRawSessionLog : IDisposable
    {
        [Serializable] public sealed class TransformSnapshot
        {
            public string name;
            public Vector3 position, localScale;
            public Quaternion rotation;
            // Row-major, includes every ancestor (and possible shear); TRS alone is insufficient.
            public float[] local_to_world;
            public static TransformSnapshot Capture(Transform t)
            {
                if (t == null) throw new InvalidOperationException("Research transform is missing.");
                var matrix=t.localToWorldMatrix;
                foreach(var v in Elements(matrix))if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidOperationException("Non-finite research transform.");
                if(Mathf.Abs(matrix.determinant)<1e-12f)throw new InvalidOperationException("Singular research transform.");
                return new TransformSnapshot { name=t.name, position=t.position, rotation=t.rotation,
                    localScale=t.localScale, local_to_world=Elements(t.localToWorldMatrix) };
            }
        }
        [Serializable] public sealed class KeyGeometry
        {
            public int note;
            public Vector3 min, max;
        }
        [Serializable] public sealed class Snapshot
        {
            public long snapshot_id;
            public double quest_timestamp_sec;
            public TransformSnapshot tracking_origin, keyboard_root, keyboard_geometry;
            public KeyGeometry[] keys;
        }
        [Serializable] sealed class Timing
        {
            public string hand_clock_definition="Quest Stopwatch absolute seconds at XR Hands callback acquisition; shared by all joints in callback; sensor timestamp unavailable";
            public string midi_clock_definition="BLE: Android System.nanoTime mapped to same Quest Stopwatch; UDP: Quest socket receive Stopwatch plus separate PC sender clock";
            public string timestamp_unit="seconds (BLE sender timestamp: wrapped 13-bit milliseconds)";
            public string clock_type="monotonic; start_utc is separate wall-clock anchor, not a synchronization estimate";
            public string hand_update_policy="Dynamic only; no BeforeRender duplicates";
            public string udp_pc_clock="independent PC Stopwatch; never directly subtract from Quest timestamps";
            public string midi_semantics="NoteOn is not assumed to be physical contact time; candidate fingers are not assignments";
        }
        readonly struct Entry
        {
            public readonly int File; public readonly string Text;
            public Entry(int file,string text){File=file;Text=text;}
        }
        BlockingCollection<Entry> queue;
        Thread writer;
        volatile string error;
        Snapshot snapshot;
        Guid session;
        long overflows;
        public bool Recording { get; private set; }
        public string LastError => error ?? "";
        public string DirectoryPath { get; private set; }
        static readonly CultureInfo F=CultureInfo.InvariantCulture;
        public static float[] Elements(Matrix4x4 matrix)
        {
            var values=new float[16];
            for(var r=0;r<4;r++)for(var c=0;c<4;c++)values[r*4+c]=matrix[r,c];
            return values;
        }
        public static Vector3 ToWorld(Matrix4x4 trackingToWorld,Vector3 raw)=>trackingToWorld.MultiplyPoint3x4(raw);
        public static Vector3 ToKeyboard(Matrix4x4 keyboardToWorld,Vector3 world)=>keyboardToWorld.inverse.MultiplyPoint3x4(world);
        public bool Begin(Guid id,VirtualPianoKeyboard keyboard,PianoCalibrationData calibration)
        {
            if(Recording)return false;
            if(writer!=null&&writer.IsAlive)return false;
            error=null;overflows=0;session=id;snapshot=null;
            var opened=new System.Collections.Generic.List<StreamWriter>();
            try
            {
                DirectoryPath=Path.Combine(Application.persistentDataPath,"PianoResearch","DistributedSessions",id.ToString("N")+"_Quest");
                if(Directory.Exists(DirectoryPath))throw new IOException("Refusing to overwrite existing research session.");
                snapshot=Capture(keyboard,ResearchServices.Clock.AbsoluteSeconds,0);
                DistributedSessionRecorder.WriteQuestMetadata(id,calibration,keyboard);
                DirectoryPath=Path.Combine(Application.persistentDataPath,"PianoResearch","DistributedSessions",id.ToString("N")+"_Quest");
                var path=Path.Combine(DirectoryPath,"session_metadata.json");
                var json=File.ReadAllText(path);var end=json.LastIndexOf('}');
                var extra=",\n  \"research_log_version\": 1,\n  \"tracking_origin_snapshot\": "+JsonUtility.ToJson(snapshot.tracking_origin)+
                    ",\n  \"keyboard_root_snapshot\": "+JsonUtility.ToJson(snapshot.keyboard_root)+
                    ",\n  \"timing\": "+JsonUtility.ToJson(new Timing())+
                    ",\n  \"device_model\": "+Quote(SystemInfo.deviceModel)+
                    ",\n  \"git_branch\": "+Quote(DistributedBuildInfo.Metadata.git_branch)+
                    ",\n  \"transform_snapshot_file\": \"transform_snapshots.jsonl\",\n  \"transform_snapshot_policy\": \"initial and exact matrix/mode changes at each logged Dynamic callback and MIDI observation; every hand/MIDI row references snapshot_id\",\n  \"finger_pad_model\": {\"pad_offset\": [0,0,0], \"contact_point_assumption\": false}";
                File.WriteAllText(path,json.Substring(0,end).TrimEnd()+extra+"\n}\n");
                queue?.Dispose();queue=new BlockingCollection<Entry>(4096);
                // Create files before accepting events; fail Begin if storage is unavailable.
                var files=new[]{OpenTracked(opened,"quest_hand_raw.csv","session_id,quest_timestamp_sec,unity_frame,callback_index,update_type,hand,hand_tracked,joint_id,joint_name,pose_valid,tracking_state,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w,snapshot_id,success_flags"),
                    OpenTracked(opened,"quest_midi_research.csv","session_id,source,quest_receive_timestamp_sec,sender_timestamp_sec,ble_timestamp_13bit_ms,connection_generation,event_index,event_type,channel,note,velocity,control,value,synthetic,snapshot_id,quest_observed_timestamp_sec"),
                    OpenTracked(opened,"transform_snapshots.jsonl",null)};
                writer=new Thread(()=>WriteLoop(files)){IsBackground=true,Name="Quest raw research writer"};writer.Start();
                Recording=true;Enqueue(2,JsonUtility.ToJson(snapshot));return true;
            }
            catch(Exception e){foreach(var f in opened)f.Dispose();error=e.ToString();Debug.LogError("Research recording failed: "+error);return false;}
        }
        StreamWriter OpenTracked(System.Collections.Generic.List<StreamWriter> opened,string name,string header)
        {
            var w=Open(name,header);opened.Add(w);return w;
        }
        StreamWriter Open(string name,string header)
        {
            var w=new StreamWriter(Path.Combine(DirectoryPath,name),false,new UTF8Encoding(false),65536);
            if(header!=null)w.WriteLine(header);return w;
        }
        static Snapshot Capture(VirtualPianoKeyboard keyboard,double time,long id)
        {
            if(keyboard==null||keyboard.KeyboardRoot==null||keyboard.KeyboardGeometry==null)throw new InvalidOperationException("Keyboard unavailable.");
            var keys=new KeyGeometry[keyboard.KeyCount];
            for(var note=keyboard.MinNote;note<=keyboard.MaxNote;note++)
            {
                if(!keyboard.TryGetKey(note,out var key))throw new InvalidOperationException("Key unavailable.");
                var t=key.Renderer.transform;var size=t.localScale;
                var matrix=keyboard.KeyboardRoot.worldToLocalMatrix*keyboard.KeyboardGeometry.localToWorldMatrix;
                var min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
                var max=new Vector3(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
                for(var i=0;i<8;i++)
                {
                    var corner=key.RestLocalPosition+Vector3.Scale(size,new Vector3((i&1)==0?-.5f:.5f,(i&2)==0?-.5f:.5f,(i&4)==0?-.5f:.5f));
                    var p=matrix.MultiplyPoint3x4(corner);min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                }
                keys[note-keyboard.MinNote]=new KeyGeometry{note=note,min=min,max=max};
            }
            return new Snapshot{snapshot_id=id,quest_timestamp_sec=time,
                tracking_origin=TransformSnapshot.Capture(ResearchServices.TrackingOrigin),keyboard_root=TransformSnapshot.Capture(keyboard.KeyboardRoot),
                keyboard_geometry=TransformSnapshot.Capture(keyboard.KeyboardGeometry),keys=keys};
        }
        static bool Same(float[] a,Matrix4x4 b)
        {
            for(var r=0;r<4;r++)for(var c=0;c<4;c++)if(a[r*4+c]!=b[r,c])return false;
            return true;
        }
        bool EnsureSnapshot(VirtualPianoKeyboard keyboard,double time)
        {
            if(!Recording||error!=null)return false;
            try
            {
                if(ResearchServices.TrackingOrigin==null||keyboard==null||keyboard.KeyboardRoot==null||keyboard.KeyboardGeometry==null)
                    throw new InvalidOperationException("Transform lost during recording.");
                if(!Same(snapshot.tracking_origin.local_to_world,ResearchServices.TrackingOrigin.localToWorldMatrix)||
                    !Same(snapshot.keyboard_root.local_to_world,keyboard.KeyboardRoot.localToWorldMatrix)||
                    !Same(snapshot.keyboard_geometry.local_to_world,keyboard.KeyboardGeometry.localToWorldMatrix)||snapshot.keys.Length!=keyboard.KeyCount)
                {
                    snapshot=Capture(keyboard,time,snapshot.snapshot_id+1);Enqueue(2,JsonUtility.ToJson(snapshot));
                }
                return true;
            }
            catch(Exception e){error=e.ToString();return false;}
        }
        public void RecordFrame(HandPoseFrame frame,VirtualPianoKeyboard keyboard)
        {
            if(frame.UpdateType!=XRHandSubsystem.UpdateType.Dynamic||!EnsureSnapshot(keyboard,frame.AbsoluteTimeSeconds))return;
            var b=new StringBuilder(16000);
            AddHand(b,frame,frame.LeftJoints,"Left",frame.LeftTracked);
            AddHand(b,frame,frame.RightJoints,"Right",frame.RightTracked);
            Enqueue(0,b.ToString().TrimEnd('\n'));
        }
        void AddHand(StringBuilder b,HandPoseFrame frame,HandJointPose[] joints,string hand,bool tracked)
        {
            foreach(var j in joints)
            {
                var p=j.Pose.position;var q=j.Pose.rotation;
                b.Append(string.Join(",",session,N(frame.AbsoluteTimeSeconds),frame.UnityFrame,frame.CallbackIndex,frame.UpdateType,hand,tracked,(int)j.JointId,j.JointId,j.PoseValid,(uint)j.TrackingState,N(p.x),N(p.y),N(p.z),N(q.x),N(q.y),N(q.z),N(q.w),snapshot.snapshot_id,(uint)frame.SuccessFlags)).Append('\n');
            }
        }
        public void RecordMidi(string source,MidiMessage m,double received,int bleTimestamp,long generation,bool synthetic,VirtualPianoKeyboard keyboard)
        {
            // Capture transform at Unity observation time, while preserving transport receive time independently.
            var observed=ResearchServices.Clock.AbsoluteSeconds;
            if(!EnsureSnapshot(keyboard,observed))return;
            var type=m.EventType==MidiEventType.NoteOn&&m.Velocity==0?MidiEventType.NoteOff:m.EventType;
            Enqueue(1,string.Join(",",session,source,N(received),N(m.AbsoluteTimeSeconds),bleTimestamp<0?"":bleTimestamp.ToString(F),generation,m.EventIndex,type,m.Channel,m.NoteNumber,m.Velocity,m.ControlNumber,m.ControlValue,synthetic,snapshot.snapshot_id,N(observed)));
        }
        void Enqueue(int file,string text)
        {
            if(!queue.TryAdd(new Entry(file,text))){Interlocked.Increment(ref overflows);error="Research queue overflow; session incomplete";}
        }
        void WriteLoop(StreamWriter[] files)
        {
            try
            {
                var count=0;
                foreach(var e in queue.GetConsumingEnumerable())
                {files[e.File].WriteLine(e.Text);if(++count%64==0)foreach(var f in files)f.Flush();}
            }
            catch(Exception e){error=e.ToString();}
            finally{foreach(var f in files)try{f.Dispose();}catch(Exception e){error=e.ToString();}}
        }
        public void End(string reason)
        {
            if(!Recording)return;Recording=false;queue.CompleteAdding();
            var joined=writer.Join(5000);if(!joined)error="Research writer did not close within 5 seconds";
            try { File.WriteAllText(Path.Combine(DirectoryPath,"research_recording_summary.json"),"{\"reason\":"+Quote(reason)+",\"writer_closed\":"+(joined?"true":"false")+",\"queue_overflows\":"+overflows+",\"error\":"+Quote(error)+"}"); }
            catch(Exception e){error=e.ToString();}
            if(error!=null)Debug.LogError("Research log incomplete: "+error);
        }
        public void Dispose()=>End("application_or_scene_end");
        static string N(float value)=>value.ToString("R",F);
        static string N(double value)=>value.ToString("R",F);
        static string Quote(string value)=>value==null?"null":"\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
    }
}
