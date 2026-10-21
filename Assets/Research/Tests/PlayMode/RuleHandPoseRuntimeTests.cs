using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RuleHandPoseRuntimeTests
    {
        [UnityTest] public IEnumerator FiveFingerSolverRunsAcrossDynamicFramesWithoutChangingRaw()
        {
            var n=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;
            var raw=new HandPoseFrame(n){RightTracked=true,UpdateType=XRHandSubsystem.UpdateType.Dynamic};
            for(var i=0;i<n;i++)raw.RightJoints[i]=new HandJointPose{JointId=(XRHandJointID)((int)XRHandJointID.BeginMarker+i),PoseValid=true,Pose=Pose.identity};
            void Set(XRHandJointID id,Vector3 p){for(var i=0;i<n;i++)if(raw.RightJoints[i].JointId==id){var v=raw.RightJoints[i];v.Pose.position=p;raw.RightJoints[i]=v;}}
            Set(XRHandJointID.Wrist,new Vector3(.04f,.07f,-.08f));
            for(var f=0;f<5;f++)
            {
                var first=RuleHandPoseSolver.First(f);var x=f*.022f;
                if(f>0)Set((XRHandJointID)((int)first-1),new Vector3(x,.06f,-.065f));
                Set(first,new Vector3(x,.06f,-.03f));Set((XRHandJointID)((int)first+1),new Vector3(x,.05f,-.005f));
                Set((XRHandJointID)((int)first+2),new Vector3(x,.035f,.01f));Set((XRHandJointID)((int)first+3),new Vector3(x,.02f,.02f));
            }
            var before=(HandJointPose[])raw.RightJoints.Clone();var display=new HandPoseFrame(n);
            var targets=new Vector3[5];var weights=new float[5];var solver=new RuleHandPoseSolver();
            var settings=new RuleHandPoseSettings{method=HandCorrectionMethod.JointConstraints};
            for(var frame=0;frame<10;frame++)
            {
                raw.AbsoluteTimeSeconds=10+frame/72d;raw.CallbackIndex=frame;
                new IdentityHandPoseProcessor().Process(raw,display);
                for(var f=0;f<5;f++){targets[f]=raw.FindJoint(false,DisplayFingerSolver.Tip((CorrectionFinger)f)).Pose.position+Vector3.down*.005f;weights[f]=1;}
                solver.Solve(raw,display,false,targets,weights,settings,.02f);
                Assert.That(solver.MaximumBoneError,Is.LessThan(1e-6));
                for(var i=0;i<n;i++)Assert.That(raw.RightJoints[i].Pose,Is.EqualTo(before[i].Pose));
                yield return null;
            }
        }
    }
}
