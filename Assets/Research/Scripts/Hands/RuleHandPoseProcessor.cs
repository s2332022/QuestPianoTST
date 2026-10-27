using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    public struct RuleFingerContact
    {
        public int note, channel;
        public long generation, eventIndex, onsetEventIndex;
        public string source, reason;
        public double received, observed, onset, phaseStart;
        public TipCorrectionState state;
        public float gain, releaseGain;
        public bool targetCapped;
    }

    /// <summary>Causal Dynamic-update processor. MIDI attribution and display pose are separate from Raw.</summary>
    public sealed class RuleHandPoseProcessor : IHandPoseProcessor
    {
        readonly IdentityHandPoseProcessor identity=new IdentityHandPoseProcessor();
        // Occupancy outlives contact animation, watchdog resets and safety stops.
        readonly RuleFingerContact[] owners=new RuleFingerContact[5];
        static readonly string[] fingerNamesJapanese={"親指","人差し指","中指","薬指","小指"};
        public string AssignmentRejectionJapanese { get; private set; }="";
        public int AssignedFinger(int note)
        {for(var f=0;f<5;f++)if(owners[f].source!=null && owners[f].note==note)return f;return -1;}
        public int AssignedNote(int finger)=>owners[finger].source==null?-1:owners[finger].note;
        public static string FingerJapanese(int finger)=>fingerNamesJapanese[finger];
        public string AssignmentSummaryJapanese
        {
            get
            {
                var text="";
                for(var f=0;f<5;f++)if(owners[f].source!=null)
                    text+=(text.Length==0?"":" / ")+$"{owners[f].note}→{FingerJapanese(f)}";
                return text.Length==0?"現在の鍵→指：なし":"現在の鍵→指："+text;
            }
        }
        bool Reject(string reason,string japanese)
        {Reason=reason;AssignmentRejectionJapanese=japanese;return false;}
        public bool TrySetManualAssignment(int note,int finger)
        {
            if(note<0 || note>127 || finger < -1 || finger>4 || !settings.IsValid)
                return Reject("invalid_finger","割当拒否：鍵または指の指定が不正です。");
            var old=settings.noteToFinger[note];
            if(old==finger)return true;
            // Held but unassigned notes also cannot acquire a finger mid-note.
            for(var s=0;s<2;s++)for(var c=0;c<16;c++)if(held[s,c,note])
                return Reject("assignment_held","割当拒否：打鍵中の鍵はNote Offまで変更できません。");
            if(old>=0 && finger>=0)
                return Reject("key_assignment_conflict","割当拒否：この鍵には既に指が設定されています。解除してから設定してください。");
            settings.noteToFinger[note]=finger;return true;
        }
        void ClearVisual(int f,string reason)
        {
            Contacts[f]=new RuleFingerContact{note=-1,reason=reason};tipOffsets[f]=Vector3.zero;previousReleaseFactor[f]=1;
            for(var j=0;j<4;j++)lastDisplacements[f,j]=0;
            for(var j=0;j<3;j++)lastRotations[f,j]=releaseRotations[f,j]=Quaternion.identity;
        }
        readonly bool[,,] held=new bool[2,16,128];
        readonly long[] generations={-1,-1};
        readonly double[] lastEvents={double.NegativeInfinity,double.NegativeInfinity};
        readonly Vector3[] targets=new Vector3[5], tipOffsets=new Vector3[5];
        readonly float[] weights=new float[5], previousReleaseFactor=new float[5];
        readonly Quaternion[,] lastRotations=new Quaternion[5,3], releaseRotations=new Quaternion[5,3];
        readonly float[,] lastDisplacements=new float[5,4];
        readonly MidiTipCorrectionSettings common;
        readonly RuleHandPoseSettings settings;
        readonly VirtualPianoKeyboard keyboard;
        readonly Func<bool> calibrated;
        readonly Func<Transform> origin;
        readonly RuleHandPoseSolver solver=new RuleHandPoseSolver();
        public readonly RuleFingerContact[] Contacts=new RuleFingerContact[5];
        public RuleHandPoseSolver Solver=>solver;
        public readonly RuleKeyboardGeometry Geometry=new RuleKeyboardGeometry();
        public float ProcessingMilliseconds { get; private set; }
        public bool SolvedThisFrame { get; private set; }
        public string Reason { get; private set; }="no_note";
        public int HeldCount { get; private set; }
        bool blocked,lastLeft,lastEnabled,lastManualMap,hasTransform,hasPose;
        CorrectionFinger lastFinger;
        Matrix4x4 lastTransform;
        double lastPoseTime=double.NegativeInfinity;
        public RuleHandPoseProcessor(MidiTipCorrectionSettings common,RuleHandPoseSettings settings,VirtualPianoKeyboard keyboard,Func<bool> calibrated,Func<Transform> origin=null)
        {
            this.common=common;this.settings=settings;this.keyboard=keyboard;this.calibrated=calibrated;
            this.origin=origin??(()=>ResearchServices.TrackingOrigin);solver.Geometry=Geometry;Reset("no_note");
        }
        public void Reset(string reason)
        {
            Reason=reason;blocked=HeldCount>0;hasPose=false;hasTransform=false;lastPoseTime=double.NegativeInfinity;
            lastLeft=common.leftHand;lastFinger=common.finger;lastEnabled=common.enabled;lastManualMap=settings.manualNoteMap;
            for(var f=0;f<5;f++)
            {
                Contacts[f]=new RuleFingerContact{note=-1,reason=reason};tipOffsets[f]=Vector3.zero;previousReleaseFactor[f]=1;
                for(var j=0;j<4;j++)lastDisplacements[f,j]=0;
                for(var j=0;j<3;j++)lastRotations[f,j]=releaseRotations[f,j]=Quaternion.identity;
            }
        }
        public void ObserveMidi(string source,MidiMessage message,double received,double observed,long generation=0,bool synthetic=false)
        {
            var s=source=="BLE"?0:source=="UDP"?1:-1;
            if(s<0 || !double.IsFinite(received) || !double.IsFinite(observed) || generation<generations[s] || observed<lastEvents[s])return;
            lastEvents[s]=observed;
            if(generation>generations[s]){DisconnectSource(source,observed,"source_generation_changed");generations[s]=generation;}
            if(synthetic){DisconnectSource(source,observed);return;}
            var channel=message.Channel-1;if(channel<0 || channel>15)return;
            var type=message.EventType==MidiEventType.NoteOn && message.Velocity==0?MidiEventType.NoteOff:message.EventType;
            if(type==MidiEventType.ControlChange && (message.ControlNumber==120 || message.ControlNumber==123))
            {
                for(var n=0;n<128;n++)if(held[s,channel,n]){held[s,channel,n]=false;HeldCount--;}
                for(var f=0;f<5;f++)
                {
                    if(owners[f].source==source && owners[f].channel==message.Channel)owners[f]=default;
                    if(Contacts[f].source==source && Contacts[f].channel==message.Channel)Release(f,observed,"all_notes_off");
                }
                if(HeldCount==0)blocked=false;return;
            }
            if(type!=MidiEventType.NoteOn && type!=MidiEventType.NoteOff)return;
            var note=message.NoteNumber;if(note<0 || note>127)return;
            var was=held[s,channel,note];var on=type==MidiEventType.NoteOn;
            if(on && HeldCount==0 && (common.leftHand!=lastLeft || common.finger!=lastFinger || common.enabled!=lastEnabled || settings.manualNoteMap!=lastManualMap))
                Reset("configuration_changed");
            if(on!=was){held[s,channel,note]=on;HeldCount+=on?1:-1;}
            if(on)
            {
                if(!settings.IsValid){Reset("invalid_settings");return;}
                if(was)
                {
                    Reject("ambiguous_notes","補正停止：同一ノートのNote Onが重複しました。全鍵のNote Offを待ちます。");
                    Reset("ambiguous_notes");return;
                }
                var keyOwner=AssignedFinger(note);
                if(keyOwner>=0)
                {Reject("key_assignment_conflict",$"割当拒否：鍵{note}は既に{FingerJapanese(keyOwner)}が占有しています。Note Offまで維持します。");return;}
                for(var otherSource=0;otherSource<2;otherSource++)for(var otherChannel=0;otherChannel<16;otherChannel++)
                    if((otherSource!=s || otherChannel!=channel) && held[otherSource,otherChannel,note])
                    {
                        Reject("ambiguous_notes","補正停止：同じ鍵の未解除イベントがあります。全鍵のNote Offを待ちます。");
                        Reset("ambiguous_notes");return;
                    }
                var f=settings.manualNoteMap?settings.noteToFinger[note]:(int)common.finger;
                if(f<0){Reset("unmapped_note");return;}
                if(f>4){Reset("invalid_finger");return;}
                if(owners[f].source!=null)
                {Reject("finger_assignment_conflict",$"割当拒否：{FingerJapanese(f)}は既に鍵{owners[f].note}を占有しています。Note Offまで維持します。");return;}
                if((!settings.manualNoteMap && HeldCount!=1) || blocked){Reset("ambiguous_notes");return;}
                // A released key can be retriggered by another finger immediately. Remove
                // the old frozen release pose before engaging its new owner.
                for(var other=0;other<5;other++)if(Contacts[other].note==note)ClearVisual(other,"key_retriggered");
                ClearVisual(f,"new_assignment");
                Contacts[f]=new RuleFingerContact{note=note,channel=message.Channel,generation=generation,eventIndex=message.EventIndex,onsetEventIndex=message.EventIndex,
                    source=source,received=received,observed=observed,onset=observed,phaseStart=observed,state=TipCorrectionState.Engaging,reason="note_on"};
                owners[f]=Contacts[f];
            }
            else
            {
                for(var f=0;f<5;f++)if(owners[f].source==source && owners[f].channel==message.Channel && owners[f].note==note)owners[f]=default;
                for(var f=0;f<5;f++)if(Contacts[f].source==source && Contacts[f].channel==message.Channel && Contacts[f].note==note)
                {var contact=Contacts[f];contact.eventIndex=message.EventIndex;contact.received=received;contact.observed=observed;Contacts[f]=contact;Release(f,observed,"note_off");}
                if(HeldCount==0)blocked=false;
            }
        }
        void Release(int f,double time,string reason)
        {
            var c=Contacts[f];if(c.state==TipCorrectionState.Inactive || c.state==TipCorrectionState.Releasing)return;
            c.releaseGain=c.gain;c.phaseStart=time;c.state=TipCorrectionState.Releasing;c.reason=reason;c.targetCapped=false;Contacts[f]=c;
            previousReleaseFactor[f]=1;
            for(var j=0;j<3;j++)releaseRotations[f,j]=lastRotations[f,j];
        }
        public void DisconnectSource(string source,double time,string reason="midi_disconnect")
        {
            var s=source=="BLE"?0:source=="UDP"?1:-1;if(s<0)return;
            for(var c=0;c<16;c++)for(var n=0;n<128;n++)if(held[s,c,n]){held[s,c,n]=false;HeldCount--;}
            for(var f=0;f<5;f++)
            {
                if(owners[f].source==source)owners[f]=default;
                // Disconnect is a safety boundary: do not carry a frozen pose into a new generation.
                if(Contacts[f].source==source)ClearVisual(f,reason);
            }
            if(HeldCount==0)blocked=false;
        }
        public void SynchronizeUdpOwnership(byte[] bits,double time)
        {
            if(bits==null || bits.Length!=256)return;
            DisconnectSource("UDP",time,"udp_snapshot_repair");
            for(var c=0;c<16;c++)for(var n=0;n<128;n++)if((bits[c*16+(n>>3)]&(1<<(n&7)))!=0){held[1,c,n]=true;HeldCount++;}
            Reset("udp_snapshot_repair");
        }
        public void EndSession(double time){for(var f=0;f<5;f++)Release(f,time,"session_end");blocked=HeldCount>0;}
        public void Watchdog(double time)
        {
            if(double.IsFinite(lastPoseTime) && time-lastPoseTime>.25)Reset("pose_timeout");
            for(var f=0;f<5;f++)if((Contacts[f].state==TipCorrectionState.Engaging || Contacts[f].state==TipCorrectionState.Active) && time-Contacts[f].onset>common.maxHoldSeconds)
                Release(f,time,"note_timeout");
        }
        static float Smooth(double elapsed,float duration){var t=duration<=0?1:Mathf.Clamp01((float)(elapsed/duration));return t*t*(3-2*t);}
        public void Process(HandPoseFrame raw,HandPoseFrame display)
        {
            SolvedThisFrame=false;
            var start=Stopwatch.GetTimestamp();ProcessPose(raw,display);RuleHandPoseSolver.SanitizeDisplay(display);
            ProcessingMilliseconds=(float)((Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);
        }
        void ProcessPose(HandPoseFrame raw,HandPoseFrame display)
        {
            identity.Process(raw,display);
            if(!common.enabled || settings.method==HandCorrectionMethod.Raw){Reset("disabled");return;}
            if(!common.IsValid || !settings.IsValid || common.selectionMode!=StrikingFingerMode.Manual){Reset("invalid_settings");return;}
            if(common.leftHand!=lastLeft || common.finger!=lastFinger || common.enabled!=lastEnabled || settings.manualNoteMap!=lastManualMap){Reset("selection_changed");return;}
            if(blocked){Reason="await_all_notes_off";return;}
            if(calibrated==null || !calibrated()){Reset("calibration_invalid");return;}
            if(!(common.leftHand?raw.LeftTracked:raw.RightTracked)){Reset("tracking_invalid");return;}
            if(!Geometry.Refresh(keyboard,origin())){Reset("transform_invalid");return;}
            if(hasTransform && lastTransform!=Geometry.TrackingToKeyboard){Reset("transform_changed");return;}
            hasTransform=true;lastTransform=Geometry.TrackingToKeyboard;
            var dynamic=raw.UpdateType==XRHandSubsystem.UpdateType.Dynamic;
            if(!dynamic && !hasPose)return;
            var now=raw.AbsoluteTimeSeconds;
            if(!double.IsFinite(now) || (dynamic && now<=lastPoseTime)){Reset("nonmonotonic_pose");return;}
            if(dynamic && double.IsFinite(lastPoseTime) && now-lastPoseTime>.25){Reset("pose_timeout");return;}
            var dt=dynamic?(double.IsFinite(lastPoseTime)?Mathf.Min(.05f,(float)(now-lastPoseTime)):0):0;
            Reason="idle_constraints";
            for(var f=0;f<5;f++)
            {
                var tip=raw.FindJoint(common.leftHand,DisplayFingerSolver.Tip((CorrectionFinger)f));
                targets[f]=tip.Pose.position;weights[f]=0;solver.FrozenFactors[f]=-1;
                solver.TemporalRadians[f]=float.PositiveInfinity;
                for(var j=0;j<4;j++)solver.JointDisplacementLimits[f,j]=float.PositiveInfinity;
                if(!dynamic)
                {
                    solver.FrozenFactors[f]=1;for(var j=0;j<3;j++)solver.FrozenRotations[f,j]=lastRotations[f,j];continue;
                }
                var c=Contacts[f];
                var contactSource=c.source=="BLE"?0:c.source=="UDP"?1:-1;
                var contactHeld=contactSource>=0 && c.channel>=1 && c.channel<=16 && c.note>=0 && held[contactSource,c.channel-1,c.note];
                if(settings.manualNoteMap && contactHeld && settings.noteToFinger[c.note]!=f)
                {Reset("assignment_changed");return;}
                if(c.state!=TipCorrectionState.Inactive && (!tip.PoseValid || !DisplayFingerSolver.Finite(tip.Pose.position))) {Reset("tracking_invalid");return;}
                if(c.state!=TipCorrectionState.Inactive && (now<c.observed || now<c.received)){Reason="before_midi_observation";continue;}
                if(c.state==TipCorrectionState.Engaging || c.state==TipCorrectionState.Active)
                {
                    if(now-c.onset>common.maxHoldSeconds){Release(f,now,"note_timeout");c=Contacts[f];}
                    else
                    {
                        c.gain=Smooth(now-c.onset,common.engageSeconds);c.state=c.gain>=1?TipCorrectionState.Active:TipCorrectionState.Engaging;
                        var surface=Geometry.Surface(c.note);
                        if(surface==null){Reset("key_unavailable");return;}
                        c.targetCapped=false;
                        if(settings.contact)
                        {
                            var rawKeyboard=Geometry.TrackingToKeyboard.MultiplyPoint3x4(tip.Pose.position);
                            var target=surface.Target(rawKeyboard,common.tipToContactKeyboardMeters+settings.fingers[f].contactOffsetKeyboardMeters,
                                common.planarToleranceMeters,common.belowSurfaceToleranceMeters,common.aboveSurfaceToleranceMeters);
                            var delta=Geometry.KeyboardToTracking.MultiplyVector(target-rawKeyboard)*common.strength;
                            c.targetCapped=delta.magnitude>Mathf.Min(common.maxCorrectionMeters,settings.fingers[f].maxDisplacementMeters);
                            delta=Vector3.ClampMagnitude(delta,Mathf.Min(common.maxCorrectionMeters,settings.fingers[f].maxDisplacementMeters))*c.gain;
                            var alpha=settings.smoothingSeconds<=0?1:1-Mathf.Exp(-dt/settings.smoothingSeconds);
                            var filtered=Vector3.Lerp(tipOffsets[f],delta,alpha);
                            filtered=Vector3.MoveTowards(tipOffsets[f],filtered,settings.maximumOffsetSpeedMetersPerSecond*dt);
                            targets[f]=tip.Pose.position+filtered;weights[f]=1;
                        }
                        c.reason="contact";Reason="solving";
                    }
                }
                if(c.state==TipCorrectionState.Releasing)
                {
                    var factor=1-Smooth(now-c.phaseStart,common.releaseSeconds);
                    c.gain=c.releaseGain*factor;solver.FrozenFactors[f]=factor;
                    for(var j=0;j<3;j++)solver.FrozenRotations[f,j]=releaseRotations[f,j];
                    for(var j=0;j<4;j++)solver.JointDisplacementLimits[f,j]=lastDisplacements[f,j]*(previousReleaseFactor[f]>0?factor/previousReleaseFactor[f]:0);
                    previousReleaseFactor[f]=factor;
                    if(factor<=0)c.state=TipCorrectionState.Inactive;
                    Reason="releasing";
                }
                var total=0f;
                for(var j=0;j<3;j++)
                {
                    var a=raw.FindJoint(common.leftHand,(XRHandJointID)((int)RuleHandPoseSolver.First(f)+j)).Pose.position;
                    var b=raw.FindJoint(common.leftHand,(XRHandJointID)((int)RuleHandPoseSolver.First(f)+j+1)).Pose.position;
                    total+=Vector3.Distance(a,b);solver.PreviousRotations[f,j]=lastRotations[f,j];
                }
                solver.TemporalRadians[f]=total>1e-5f?settings.maximumOffsetSpeedMetersPerSecond*dt/total:0;
                Contacts[f]=c;
            }
            solver.ConstraintStrength=common.strength;
            solver.Solve(raw,display,common.leftHand,targets,weights,settings,common.maxCorrectionMeters);
            SolvedThisFrame=true;
            for(var f=0;f<5;f++)if(Contacts[f].state!=TipCorrectionState.Inactive && !solver.Metrics[f].valid)
            {Reset("tracking_invalid");identity.Process(raw,display);return;}
            if(solver.Reason!="solved")Reason=solver.Reason;
            if(!dynamic)return;
            for(var f=0;f<5;f++)
            {
                if(!solver.Metrics[f].valid)continue;
                tipOffsets[f]=solver.Metrics[f].correctedTip-solver.Metrics[f].rawTip;
                for(var j=0;j<4;j++)
                {
                    var id=(XRHandJointID)((int)RuleHandPoseSolver.First(f)+j);
                    lastDisplacements[f,j]=Vector3.Distance(raw.FindJoint(common.leftHand,id).Pose.position,display.FindJoint(common.leftHand,id).Pose.position);
                    if(j==3)continue;
                    var child=(XRHandJointID)((int)id+1);
                    lastRotations[f,j]=Quaternion.FromToRotation(raw.FindJoint(common.leftHand,child).Pose.position-raw.FindJoint(common.leftHand,id).Pose.position,
                        display.FindJoint(common.leftHand,child).Pose.position-display.FindJoint(common.leftHand,id).Pose.position);
                }
            }
            hasPose=true;lastPoseTime=now;
        }
    }
}
