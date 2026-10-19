using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class MidiTipCorrectionTests
    {
        GameObject m_Root, m_Origin;
        VirtualPianoKeyboard m_Keyboard;
        MidiTipCorrectionSettings m_Settings;
        MidiTipCorrectionProcessor m_Processor;
        HandPoseFrame m_Raw, m_Display;
        bool m_Calibrated;
        Vector3 m_Tip;
        [SetUp] public void SetUp()
        {
            m_Root = new GameObject("Correction test keyboard", typeof(VirtualPianoKeyboard));
            m_Origin = new GameObject("Correction test tracking origin");
            m_Keyboard = m_Root.GetComponent<VirtualPianoKeyboard>();
            typeof(VirtualPianoKeyboard).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(m_Keyboard,null);
            m_Settings = new MidiTipCorrectionSettings { enabled = true, strength = 1, maxCorrectionMeters = .02f };
            m_Calibrated = true;
            m_Processor = new MidiTipCorrectionProcessor(m_Settings,m_Keyboard,()=>m_Calibrated,origin:()=>m_Origin.transform);
            m_Keyboard.TryGetRestKeyBounds(60,out var bounds);
            m_Tip = new Vector3(bounds.max.x+.01f,.005f,.04f);
            m_Raw = Frame(m_Tip); m_Display = new HandPoseFrame(m_Raw.RightJoints.Length);
        }
        [TearDown] public void TearDown()
        { UnityEngine.Object.DestroyImmediate(m_Root); UnityEngine.Object.DestroyImmediate(m_Origin); }
        public static HandPoseFrame Frame(Vector3 tip)
        {
            var count=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;
            var f=new HandPoseFrame(count){RightTracked=true,LeftTracked=true,UpdateType=XRHandSubsystem.UpdateType.Dynamic};
            for(var i=0;i<count;++i)
            {
                var id=(XRHandJointID)((int)XRHandJointID.BeginMarker+i);
                f.RightJoints[i]=f.LeftJoints[i]=new HandJointPose{JointId=id,PoseValid=true,TrackingState=XRHandJointTrackingState.Pose,Pose=new Pose(new Vector3(.1f,.2f,.3f),Quaternion.identity)};
            }
            Set(f,XRHandJointID.IndexProximal,tip+new Vector3(0,.05f,-.04f));
            Set(f,XRHandJointID.IndexIntermediate,tip+new Vector3(0,.035f,-.022f));
            Set(f,XRHandJointID.IndexDistal,tip+new Vector3(0,.015f,-.008f));
            Set(f,XRHandJointID.IndexTip,tip);
            return f;
        }
        static void Set(HandPoseFrame f,XRHandJointID id,Vector3 p)
        {
            for(var i=0;i<f.RightJoints.Length;++i)if(f.RightJoints[i].JointId==id)
            {var j=f.RightJoints[i];j.Pose.position=p;f.RightJoints[i]=j;}
        }
        void Midi(MidiEventType type,int note=60,double time=10,string source="BLE",int velocity=100,int channel=1)
        {m_Processor.ObserveMidi(source,new MidiMessage(-900,42,"test",type,channel,note,velocity,-1,-1),time,time);}
        void Pose(double time)
        { m_Raw.AbsoluteTimeSeconds=time;m_Raw.CallbackIndex++;m_Processor.Process(m_Raw,m_Display); }
        void Engage() { Midi(MidiEventType.NoteOn);Pose(10);Pose(10.1); }
        [TestCase(60,false,0f)] [TestCase(61,true,.0245f)] [TestCase(72,false,0f)]
        public void NoteLookupUsesRestGeometryAndWhiteBlackHeight(int note,bool black,float height)
        {
            Assert.That(m_Keyboard.TryGetKeySurface(note,out var surface),Is.True);
            Assert.That(surface.Note,Is.EqualTo(note));Assert.That(surface.IsBlack,Is.EqualTo(black));
            Assert.That(surface.Height,Is.EqualTo(height).Within(1e-6));
            m_Keyboard.ApplyMidi(new MidiMessage(10,1,"test",MidiEventType.NoteOn,1,note,100,-1,-1));
            m_Keyboard.TryGetKeySurface(note,out var pressed);
            Assert.That(pressed.Height,Is.EqualTo(height).Within(1e-6),"8mm animation must not become physical ground truth");
            Assert.That(m_Keyboard.TryGetKeySurface(200,out _),Is.False);
        }
        [Test] public void SurfaceInteriorAndToleranceNeverSnapToCenter()
        {
            m_Keyboard.TryGetKeySurface(60,out var s);
            var p=new Vector3(.007f,.005f,.03f);
            Assert.That(s.Target(p,Vector3.zero,.002f,.003f,.008f),Is.EqualTo(p));
            m_Keyboard.TryGetRestKeyBounds(60,out var b);p.x=b.min.x-.001f;
            Assert.That(s.Target(p,Vector3.zero,.002f,.003f,.008f),Is.EqualTo(p));
        }
        [Test] public void OutsideProjectionUsesNearestBoundaryAndPreservesDepth()
        {
            m_Keyboard.TryGetKeySurface(60,out var s);m_Keyboard.TryGetRestKeyBounds(60,out var b);
            var p=new Vector3(b.min.x-.01f,.005f,.02f);var target=s.Target(p,Vector3.zero,.002f,.003f,.008f);
            Assert.That(target.x,Is.EqualTo(b.min.x).Within(1e-6));Assert.That(target.z,Is.EqualTo(p.z));
            Assert.That(target.y,Is.EqualTo(p.y));
        }
        [Test] public void WhiteKeyExcludesBlackFootprintAndNormalOffsetIsExplicit()
        {
            m_Keyboard.TryGetKeySurface(60,out var white);m_Keyboard.TryGetRestKeyBounds(61,out var black);
            m_Keyboard.TryGetRestKeyBounds(60,out var w);
            var p=new Vector3((black.min.x+w.max.x)*.5f,0,black.center.z);
            Assert.That(white.PlanarDistance(p),Is.GreaterThan(0));
            var target=white.Target(new Vector3(.01f,.04f,.02f),new Vector3(0,-.01f,0),0,.003f,.008f);
            Assert.That(target.y,Is.EqualTo(.018f).Within(1e-6));
        }
        [Test] public void CorrectsDisplayPreservesRawOtherFingersAndBoneLengths()
        {
            var before=(HandJointPose[])m_Raw.RightJoints.Clone();Engage();
            Assert.That(m_Processor.Latest.AfterDistance,Is.LessThan(m_Processor.Latest.BeforeDistance));
            Assert.That(m_Processor.Latest.AfterDistance,Is.LessThanOrEqualTo(m_Settings.planarToleranceMeters));
            Assert.That(m_Processor.Latest.DeltaKeyboard.magnitude,Is.GreaterThan(.005f));
            for(var i=0;i<before.Length;++i)
            {
                Assert.That(m_Raw.RightJoints[i].Pose.position,Is.EqualTo(before[i].Pose.position));
                Assert.That(m_Raw.RightJoints[i].Pose.rotation,Is.EqualTo(before[i].Pose.rotation));
                var id=before[i].JointId;
                if(id<XRHandJointID.IndexProximal||id>XRHandJointID.IndexTip)
                    Assert.That(m_Display.RightJoints[i].Pose,Is.EqualTo(before[i].Pose));
                Assert.That(m_Display.LeftJoints[i].Pose,Is.EqualTo(m_Raw.LeftJoints[i].Pose));
            }
            for(var id=XRHandJointID.IndexProximal;id<XRHandJointID.IndexTip;++id)
            {
                float Length(HandPoseFrame f)=>Vector3.Distance(f.FindJoint(false,id).Pose.position,f.FindJoint(false,id+1).Pose.position);
                Assert.That(Length(m_Display),Is.EqualTo(Length(m_Raw)).Within(1e-5));
            }
        }
        [Test] public void StrengthCapAndDisabledDisplayAreRespected()
        {
            m_Settings.maxCorrectionMeters=.002f;Engage();
            Assert.That(m_Processor.Latest.Capped,Is.True);
            Assert.That(m_Processor.Latest.DeltaKeyboard.magnitude,Is.LessThanOrEqualTo(.002001f));
            m_Settings.enabled=false;Pose(10.11);
            Assert.That(m_Display.FindJoint(false,XRHandJointID.IndexTip).Pose,Is.EqualTo(m_Raw.FindJoint(false,XRHandJointID.IndexTip).Pose));
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("disabled"));
        }
        [Test] public void NoteOffSmoothlyReleasesEvenWhenCorrectionWasCapped()
        {
            m_Settings.maxCorrectionMeters=.002f;Engage();var full=m_Processor.Latest.DeltaKeyboard.magnitude;
            Midi(MidiEventType.NoteOff,time:10.11);Pose(10.17);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Releasing));
            Assert.That(m_Processor.Latest.DeltaKeyboard.magnitude,Is.GreaterThan(0).And.LessThan(full));
            Pose(10.24);Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Assert.That(m_Processor.Latest.DeltaKeyboard,Is.EqualTo(Vector3.zero));
        }
        [TestCase("tracking")] [TestCase("calibration")] [TestCase("scaled")]
        public void InvalidPrerequisiteImmediatelyReturnsToRaw(string failure)
        {
            Engage();
            if(failure=="tracking")m_Raw.RightTracked=false;
            if(failure=="calibration")m_Calibrated=false;
            if(failure=="scaled")m_Origin.transform.localScale=Vector3.one*2;
            Pose(10.11);Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Assert.That(m_Display.FindJoint(false,XRHandJointID.IndexTip).Pose,Is.EqualTo(m_Raw.FindJoint(false,XRHandJointID.IndexTip).Pose));
        }
        [Test] public void DisconnectAndTimeoutReleaseWithoutResumingHeldNotes()
        {
            Engage();m_Processor.DisconnectSource("BLE",10.11);Pose(10.17);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Releasing));
            Pose(10.24);Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOn,time:11);Pose(11);Pose(11.1);
            m_Processor.Watchdog(11.5);Pose(11.5);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
        }
        [Test] public void ChordAndCrossSourceDuplicateLatchUntilAllReleased()
        {
            Engage();Midi(MidiEventType.NoteOn,61,10.11);Pose(10.12);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOff,61,10.13);Pose(10.14);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOff,60,10.15);Midi(MidiEventType.NoteOn,60,10.16);Pose(10.24);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Active));
            Midi(MidiEventType.NoteOn,60,10.25,"UDP");Pose(10.26);
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("ambiguous_notes"));
        }
        [Test] public void VelocityZeroReleasesAndIndependentSenderClockIsNeverUsed()
        {
            Engage();Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Active));
            Midi(MidiEventType.NoteOn,time:10.11,velocity:0);Pose(10.17);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Releasing));
            Assert.That(m_Processor.Latest.ReceivedTime,Is.EqualTo(10.11));
        }
        [Test] public void FramesBeforeUnityObservationCannotBeCorrectedRetrospectively()
        {
            m_Processor.ObserveMidi("UDP",new MidiMessage(-999,42,"test",MidiEventType.NoteOn,1,60,100,-1,-1),9.9,10.1);
            Pose(10);Assert.That(m_Processor.Latest.DeltaKeyboard,Is.EqualTo(Vector3.zero));
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("before_midi_observation"));
        }
        [Test] public void SelectionChangeRequiresFreshOnsetAndAutomaticModeDoesNotGuess()
        {
            Engage();m_Settings.leftHand=true;Pose(10.11);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("selection_changed"));
            m_Settings.selectionMode=StrikingFingerMode.AutomaticReserved;Pose(10.12);
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("automatic_unavailable"));
        }
        [TestCase(false,CorrectionFinger.Thumb)] [TestCase(true,CorrectionFinger.Thumb)]
        [TestCase(false,CorrectionFinger.Index)] [TestCase(true,CorrectionFinger.Index)]
        [TestCase(false,CorrectionFinger.Middle)] [TestCase(true,CorrectionFinger.Middle)]
        [TestCase(false,CorrectionFinger.Ring)] [TestCase(true,CorrectionFinger.Ring)]
        [TestCase(false,CorrectionFinger.Little)] [TestCase(true,CorrectionFinger.Little)]
        public void EveryManualFingerAndHandCanDriveTheSelectedTip(bool left,CorrectionFinger finger)
        {
            m_Settings.leftHand=left;m_Settings.finger=finger;
            var joints=left?m_Raw.LeftJoints:m_Raw.RightJoints;
            var tip=DisplayFingerSolver.Tip(finger);var count=finger==CorrectionFinger.Thumb?3:4;
            var positions=count==3?new[]{m_Tip+new Vector3(0,.05f,-.04f),m_Tip+new Vector3(0,.025f,-.015f),m_Tip}:
                new[]{m_Tip+new Vector3(0,.05f,-.04f),m_Tip+new Vector3(0,.035f,-.022f),m_Tip+new Vector3(0,.015f,-.008f),m_Tip};
            for(var i=0;i<count;++i)
                for(var j=0;j<joints.Length;++j)if(joints[j].JointId==tip-count+1+i)
                {var sample=joints[j];sample.Pose.position=positions[i];joints[j]=sample;}
            Engage();
            Assert.That(m_Processor.Latest.DeltaKeyboard.magnitude,Is.GreaterThan(.003f));
            Assert.That(m_Processor.Latest.Left,Is.EqualTo(left));Assert.That(m_Processor.Latest.Finger,Is.EqualTo(finger));
            Assert.That(m_Raw.FindJoint(left,tip).Pose.position,Is.EqualTo(m_Tip));
        }
        [Test] public void NormalConstraintUsesBlackTopAndAsymmetricBand()
        {
            m_Keyboard.TryGetKeySurface(61,out var black);
            var p=new Vector3(.035f,.1f,.1f);
            var target=black.Target(p,Vector3.zero,.002f,.003f,.008f);
            Assert.That(target.y,Is.EqualTo(.0245f+.008f).Within(1e-6));
            p.y=-.01f;target=black.Target(p,Vector3.zero,.002f,.003f,.008f);
            Assert.That(target.y,Is.EqualTo(.0245f-.003f).Within(1e-6));
        }
        [Test] public void ZeroStrengthAndSessionStopLeaveNoPersistentCorrection()
        {
            m_Settings.strength=0;Engage();Assert.That(m_Processor.Latest.DeltaKeyboard,Is.EqualTo(Vector3.zero));
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("strength_zero"));
            m_Settings.strength=1;Pose(10.11);Assert.That(m_Processor.Latest.DeltaKeyboard.magnitude,Is.GreaterThan(0));
            m_Processor.EndSession(10.12);Pose(10.18);Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Releasing));
            Pose(10.25);Assert.That(m_Processor.Latest.DeltaKeyboard,Is.EqualTo(Vector3.zero));
        }
        [Test] public void MissingNoteOffTimesOutByElapsedTimeAndRequiresReleaseBeforeReengagement()
        {
            m_Settings.maxHoldSeconds=.15f;Engage();Pose(10.16);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Releasing));
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("note_timeout"));
            Pose(10.29);Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOn,61,10.3);Pose(10.31);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
        }
        [Test] public void UnsupportedRangeAndInvalidSettingsFailClosed()
        {
            Midi(MidiEventType.NoteOn,59);Pose(10.1);Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOff,59,10.11);Midi(MidiEventType.NoteOn,60,10.12);
            m_Settings.maxCorrectionMeters=float.NaN;Pose(10.13);
            Assert.That(m_Processor.Latest.Reason,Is.EqualTo("invalid_settings"));
            Assert.That(m_Processor.Latest.DeltaKeyboard,Is.EqualTo(Vector3.zero));
        }
        [Test] public void SnapshotOwnershipNeverEngagesAndBlocksWrongAttributionToAnotherSource()
        {
            var bits=new byte[256];bits[60>>3]=(byte)(1<<(60&7));
            m_Processor.SynchronizeUdpOwnership(bits,10);Pose(10.01);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOn,61,10.02);Pose(10.03);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Inactive));
            Midi(MidiEventType.NoteOff,61,10.04);Midi(MidiEventType.NoteOff,60,10.04,"UDP");
            Midi(MidiEventType.NoteOn,60,10.05);Pose(10.13);
            Assert.That(m_Processor.State,Is.EqualTo(TipCorrectionState.Active));
        }
        [Test] public void TransformCompositionProducesKeyboardMetresUnderRotationAndTranslation()
        {
            m_Root.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(0,40,0));
            m_Origin.transform.SetPositionAndRotation(m_Root.transform.position,m_Root.transform.rotation);
            Engage();Assert.That(Vector3.Distance(m_Processor.Latest.RawKeyboard,m_Tip),Is.LessThan(1e-5));
        }
    }
}
