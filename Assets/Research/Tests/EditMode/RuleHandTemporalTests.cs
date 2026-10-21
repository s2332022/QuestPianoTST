using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RuleHandTemporalTests
    {
        GameObject root,origin;
        RuleHandPoseProcessor processor;
        MidiTipCorrectionSettings common;
        RuleHandPoseSettings settings;
        HandPoseFrame raw,display;
        [SetUp] public void Setup()
        {
            root=new GameObject("Temporal keyboard",typeof(VirtualPianoKeyboard));origin=new GameObject("Temporal origin");
            var k=root.GetComponent<VirtualPianoKeyboard>();typeof(VirtualPianoKeyboard).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(k,null);
            common=new MidiTipCorrectionSettings{enabled=true,strength=1,maxCorrectionMeters=.03f,belowSurfaceToleranceMeters=0,aboveSurfaceToleranceMeters=0};
            settings=new RuleHandPoseSettings{method=HandCorrectionMethod.JointConstraints};
            processor=new RuleHandPoseProcessor(common,settings,k,()=>true,()=>origin.transform);
            raw=RuleHandPoseSolverTests.Frame();display=new HandPoseFrame(raw.RightJoints.Length);
        }
        [TearDown] public void Teardown(){Object.DestroyImmediate(root);Object.DestroyImmediate(origin);}
        void Midi(MidiEventType type,double time,int note=60,int velocity=100)=>processor.ObserveMidi("BLE",new MidiMessage(time,42,"test",type,1,note,velocity,-1,-1),time,time,2);
        void Pose(double time){raw.AbsoluteTimeSeconds=time;raw.CallbackIndex++;processor.Process(raw,display);}
        void Engage(){Midi(MidiEventType.NoteOn,10);for(var i=0;i<=20;i++)Pose(10+i/72d);}
        [Test] public void ReleaseActualJointOffsetsNeverGrowWhileRawFingerIsLifted()
        {
            Engage();Assert.That(processor.Solver.Metrics[1].correctedTip,Is.Not.EqualTo(processor.Solver.Metrics[1].rawTip));
            var last=new float[4];
            for(var j=0;j<4;j++){var id=(XRHandJointID)((int)RuleHandPoseSolver.First(1)+j);last[j]=Vector3.Distance(raw.FindJoint(false,id).Pose.position,display.FindJoint(false,id).Pose.position);}
            Midi(MidiEventType.NoteOff,10.29);
            for(var frame=1;frame<=10;frame++)
            {
                for(var j=0;j<raw.RightJoints.Length;j++)
                {var v=raw.RightJoints[j];v.Pose.position=Quaternion.AngleAxis(1,Vector3.forward)*v.Pose.position+Vector3.up*.005f;raw.RightJoints[j]=v;}
                Pose(10.29+frame/72d);
                for(var j=0;j<4;j++)
                {
                    var id=(XRHandJointID)((int)RuleHandPoseSolver.First(1)+j);
                    var delta=Vector3.Distance(raw.FindJoint(false,id).Pose.position,display.FindJoint(false,id).Pose.position);
                    Assert.That(delta,Is.LessThanOrEqualTo(last[j]+1e-6f));last[j]=delta;
                }
                Assert.That(processor.Solver.MaximumBoneError,Is.LessThan(1e-6));
            }
            Assert.That(processor.Contacts[1].state,Is.EqualTo(TipCorrectionState.Inactive));
            Assert.That(last[3],Is.LessThan(1e-6));
        }
        [Test] public void FutureMidiIsNotAppliedToEarlierRawAndBeforeRenderDoesNotAdvanceState()
        {
            Midi(MidiEventType.NoteOn,10);Pose(9.99);
            Assert.That(display.FindJoint(false,XRHandJointID.IndexTip).Pose,Is.EqualTo(raw.FindJoint(false,XRHandJointID.IndexTip).Pose));
            Pose(10.02);var gain=processor.Contacts[1].gain;
            raw.UpdateType=XRHandSubsystem.UpdateType.BeforeRender;Pose(10.5);
            Assert.That(processor.Contacts[1].gain,Is.EqualTo(gain));
            Assert.That(processor.Contacts[1].state,Is.EqualTo(TipCorrectionState.Engaging));
        }
        [Test] public void DisconnectAndVelocityZeroReleaseWithoutUsingSenderClock()
        {
            Engage();Midi(MidiEventType.NoteOn,10.29,velocity:0);
            Assert.That(processor.HeldCount,Is.Zero);Assert.That(processor.Contacts[1].state,Is.EqualTo(TipCorrectionState.Releasing));
            processor.DisconnectSource("BLE",10.3);Pose(10.31);
            Assert.That(DisplayFingerSolver.Finite(display.FindJoint(false,XRHandJointID.IndexTip).Pose.position),Is.True);
        }
        [Test] public void NonmonotonicPoseAndTrackingLossRequireFreshAttribution()
        {
            Engage();Pose(10.1);Assert.That(processor.Reason,Is.EqualTo("nonmonotonic_pose"));
            Assert.That(display.FindJoint(false,XRHandJointID.IndexTip).Pose,Is.EqualTo(raw.FindJoint(false,XRHandJointID.IndexTip).Pose));
            Pose(10.3);Assert.That(processor.Reason,Is.EqualTo("await_all_notes_off"));
            Midi(MidiEventType.NoteOff,10.31);Midi(MidiEventType.NoteOn,10.32);raw.RightTracked=false;Pose(10.33);
            Assert.That(processor.Reason,Is.EqualTo("tracking_invalid"));
        }
        [Test] public void ZeroMaximumAndInvalidConfigurationReturnRaw()
        {
            common.maxCorrectionMeters=0;Engage();
            Assert.That(display.FindJoint(false,XRHandJointID.IndexTip).Pose.position,Is.EqualTo(raw.FindJoint(false,XRHandJointID.IndexTip).Pose.position));
            settings.iterations=999;Pose(10.29);Assert.That(processor.Reason,Is.EqualTo("invalid_settings"));
        }
    }
}
