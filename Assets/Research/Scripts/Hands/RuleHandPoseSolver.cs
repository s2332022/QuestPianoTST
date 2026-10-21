using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    public struct RuleFingerMetrics
    {
        public bool valid, limited;
        public Vector3 rawTip, correctedTip;
        public float boneError, angularViolationDegrees, targetResidual, rawPenetration, penetration, rawInterference, interference;
    }

    /// <summary>Bounded, preallocated hand-wide projected CCD. All solver positions: TrackingOriginLocal metres.
    /// Never writes Raw, wrist, palm, metacarpals or the fixed chain anchors.</summary>
    public sealed partial class RuleHandPoseSolver
    {
        readonly Vector3[,] raw=new Vector3[5,4], p=new Vector3[5,4], candidate=new Vector3[5,4];
        readonly Vector3[,] rawAxes=new Vector3[5,3];
        readonly Vector3[] guide=new Vector3[5];
        readonly float[,] length=new float[5,3];
        readonly int[,] index=new int[5,4];
        readonly bool[] valid=new bool[5];
        public readonly RuleFingerMetrics[] Metrics=new RuleFingerMetrics[5];
        public int Iterations { get; private set; }
        public float MaximumBoneError { get; private set; }
        public string Reason { get; private set; }
        public readonly float[] FrozenFactors=new float[5], TemporalRadians=new float[5];
        public readonly Quaternion[,] FrozenRotations=new Quaternion[5,3], PreviousRotations=new Quaternion[5,3];
        public readonly float[,] JointDisplacementLimits=new float[5,4];
        public RuleHandPoseSolver()
        {
            for(var f=0;f<5;f++)
            {
                FrozenFactors[f]=-1;TemporalRadians[f]=float.PositiveInfinity;
                for(var j=0;j<4;j++)JointDisplacementLimits[f,j]=float.PositiveInfinity;
                for(var j=0;j<3;j++)FrozenRotations[f,j]=PreviousRotations[f,j]=Quaternion.identity;
            }
        }
        public static XRHandJointID First(int finger)=>finger==0?XRHandJointID.ThumbMetacarpal:
            (XRHandJointID)((int)DisplayFingerSolver.Tip((CorrectionFinger)finger)-3);
        public static bool Finite(Quaternion q)=>float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&
            q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>1e-8f;
        public static void SanitizeDisplay(HandPoseFrame display)
        {
            Sanitize(display.LeftJoints);Sanitize(display.RightJoints);
        }
        static void Sanitize(HandJointPose[] joints)
        {
            for(var i=0;i<joints.Length;i++)
            {
                var j=joints[i];
                if(!DisplayFingerSolver.Finite(j.Pose.position) || !Finite(j.Pose.rotation))
                {j.PoseValid=false;j.Pose=Pose.identity;joints[i]=j;}
            }
        }

        public void Solve(HandPoseFrame input, HandPoseFrame display, bool left, Vector3[] targets,
            float[] weights, RuleHandPoseSettings settings, float commonLimit)
        {
            Iterations=0;MaximumBoneError=0;Reason="solved";
            for(var f=0;f<5;f++){environmentTarget[f]=Vector3.zero;contactWeight[f]=Mathf.Clamp01(weights[f]);}
            for(var f=0;f<5;f++)Metrics[f]=default;
            if(!settings.IsValid || !float.IsFinite(commonLimit) || commonLimit<0) {Reason="invalid_settings";return;}
            var source=left?input.LeftJoints:input.RightJoints;var dest=left?display.LeftJoints:display.RightJoints;
            SanitizeDisplay(display);
            for(var f=0;f<5;f++)
            {
                valid[f]=left?input.LeftTracked:input.RightTracked;Metrics[f]=default;
                for(var j=0;j<4;j++)
                {
                    index[f,j]=-1;var id=(XRHandJointID)((int)First(f)+j);
                    for(var k=0;k<source.Length;k++)if(source[k].JointId==id){index[f,j]=k;break;}
                    if(index[f,j]<0){valid[f]=false;continue;}
                    var s=source[index[f,j]];
                    if(!s.PoseValid || !DisplayFingerSolver.Finite(s.Pose.position) || !Finite(s.Pose.rotation))valid[f]=false;
                    raw[f,j]=p[f,j]=s.Pose.position;
                }
                var g=input.FindJoint(left,f==0?XRHandJointID.Wrist:(XRHandJointID)((int)First(f)-1));
                guide[f]=raw[f,0]-g.Pose.position;
                if(!g.PoseValid || !DisplayFingerSolver.Finite(guide[f]) || guide[f].sqrMagnitude<1e-10f)valid[f]=false;
                for(var j=0;j<3;j++)
                {
                    length[f,j]=Vector3.Distance(raw[f,j],raw[f,j+1]);
                    if(length[f,j]<.00001f || !float.IsFinite(length[f,j]))valid[f]=false;
                    var parent=j==0?guide[f]:raw[f,j]-raw[f,j-1];
                    rawAxes[f,j]=Vector3.Cross(parent,raw[f,j+1]-raw[f,j]);
                }
                if(!DisplayFingerSolver.Finite(targets[f]) || !float.IsFinite(weights[f]))valid[f]=false;
                if(valid[f] && FrozenFactors[f]>=0)
                    for(var j=0;j<3;j++)p[f,j+1]=p[f,j]+Quaternion.Slerp(Quaternion.identity,FrozenRotations[f,j],FrozenFactors[f])*(raw[f,j+1]-raw[f,j]);
            }
            for(var iteration=0;iteration<settings.iterations;iteration++)
            {
                Iterations++;
                PrepareEnvironment(settings);
                for(var f=0;f<5;f++)
                {
                    if(!valid[f] || FrozenFactors[f]>=0)continue;
                    var target=Vector3.Lerp(raw[f,3],targets[f],Mathf.Clamp01(weights[f]));
                    target+=EnvironmentPush(f,settings);
                    if((target-raw[f,3]).sqrMagnitude<1e-14f)continue;
                    for(var j=2;j>=0;j--)
                    {
                        var from=p[f,3]-p[f,j];var to=target-p[f,j];
                        if(from.sqrMagnitude<1e-12f || to.sqrMagnitude<1e-12f)continue;
                        var rotation=Quaternion.FromToRotation(from,to);
                        for(var k=j+1;k<4;k++)p[f,k]=p[f,j]+rotation*(p[f,k]-p[f,j]);
                        ProjectAngles(f,settings);
                    }
                }
            }
            for(var f=0;f<5;f++)if(valid[f] && FrozenFactors[f]<0 && float.IsFinite(TemporalRadians[f]))
                for(var j=0;j<3;j++)
                {
                    var previous=PreviousRotations[f,j]*(raw[f,j+1]-raw[f,j]);
                    var direction=Vector3.RotateTowards(previous.normalized,(p[f,j+1]-p[f,j]).normalized,TemporalRadians[f],0);
                    var shift=p[f,j]+direction*length[f,j]-p[f,j+1];
                    for(var k=j+1;k<4;k++)p[f,k]+=shift;
                }
            // Angular reconstruction preserves lengths, unlike blending Cartesian joint positions.
            for(var f=0;f<5;f++)for(var j=0;j<4;j++)candidate[f,j]=p[f,j];
            var scale=1f;var accepted=false;
            for(var attempt=0;attempt<18;attempt++)
            {
                var squared=0f;var count=0;var safe=true;
                for(var f=0;f<5;f++)
                {
                    if(!valid[f])continue;
                    p[f,0]=raw[f,0];
                    for(var j=0;j<3;j++)
                    {
                        var direction=Vector3.Slerp((raw[f,j+1]-raw[f,j]).normalized,(candidate[f,j+1]-candidate[f,j]).normalized,scale);
                        p[f,j+1]=p[f,j]+direction.normalized*length[f,j];
                        var d=Vector3.Distance(p[f,j+1],raw[f,j+1]);
                        safe &= DisplayFingerSolver.Finite(p[f,j+1]) && d<=Mathf.Min(Mathf.Min(commonLimit,settings.fingers[f].maxDisplacementMeters),JointDisplacementLimits[f,j+1])+1e-7f;
                        if(settings.UseJoints)
                            safe &= Vector3.Angle(raw[f,j+1]-raw[f,j],p[f,j+1]-p[f,j])<=settings.fingers[f].maximumChangeFromRawDegrees+.001f;
                        squared+=d*d;count++;
                    }
                }
                safe &= count==0 || Mathf.Sqrt(squared/count)<=settings.wholeHandRmsLimitMeters+1e-7f;
                if(safe){accepted=true;break;}
                scale=attempt==16?0:scale*.5f;Reason="displacement_limited";
            }
            if(!accepted)
            {
                for(var f=0;f<5;f++)for(var j=0;j<4;j++)p[f,j]=raw[f,j];
                Reason="numerical_fallback";
            }
            for(var f=0;f<5;f++)
            {
                if(!valid[f]){Metrics[f]=new RuleFingerMetrics{valid=false};continue;}
                var unchanged=true;
                for(var j=0;j<4;j++)if((p[f,j]-raw[f,j]).sqrMagnitude>1e-14f)unchanged=false;
                if(unchanged)for(var j=0;j<4;j++){p[f,j]=raw[f,j];dest[index[f,j]]=source[index[f,j]];}
                var metric=new RuleFingerMetrics{valid=true,rawTip=raw[f,3],correctedTip=p[f,3],limited=scale<1,
                    targetResidual=Vector3.Distance(targets[f],p[f,3])};
                for(var j=0;j<3;j++)
                {
                    metric.boneError=Mathf.Max(metric.boneError,Mathf.Abs(Vector3.Distance(p[f,j],p[f,j+1])-length[f,j]));
                    var parent=j==0?guide[f]:p[f,j]-p[f,j-1];
                    var violation=Vector3.Angle(parent,p[f,j+1]-p[f,j])-settings.fingers[f].maximumBendDegrees[j];
                    metric.angularViolationDegrees=Mathf.Max(metric.angularViolationDegrees,violation);
                    if(!unchanged)
                    {
                        var joint=dest[index[f,j]];
                        joint.Pose.position=p[f,j];
                        joint.Pose.rotation=Quaternion.FromToRotation(raw[f,j+1]-raw[f,j],p[f,j+1]-p[f,j])*source[index[f,j]].Pose.rotation;
                        dest[index[f,j]]=joint;
                    }
                }
                if(!unchanged)
                {
                    var tip=dest[index[f,3]];tip.Pose.position=p[f,3];
                    tip.Pose.rotation=Quaternion.FromToRotation(raw[f,3]-raw[f,2],p[f,3]-p[f,2])*source[index[f,3]].Pose.rotation;
                    dest[index[f,3]]=tip;
                }
                MaximumBoneError=Mathf.Max(MaximumBoneError,metric.boneError);Metrics[f]=metric;
            }
            FinishEnvironment(settings);
        }
        void ProjectAngles(int f,RuleHandPoseSettings settings)
        {
            if(!settings.UseJoints)return;
            for(var j=0;j<3;j++)
            {
                var parent=(j==0?guide[f]:p[f,j]-p[f,j-1]).normalized;
                var rawDirection=(raw[f,j+1]-raw[f,j]).normalized;
                var direction=(p[f,j+1]-p[f,j]).normalized;
                direction=Vector3.RotateTowards(rawDirection,direction,settings.fingers[f].maximumChangeFromRawDegrees*Mathf.Deg2Rad,0);
                // Preserve the observed bend side when identifiable; never invent a clinical hinge axis from a straight chain.
                if(rawAxes[f,j].sqrMagnitude>1e-10f && Vector3.Dot(Vector3.Cross(parent,direction),rawAxes[f,j])<0)
                    direction=parent;
                direction=Vector3.RotateTowards(parent,direction,settings.fingers[f].maximumBendDegrees[j]*Mathf.Deg2Rad,0);
                var offset=p[f,j]+direction.normalized*length[f,j]-p[f,j+1];
                for(var k=j+1;k<4;k++)p[f,k]+=offset;
            }
        }
    }
}
