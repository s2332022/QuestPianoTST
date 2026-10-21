using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RuleHandPoseSolverTests
    {
        public static HandPoseFrame Frame()
        {
            var n=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;
            var r=new HandPoseFrame(n){LeftTracked=true,RightTracked=true,AbsoluteTimeSeconds=10,UpdateType=XRHandSubsystem.UpdateType.Dynamic};
            for(var i=0;i<n;i++)r.LeftJoints[i]=r.RightJoints[i]=new HandJointPose{JointId=(XRHandJointID)((int)XRHandJointID.BeginMarker+i),PoseValid=true,Pose=Pose.identity};
            Set(r,XRHandJointID.Wrist,new Vector3(.04f,.07f,-.08f));
            for(var f=0;f<5;f++)
            {
                var x=f*.022f;
                if(f>0)Set(r,(XRHandJointID)((int)RuleHandPoseSolver.First(f)-1),new Vector3(x,.06f,-.065f));
                Set(r,RuleHandPoseSolver.First(f),new Vector3(x,.06f,-.03f));
                Set(r,(XRHandJointID)((int)RuleHandPoseSolver.First(f)+1),new Vector3(x,.05f,-.005f));
                Set(r,(XRHandJointID)((int)RuleHandPoseSolver.First(f)+2),new Vector3(x,.035f,.01f));
                Set(r,(XRHandJointID)((int)RuleHandPoseSolver.First(f)+3),new Vector3(x,.02f,.02f));
            }
            return r;
        }
        public static void Set(HandPoseFrame frame,XRHandJointID id,Vector3 p)
        {for(var i=0;i<frame.RightJoints.Length;i++)if(frame.RightJoints[i].JointId==id){var j=frame.RightJoints[i];j.Pose.position=p;frame.RightJoints[i]=frame.LeftJoints[i]=j;}}
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void AllFiveFingersPreserveRawBonesPalmAndNonStrikingFingers(int finger)
        {
            var r=Frame();var before=(HandJointPose[])r.RightJoints.Clone();var d=new HandPoseFrame(before.Length);new IdentityHandPoseProcessor().Process(r,d);
            var targets=new Vector3[5];var weights=new float[5];
            for(var f=0;f<5;f++)targets[f]=r.FindJoint(false,DisplayFingerSolver.Tip((CorrectionFinger)f)).Pose.position;
            targets[finger]+=new Vector3(.002f,-.004f,0);weights[finger]=1;
            var solver=new RuleHandPoseSolver();var settings=new RuleHandPoseSettings{method=HandCorrectionMethod.JointConstraints};
            solver.Solve(r,d,false,targets,weights,settings,.02f);
            Assert.That(solver.Metrics[finger].valid,Is.True);
            Assert.That(Vector3.Distance(solver.Metrics[finger].rawTip,solver.Metrics[finger].correctedTip),Is.GreaterThan(.0001f));
            Assert.That(solver.MaximumBoneError,Is.LessThan(1e-6f));
            Assert.That(solver.Iterations,Is.EqualTo(settings.iterations));
            for(var i=0;i<before.Length;i++)
            {
                Assert.That(r.RightJoints[i].Pose,Is.EqualTo(before[i].Pose));
                Assert.That(d.LeftJoints[i].Pose,Is.EqualTo(r.LeftJoints[i].Pose));
                var id=before[i].JointId;
                if(id<RuleHandPoseSolver.First(finger)||id>DisplayFingerSolver.Tip((CorrectionFinger)finger))
                    Assert.That(Vector3.Distance(d.RightJoints[i].Pose.position,before[i].Pose.position),Is.LessThan(1e-7));
            }
            Assert.That(d.FindJoint(false,RuleHandPoseSolver.First(finger)).Pose.position,Is.EqualTo(r.FindJoint(false,RuleHandPoseSolver.First(finger)).Pose.position));
        }
        [Test] public void UnreachableTargetRespectsAllJointAndWholeHandLimits()
        {
            var r=Frame();var d=new HandPoseFrame(r.RightJoints.Length);new IdentityHandPoseProcessor().Process(r,d);
            var t=new Vector3[5];var w=new float[5];for(var f=0;f<5;f++){t[f]=Vector3.one;w[f]=1;}
            var settings=new RuleHandPoseSettings{method=HandCorrectionMethod.JointConstraints,wholeHandRmsLimitMeters=.001f};
            var solver=new RuleHandPoseSolver();solver.Solve(r,d,false,t,w,settings,.002f);
            float sq=0;int n=0;
            for(var f=0;f<5;f++)for(var j=1;j<4;j++)
            {
                var id=(XRHandJointID)((int)RuleHandPoseSolver.First(f)+j);var delta=Vector3.Distance(r.FindJoint(false,id).Pose.position,d.FindJoint(false,id).Pose.position);
                Assert.That(delta,Is.LessThanOrEqualTo(.002001f));sq+=delta*delta;n++;
            }
            Assert.That(Mathf.Sqrt(sq/n),Is.LessThanOrEqualTo(.001001f));Assert.That(solver.MaximumBoneError,Is.LessThan(1e-6));
        }
        [Test] public void InvalidSamplesAndSettingsNeverIntroduceNonfinitePositions()
        {
            var r=Frame();Set(r,XRHandJointID.IndexTip,new Vector3(float.NaN,0,0));
            var d=new HandPoseFrame(r.RightJoints.Length);new IdentityHandPoseProcessor().Process(r,d);
            var t=new Vector3[5];var w=new float[5];var s=new RuleHandPoseSettings();
            var solver=new RuleHandPoseSolver();solver.Solve(r,d,false,t,w,s,.02f);
            Assert.That(solver.Metrics[1].valid,Is.False);
            Assert.That(d.FindJoint(false,XRHandJointID.IndexTip).PoseValid,Is.False);
            Assert.That(DisplayFingerSolver.Finite(d.FindJoint(false,XRHandJointID.IndexTip).Pose.position),Is.True);
            Assert.That(DisplayFingerSolver.Finite(d.FindJoint(false,XRHandJointID.MiddleTip).Pose.position),Is.True);
            s.fingers[0].maximumBendDegrees.x=float.NaN;
            solver.Solve(r,d,false,t,w,s,.02f);Assert.That(solver.Reason,Is.EqualTo("invalid_settings"));
        }
        [Test] public void JointProjectionBoundsBendForFeasibleRawPose()
        {
            var r=Frame();var d=new HandPoseFrame(r.RightJoints.Length);new IdentityHandPoseProcessor().Process(r,d);
            var t=new Vector3[5];var w=new float[5];for(var f=0;f<5;f++){t[f]=new Vector3(f*.022f,.06f,-.02f);w[f]=1;}
            var s=new RuleHandPoseSettings{method=HandCorrectionMethod.JointConstraints};
            var solver=new RuleHandPoseSolver();solver.Solve(r,d,false,t,w,s,.03f);
            foreach(var m in solver.Metrics)Assert.That(m.angularViolationDegrees,Is.LessThan(.01f));
        }
        [Test] public void ExtremeFiniteTargetCannotIntroduceNonfiniteDisplay()
        {
            var raw=Frame();var d=new HandPoseFrame(raw.RightJoints.Length);new IdentityHandPoseProcessor().Process(raw,d);
            var targets=new Vector3[5];var weights=new float[5];for(var f=0;f<5;f++){targets[f]=Vector3.one*1e30f;weights[f]=1;}
            new RuleHandPoseSolver().Solve(raw,d,false,targets,weights,new RuleHandPoseSettings{method=HandCorrectionMethod.FullRules},.02f);
            foreach(var j in d.RightJoints){Assert.That(DisplayFingerSolver.Finite(j.Pose.position),Is.True);Assert.That(RuleHandPoseSolver.Finite(j.Pose.rotation),Is.True);}
        }
    }
}
