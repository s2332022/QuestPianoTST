using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RuleHandAssignmentTests
    {
        GameObject root,origin;
        VirtualPianoKeyboard keyboard;
        MidiTipCorrectionSettings common;
        RuleHandPoseSettings rules;
        RuleHandPoseProcessor processor;
        HandPoseFrame raw,display;
        [SetUp] public void Setup()
        {
            root=new GameObject("Assignment keyboard",typeof(VirtualPianoKeyboard));origin=new GameObject("Assignment origin");keyboard=root.GetComponent<VirtualPianoKeyboard>();
            typeof(VirtualPianoKeyboard).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(keyboard,null);
            common=new MidiTipCorrectionSettings{enabled=true,strength=1,maxCorrectionMeters=.02f};rules=new RuleHandPoseSettings{method=HandCorrectionMethod.JointConstraints,manualNoteMap=true};
            processor=new RuleHandPoseProcessor(common,rules,keyboard,()=>true,()=>origin.transform);raw=RuleHandPoseSolverTests.Frame();display=new HandPoseFrame(raw.RightJoints.Length);
        }
        [TearDown] public void Teardown(){Object.DestroyImmediate(root);Object.DestroyImmediate(origin);}
        void Midi(MidiEventType type,int note,double t=10,string source="BLE",long generation=2)=>processor.ObserveMidi(source,new MidiMessage(-99,42,"assignment",type,1,note,100,-1,-1),t,t,generation);
        void Pose(double t){raw.AbsoluteTimeSeconds=t;processor.Process(raw,display);}
        [TestCase("BLE")] [TestCase("UDP")] public void ExplicitFiveNoteChordUsesAllFiveManualAssignments(string source)
        {
            int[] notes={60,62,64,65,67};for(var f=0;f<5;f++){rules.noteToFinger[notes[f]]=f;Midi(MidiEventType.NoteOn,notes[f],10+f*.001,source);}
            for(var i=0;i<20;i++)Pose(10.01+i/72d);
            Assert.That(processor.HeldCount,Is.EqualTo(5));
            for(var f=0;f<5;f++){Assert.That(processor.Contacts[f].note,Is.EqualTo(notes[f]));Assert.That(processor.Contacts[f].state,Is.EqualTo(TipCorrectionState.Active));Assert.That(processor.Solver.Metrics[f].valid,Is.True);}
            Midi(MidiEventType.NoteOff,64,10.3,source);Assert.That(processor.Contacts[2].state,Is.EqualTo(TipCorrectionState.Releasing));
            Assert.That(processor.Contacts[1].state,Is.EqualTo(TipCorrectionState.Active));
        }
        [TestCase(false)] [TestCase(true)] public void UnmappedOrConflictingChordReturnsRawUntilAllNotesAreReleased(bool conflict)
        {
            rules.noteToFinger[60]=1;rules.noteToFinger[64]=conflict?1:-1;
            Midi(MidiEventType.NoteOn,60);Midi(MidiEventType.NoteOn,64,10.01);
            Assert.That(processor.Reason,Is.EqualTo(conflict?"finger_assignment_conflict":"unmapped_note"));
            Pose(10.1);Assert.That(display.FindJoint(false,XRHandJointID.IndexTip).Pose,Is.EqualTo(raw.FindJoint(false,XRHandJointID.IndexTip).Pose));
            Midi(MidiEventType.NoteOff,60,10.11);Pose(10.12);Assert.That(processor.Reason,Is.EqualTo("await_all_notes_off"));
            Midi(MidiEventType.NoteOff,64,10.13);Midi(MidiEventType.NoteOn,60,10.14);Pose(10.15);
            Assert.That(processor.Contacts[1].state,Is.EqualTo(TipCorrectionState.Engaging));
        }
        [Test] public void MappingChangeAndStaleGenerationDoNotReattributeHeldNote()
        {
            rules.noteToFinger[60]=1;Midi(MidiEventType.NoteOn,60);Pose(10.01);rules.noteToFinger[60]=2;Pose(10.02);
            Assert.That(processor.Reason,Is.EqualTo("assignment_changed"));
            Midi(MidiEventType.NoteOff,60,10.03,generation:1);Assert.That(processor.HeldCount,Is.EqualTo(1));
            Midi(MidiEventType.NoteOff,60,10.04);Assert.That(processor.HeldCount,Is.Zero);
        }
        [Test] public void TimedOutButStillHeldNoteCannotShareFingerWithAnotherNote()
        {
            common.maxHoldSeconds=.1f;common.releaseSeconds=.02f;rules.noteToFinger[60]=rules.noteToFinger[64]=1;
            Midi(MidiEventType.NoteOn,60);Pose(10.01);Pose(10.12);Pose(10.15);
            Assert.That(processor.Contacts[1].state,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOn,64,10.16);Assert.That(processor.Reason,Is.EqualTo("finger_assignment_conflict"));
        }
        [Test] public void LeftHandAndRawAblationDoNotChangeOppositeHand()
        {
            common.leftHand=true;rules.noteToFinger[60]=1;Midi(MidiEventType.NoteOn,60);
            for(var i=0;i<20;i++)Pose(10+i/72d);
            Assert.That(Vector3.Distance(raw.FindJoint(true,XRHandJointID.IndexTip).Pose.position,display.FindJoint(true,XRHandJointID.IndexTip).Pose.position),Is.GreaterThan(.0001f));
            for(var i=0;i<raw.RightJoints.Length;i++)Assert.That(display.RightJoints[i].Pose,Is.EqualTo(raw.RightJoints[i].Pose));
            rules.method=HandCorrectionMethod.Raw;Pose(10.29);
            for(var i=0;i<raw.RightJoints.Length;i++)Assert.That(display.LeftJoints[i].Pose,Is.EqualTo(raw.LeftJoints[i].Pose));
        }
        [Test] public void LegacyRoutingIsPoseIdenticalToOriginalProcessor()
        {
            var a=new MidiTipCorrectionProcessor(common,keyboard,()=>true,origin:()=>origin.transform);
            var reference=new MidiTipCorrectionProcessor(common,keyboard,()=>true,origin:()=>origin.transform);
            rules.method=HandCorrectionMethod.Legacy;var router=new HandCorrectionRouter(a,processor,rules);
            var message=new MidiMessage(-999,77,"legacy",MidiEventType.NoteOn,1,60,100,-1,-1);
            router.ObserveMidi("BLE",message,10,10);reference.ObserveMidi("BLE",message,10,10);
            var expected=new HandPoseFrame(raw.RightJoints.Length);
            for(var frame=0;frame<16;frame++)
            {
                raw.AbsoluteTimeSeconds=10+frame/72d;router.Process(raw,display);reference.Process(raw,expected);
                for(var i=0;i<raw.RightJoints.Length;i++)
                {Assert.That(display.RightJoints[i].Pose,Is.EqualTo(expected.RightJoints[i].Pose));Assert.That(display.LeftJoints[i].Pose,Is.EqualTo(expected.LeftJoints[i].Pose));}
            }
            rules.method=HandCorrectionMethod.JointConstraints;router.Process(raw,display);
            Assert.That(display.FindJoint(false,XRHandJointID.IndexTip).Pose,Is.EqualTo(raw.FindJoint(false,XRHandJointID.IndexTip).Pose));
        }
        [Test] public void FirstNoteAfterMethodChangeBeforeNextPoseIsNotLost()
        {
            rules.method=HandCorrectionMethod.Legacy;rules.noteToFinger[60]=1;
            var legacy=new MidiTipCorrectionProcessor(common,keyboard,()=>true,origin:()=>origin.transform);
            var router=new HandCorrectionRouter(legacy,processor,rules);rules.method=HandCorrectionMethod.JointConstraints;
            router.ObserveMidi("BLE",new MidiMessage(10,1,"mode change",MidiEventType.NoteOn,1,60,100,-1,-1),10,10);
            for(var i=0;i<20;i++){raw.AbsoluteTimeSeconds=10+i/72d;router.Process(raw,display);}
            Assert.That(processor.Contacts[1].state,Is.EqualTo(TipCorrectionState.Active));
            Assert.That(Vector3.Distance(raw.FindJoint(false,XRHandJointID.IndexTip).Pose.position,display.FindJoint(false,XRHandJointID.IndexTip).Pose.position),Is.GreaterThan(.0001f));
        }
    }
}
