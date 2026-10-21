using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RuleHandEnvironmentTests
    {
        GameObject keyboardGo,originGo;
        RuleKeyboardGeometry geometry;
        [SetUp] public void Setup()
        {
            keyboardGo=new GameObject("rule geometry",typeof(VirtualPianoKeyboard));originGo=new GameObject("origin");
            var keyboard=keyboardGo.GetComponent<VirtualPianoKeyboard>();
            typeof(VirtualPianoKeyboard).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(keyboard,null);
            geometry=new RuleKeyboardGeometry();Assert.That(geometry.Refresh(keyboard,originGo.transform),Is.True);
        }
        [TearDown] public void Teardown(){Object.DestroyImmediate(keyboardGo);Object.DestroyImmediate(originGo);}
        [Test] public void WhiteBlackGeometryAndPermittedDepthAreDistinct()
        {
            var white=geometry.Surface(60);var black=geometry.Surface(61);
            Assert.That(black.Height,Is.GreaterThan(white.Height));
            var w=new Vector3(.01f,-.005f,.02f);
            Assert.That(geometry.Penetration(w,0,0),Is.EqualTo(.005f).Within(1e-6));
            Assert.That(geometry.Penetration(w,0,.006f),Is.Zero);
            var b=black.Regions[0].center;
            Assert.That(geometry.Penetration(new Vector3(b.x,0,b.y),0,0),Is.EqualTo(black.Height).Within(1e-6));
            Assert.That(geometry.Penetration(new Vector3(4,-1,4),.003f,0),Is.Zero);
        }
        [Test] public void NonStrikingFingerCanBeLiftedWithoutMovingWholeHand()
        {
            var raw=RuleHandPoseSolverTests.Frame();
            // Only the index tip intersects the front of C4.
            RuleHandPoseSolverTests.Set(raw,XRHandJointID.IndexTip,new Vector3(.022f,-.003f,.02f));
            var display=new HandPoseFrame(raw.RightJoints.Length);new IdentityHandPoseProcessor().Process(raw,display);
            var t=new Vector3[5];var w=new float[5];for(var f=0;f<5;f++)t[f]=raw.FindJoint(false,DisplayFingerSolver.Tip((CorrectionFinger)f)).Pose.position;
            var settings=new RuleHandPoseSettings{method=HandCorrectionMethod.FullRules,interference=false};
            var solver=new RuleHandPoseSolver{Geometry=geometry};solver.Solve(raw,display,false,t,w,settings,.02f);
            Assert.That(solver.Metrics[1].penetration,Is.LessThan(solver.Metrics[1].rawPenetration));
            Assert.That(solver.MaximumBoneError,Is.LessThan(1e-6));
            Assert.That(display.FindJoint(false,XRHandJointID.Wrist).Pose,Is.EqualTo(raw.FindJoint(false,XRHandJointID.Wrist).Pose));
        }
        [Test] public void CapsuleDistancesIncludeCrossingAndParallelSegments()
        {
            RuleHandPoseSolver.Closest(Vector3.left,Vector3.right,Vector3.down,Vector3.up,out var a,out var b);
            Assert.That(Vector3.Distance(a,b),Is.LessThan(1e-6));
            RuleHandPoseSolver.Closest(Vector3.zero,Vector3.right,Vector3.up,Vector3.one,out a,out b);
            Assert.That(Vector3.Distance(a,b),Is.EqualTo(1).Within(1e-6));
        }
        [Test] public void ZeroStrengthDoesNotMoveNonStrikingFingersEvenBelowKeyboard()
        {
            var raw=RuleHandPoseSolverTests.Frame();RuleHandPoseSolverTests.Set(raw,XRHandJointID.IndexTip,new Vector3(.022f,-.003f,.02f));
            var display=new HandPoseFrame(raw.RightJoints.Length);new IdentityHandPoseProcessor().Process(raw,display);
            var t=new Vector3[5];var w=new float[5];for(var f=0;f<5;f++)t[f]=raw.FindJoint(false,DisplayFingerSolver.Tip((CorrectionFinger)f)).Pose.position;
            var solver=new RuleHandPoseSolver{Geometry=geometry,ConstraintStrength=0};solver.Solve(raw,display,false,t,w,new RuleHandPoseSettings{method=HandCorrectionMethod.FullRules},.02f);
            for(var i=0;i<raw.RightJoints.Length;i++)Assert.That(display.RightJoints[i].Pose,Is.EqualTo(raw.RightJoints[i].Pose));
        }
        [Test] public void CoupledFingerRepulsionReducesInterferenceWithinHardLimits()
        {
            var raw=RuleHandPoseSolverTests.Frame();
            RuleHandPoseSolverTests.Set(raw,XRHandJointID.IndexDistal,new Vector3(.033f,.035f,.01f));
            RuleHandPoseSolverTests.Set(raw,XRHandJointID.MiddleDistal,new Vector3(.034f,.035f,.01f));
            RuleHandPoseSolverTests.Set(raw,XRHandJointID.IndexTip,new Vector3(.033f,.02f,.02f));
            RuleHandPoseSolverTests.Set(raw,XRHandJointID.MiddleTip,new Vector3(.034f,.02f,.02f));
            var display=new HandPoseFrame(raw.RightJoints.Length);new IdentityHandPoseProcessor().Process(raw,display);
            var t=new Vector3[5];var w=new float[5];for(var f=0;f<5;f++)t[f]=raw.FindJoint(false,DisplayFingerSolver.Tip((CorrectionFinger)f)).Pose.position;
            var settings=new RuleHandPoseSettings{method=HandCorrectionMethod.FullRules,nonpenetration=false};
            var solver=new RuleHandPoseSolver();solver.Solve(raw,display,false,t,w,settings,.02f);
            Assert.That(solver.Metrics[1].interference,Is.LessThan(solver.Metrics[1].rawInterference));
            Assert.That(solver.Metrics[2].interference,Is.LessThan(solver.Metrics[2].rawInterference));
            Assert.That(solver.MaximumBoneError,Is.LessThan(1e-6));
        }
    }
}
