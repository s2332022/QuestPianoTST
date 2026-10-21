using System;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed partial class QuestRawSessionLog
    {
        const string RulePoseHeader="schema_version,session_id,quest_timestamp_sec,unity_frame,callback_index,snapshot_id,calibration_id,requested_method,executed_method,enabled,hand,finger,coordinates_valid,solver_finger_valid,note,source,channel,generation,event_index,onset_event_index,onset_sec,received_sec,observed_sec,state,gain,contact_reason,reason,target_capped,solver_limited,coordinate_space,position_unit,raw_x,raw_y,raw_z,corrected_x,corrected_y,corrected_z,delta_x,delta_y,delta_z,delta_m,planar_before_m,planar_after_m,normal_before_m,normal_after_m,band_excess_before_m,band_excess_after_m,penetration_before_m,penetration_after_m,interference_before_m,interference_after_m,bone_length_error_m,angular_violation_deg,tip_target_residual_m,solver_iterations,processing_ms,settings_revision";
        const string RuleJointHeader="schema_version,session_id,quest_timestamp_sec,callback_index,snapshot_id,requested_method,executed_method,hand,joint_id,joint_name,pose_valid,coordinate_space,position_unit,raw_x,raw_y,raw_z,corrected_x,corrected_y,corrected_z,rotation_coordinate_space,corrected_qx,corrected_qy,corrected_qz,corrected_qw,settings_revision";
        readonly int[] ruleConfig=new int[512], previousRuleConfig=new int[512];
        readonly Vector3[,] ruleRaw=new Vector3[5,4], ruleCorrected=new Vector3[5,4];
        readonly bool[] ruleValid=new bool[5];
        readonly StringBuilder ruleRows=new StringBuilder(8192),ruleJointRows=new StringBuilder(12288);
        int previousRuleConfigCount=-1,ruleRevision;
        [Serializable] sealed class RuleConfigEntry
        {
            public int schema_version=1,revision;
            public double quest_timestamp_sec;
            public long callback_index;
            public bool configuration_valid;
            public string common_settings_json,rule_settings_json;
            public string metric_definition="virtual geometric consistency; not independent physical tracking accuracy";
            public string solver_residual_definition="distance to filtered R1 tip target; unassigned fingers use Raw tip objective; not a proof of global convergence";
            public string penetration_model="rest key tops, sphere endpoints and midpoint on each bone; approximate, not continuous mesh collision";
            public string interference_model="maximum capsule overlap across all distinct finger segment pairs";
            public string processing_ms_definition="NEW only: copy/validation/geometry/solve/sanitize CPU elapsed, excluding this logger; OLD/Raw fallback unsolved samples may be blank";
            public string rotation_coordinate_space="TrackingOriginLocal; positions and distances are KeyboardRootLocal metres";
            public string event_reference="onset_event_index anchors assignment; event_index updates for matched NoteOff; non-MIDI stops retain preceding associated MIDI event";
        }
        void ResetRuleSession(){previousRuleConfigCount=-1;ruleRevision=0;}
        static void Put(int[] b,ref int i,int v){b[i++]=v;}
        static void Put(int[] b,ref int i,float v)=>Put(b,ref i,BitConverter.SingleToInt32Bits(v));
        static void Put(int[] b,ref int i,bool v)=>Put(b,ref i,v?1:0);
        static void Put(int[] b,ref int i,Vector3 v){Put(b,ref i,v.x);Put(b,ref i,v.y);Put(b,ref i,v.z);}
        static int Configuration(int[] b,MidiTipCorrectionSettings c,RuleHandPoseSettings s)
        {
            var i=0;
            Put(b,ref i,c.enabled);Put(b,ref i,c.leftHand);Put(b,ref i,(int)c.finger);Put(b,ref i,(int)c.selectionMode);
            Put(b,ref i,c.strength);Put(b,ref i,c.maxCorrectionMeters);Put(b,ref i,c.planarToleranceMeters);Put(b,ref i,c.tipToContactKeyboardMeters);
            Put(b,ref i,c.belowSurfaceToleranceMeters);Put(b,ref i,c.aboveSurfaceToleranceMeters);Put(b,ref i,c.engageSeconds);Put(b,ref i,c.releaseSeconds);Put(b,ref i,c.maxHoldSeconds);
            Put(b,ref i,(int)s.method);Put(b,ref i,s.contact);Put(b,ref i,s.joints);Put(b,ref i,s.nonpenetration);Put(b,ref i,s.interference);
            Put(b,ref i,s.iterations);Put(b,ref i,s.wholeHandRmsLimitMeters);Put(b,ref i,s.permittedPenetrationMeters);Put(b,ref i,s.segmentRadiusMeters);
            Put(b,ref i,s.interferenceRadiusMeters);Put(b,ref i,s.smoothingSeconds);Put(b,ref i,s.maximumOffsetSpeedMetersPerSecond);Put(b,ref i,s.manualNoteMap);
            Put(b,ref i,s.fingers?.Length??-1);
            if(s.fingers!=null)for(var f=0;f<Math.Min(5,s.fingers.Length);f++)
            {
                var v=s.fingers[f];Put(b,ref i,v!=null);
                if(v==null)continue;Put(b,ref i,v.contactOffsetKeyboardMeters);Put(b,ref i,v.maxDisplacementMeters);Put(b,ref i,v.maximumBendDegrees);Put(b,ref i,v.maximumChangeFromRawDegrees);
            }
            Put(b,ref i,s.noteToFinger?.Length??-1);
            if(s.noteToFinger!=null)for(var n=0;n<Math.Min(128,s.noteToFinger.Length);n++)Put(b,ref i,s.noteToFinger[n]);
            return i;
        }
        public void RecordRulePose(HandPoseFrame raw,HandPoseFrame display,HandCorrectionRouter router,MidiTipCorrectionProcessor legacy,
            MidiTipCorrectionSettings common,RuleHandPoseSettings settings,VirtualPianoKeyboard keyboard,string calibrationId)
        {
            if(raw.UpdateType!=XRHandSubsystem.UpdateType.Dynamic || !EnsureSnapshot(keyboard,raw.AbsoluteTimeSeconds))return;
            var configCount=Configuration(ruleConfig,common,settings);var changed=configCount!=previousRuleConfigCount;
            for(var i=0;i<configCount && !changed;i++)changed=ruleConfig[i]!=previousRuleConfig[i];
            if(changed)
            {
                ruleRevision++;Array.Copy(ruleConfig,previousRuleConfig,configCount);previousRuleConfigCount=configCount;
                Enqueue(5,JsonUtility.ToJson(new RuleConfigEntry{revision=ruleRevision,quest_timestamp_sec=raw.AbsoluteTimeSeconds,callback_index=raw.CallbackIndex,
                    configuration_valid=common.IsValid&&settings.IsValid,
                    common_settings_json=JsonUtility.ToJson(common),rule_settings_json=JsonUtility.ToJson(settings)}));
            }
            var geometry=router.New.Geometry;var geometryValid=geometry.Refresh(keyboard,ResearchServices.TrackingOrigin);
            var hand=common.leftHand?"Left":"Right";
            var source=common.leftHand?raw.LeftJoints:raw.RightJoints;
            var corrected=common.leftHand?display.LeftJoints:display.RightJoints;
            for(var f=0;f<5;f++)
            {
                ruleValid[f]=geometryValid && (common.leftHand?raw.LeftTracked:raw.RightTracked);
                for(var j=0;j<4;j++)
                {
                    var id=(XRHandJointID)((int)RuleHandPoseSolver.First(f)+j);var a=raw.FindJoint(common.leftHand,id);var b=display.FindJoint(common.leftHand,id);
                    ruleValid[f]&=a.PoseValid&&b.PoseValid&&DisplayFingerSolver.Finite(a.Pose.position)&&DisplayFingerSolver.Finite(b.Pose.position);
                    ruleRaw[f,j]=geometry.TrackingToKeyboard.MultiplyPoint3x4(a.Pose.position);ruleCorrected[f,j]=geometry.TrackingToKeyboard.MultiplyPoint3x4(b.Pose.position);
                }
            }
            var rows=ruleRows;var jointRows=ruleJointRows;rows.Clear();jointRows.Clear();
            var old=router.ExecutedMethod=="Legacy";
            for(var f=0;f<5;f++)
            {
                var c=router.New.Contacts[f];var sample=legacy.Latest;
                if(old)c=new RuleFingerContact{note=f==(int)common.finger?sample.Note:-1,source=f==(int)common.finger?sample.Source:"",
                    channel=sample.Channel,generation=sample.Generation,eventIndex=sample.EventIndex,onsetEventIndex=-1,
                    received=sample.ReceivedTime,observed=sample.ObservedTime,state=f==(int)common.finger?sample.State:TipCorrectionState.Inactive,
                    gain=f==(int)common.finger?sample.Gain:0,reason=sample.Reason};
                var valid=ruleValid[f];var a=ruleRaw[f,3];var b=ruleCorrected[f,3];var delta=b-a;
                var surface=geometry.Surface(c.note);var contactValid=valid && surface!=null;
                var offset=common.tipToContactKeyboardMeters+(old?Vector3.zero:settings.fingers!=null&&settings.fingers.Length>f&&settings.fingers[f]!=null?settings.fingers[f].contactOffsetKeyboardMeters:Vector3.zero);
                var before=a+offset;var after=b+offset;
                var normalBefore=contactValid?before.y-surface.Height:0;var normalAfter=contactValid?after.y-surface.Height:0;
                var penetrationBefore=0f;var penetrationAfter=0f;var interferenceBefore=0f;var interferenceAfter=0f;var boneError=0f;var angleViolation=0f;
                if(valid)
                {
                    for(var j=0;j<3;j++)
                    {
                        boneError=Mathf.Max(boneError,Mathf.Abs(Vector3.Distance(ruleRaw[f,j],ruleRaw[f,j+1])-Vector3.Distance(ruleCorrected[f,j],ruleCorrected[f,j+1])));
                        for(var k=0;k<=2;k++)
                        {
                            penetrationBefore=Mathf.Max(penetrationBefore,geometry.Penetration(geometry.KeyboardToTracking.MultiplyPoint3x4(Vector3.Lerp(ruleRaw[f,j],ruleRaw[f,j+1],k*.5f)),settings.segmentRadiusMeters,settings.permittedPenetrationMeters));
                            penetrationAfter=Mathf.Max(penetrationAfter,geometry.Penetration(geometry.KeyboardToTracking.MultiplyPoint3x4(Vector3.Lerp(ruleCorrected[f,j],ruleCorrected[f,j+1],k*.5f)),settings.segmentRadiusMeters,settings.permittedPenetrationMeters));
                        }
                        var guideId=f==0?XRHandJointID.Wrist:(XRHandJointID)((int)RuleHandPoseSolver.First(f)-1);
                        var guide=display.FindJoint(common.leftHand,guideId);
                        var parent=j==0?ruleCorrected[f,0]-geometry.TrackingToKeyboard.MultiplyPoint3x4(guide.Pose.position):ruleCorrected[f,j]-ruleCorrected[f,j-1];
                        if(settings.fingers!=null&&settings.fingers.Length>f&&settings.fingers[f]!=null)
                            angleViolation=Mathf.Max(angleViolation,Vector3.Angle(parent,ruleCorrected[f,j+1]-ruleCorrected[f,j])-settings.fingers[f].maximumBendDegrees[j]);
                    }
                    for(var g=0;g<5;g++)if(g!=f && ruleValid[g])for(var j=0;j<3;j++)for(var k=0;k<3;k++)
                    {
                        RuleHandPoseSolver.Closest(ruleRaw[f,j],ruleRaw[f,j+1],ruleRaw[g,k],ruleRaw[g,k+1],out var u,out var v);
                        interferenceBefore=Mathf.Max(interferenceBefore,settings.interferenceRadiusMeters*2-Vector3.Distance(u,v));
                        RuleHandPoseSolver.Closest(ruleCorrected[f,j],ruleCorrected[f,j+1],ruleCorrected[g,k],ruleCorrected[g,k+1],out u,out v);
                        interferenceAfter=Mathf.Max(interferenceAfter,settings.interferenceRadiusMeters*2-Vector3.Distance(u,v));
                    }
                }
                string V(float v)=>valid&&float.IsFinite(v)?N(v):"";
                string C(float v)=>contactValid&&float.IsFinite(v)?N(v):"";
                float Band(float n)=>Mathf.Max(0,Mathf.Max(n-common.aboveSurfaceToleranceMeters,-common.belowSurfaceToleranceMeters-n));
                var solved=!old && router.ExecutedMethod!="PcDisplayPose" && router.New.SolvedThisFrame;
                rows.Append(string.Join(",",1,session,N(raw.AbsoluteTimeSeconds),raw.UnityFrame,raw.CallbackIndex,snapshot.snapshot_id,Csv(calibrationId),settings.method,router.ExecutedMethod,common.enabled,hand,(CorrectionFinger)f,valid,solved?router.New.Solver.Metrics[f].valid.ToString():"",
                    c.note,c.source??"",c.channel,c.generation,c.eventIndex,c.onsetEventIndex,old?"":N(c.onset),N(c.received),N(c.observed),c.state,N(c.gain),c.reason??"",old?sample.Reason:router.New.Reason,old?(f==(int)common.finger?sample.Capped.ToString():""):c.targetCapped.ToString(),solved?router.New.Solver.Metrics[f].limited.ToString():"","KeyboardRootLocal","m",
                    V(a.x),V(a.y),V(a.z),V(b.x),V(b.y),V(b.z),V(delta.x),V(delta.y),V(delta.z),V(delta.magnitude),C(contactValid?surface.PlanarDistance(before):0),C(contactValid?surface.PlanarDistance(after):0),C(normalBefore),C(normalAfter),C(Band(normalBefore)),C(Band(normalAfter)),
                    V(penetrationBefore),V(penetrationAfter),V(interferenceBefore),V(interferenceAfter),V(boneError),V(angleViolation),solved&&router.New.Solver.Metrics[f].valid?V(router.New.Solver.Metrics[f].targetResidual):"",solved?router.New.Solver.Iterations.ToString(F):"",solved?N(router.New.ProcessingMilliseconds):"",ruleRevision)).Append('\n');
            }
            for(var i=0;i<source.Length;i++)
            {
                var a=source[i];var b=display.FindJoint(common.leftHand,a.JointId);var valid=geometryValid&&a.PoseValid&&b.PoseValid&&DisplayFingerSolver.Finite(a.Pose.position)&&DisplayFingerSolver.Finite(b.Pose.position)&&RuleHandPoseSolver.Finite(b.Pose.rotation);
                var p=geometry.TrackingToKeyboard.MultiplyPoint3x4(a.Pose.position);var q=geometry.TrackingToKeyboard.MultiplyPoint3x4(b.Pose.position);var r=b.Pose.rotation;
                string V(float v)=>valid&&float.IsFinite(v)?N(v):"";
                jointRows.Append(string.Join(",",1,session,N(raw.AbsoluteTimeSeconds),raw.CallbackIndex,snapshot.snapshot_id,settings.method,router.ExecutedMethod,hand,(int)a.JointId,a.JointId,valid,"KeyboardRootLocal","m",V(p.x),V(p.y),V(p.z),V(q.x),V(q.y),V(q.z),"TrackingOriginLocal",V(r.x),V(r.y),V(r.z),V(r.w),ruleRevision)).Append('\n');
            }
            // Only immutable strings enter the writer queue. No mutable frame escapes this callback.
            Enqueue(4,rows.ToString().TrimEnd('\n'));Enqueue(6,jointRows.ToString().TrimEnd('\n'));
        }
    }
}
