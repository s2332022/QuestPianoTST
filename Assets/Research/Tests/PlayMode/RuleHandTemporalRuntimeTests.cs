using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RuleHandTemporalRuntimeTests
    {
        [UnityTest] public IEnumerator MovingRawReleaseRemainsBoundedAcrossRuntimeFrames()
        {
            var keyboardGo=new GameObject("Temporal runtime keyboard",typeof(VirtualPianoKeyboard));
            var origin=new GameObject("Temporal runtime origin");
            try
            {
                var common=new MidiTipCorrectionSettings{enabled=true,strength=1,maxCorrectionMeters=.02f,aboveSurfaceToleranceMeters=0,belowSurfaceToleranceMeters=0};
                var rules=new RuleHandPoseSettings{method=HandCorrectionMethod.ContactOnly};
                var p=new RuleHandPoseProcessor(common,rules,keyboardGo.GetComponent<VirtualPianoKeyboard>(),()=>true,()=>origin.transform);
                var n=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;var raw=new HandPoseFrame(n){RightTracked=true,UpdateType=XRHandSubsystem.UpdateType.Dynamic};
                for(var i=0;i<n;i++)raw.RightJoints[i]=new HandJointPose{JointId=(XRHandJointID)((int)XRHandJointID.BeginMarker+i),PoseValid=true,Pose=new Pose(new Vector3(.02f,.07f,-.08f),Quaternion.identity)};
                void Set(XRHandJointID id,Vector3 position){for(var i=0;i<n;i++)if(raw.RightJoints[i].JointId==id){var j=raw.RightJoints[i];j.Pose.position=position;raw.RightJoints[i]=j;}}
                Set(XRHandJointID.IndexMetacarpal,new Vector3(.02f,.06f,-.06f));Set(XRHandJointID.IndexProximal,new Vector3(.02f,.06f,-.03f));
                Set(XRHandJointID.IndexIntermediate,new Vector3(.02f,.05f,-.005f));Set(XRHandJointID.IndexDistal,new Vector3(.02f,.035f,.01f));Set(XRHandJointID.IndexTip,new Vector3(.02f,.02f,.02f));
                var display=new HandPoseFrame(n);
                p.ObserveMidi("BLE",new MidiMessage(10,1,"runtime",MidiEventType.NoteOn,1,60,100,-1,-1),10,10);
                for(var f=0;f<20;f++){raw.AbsoluteTimeSeconds=10+f/72d;p.Process(raw,display);yield return null;}
                var previous=Vector3.Distance(raw.FindJoint(false,XRHandJointID.IndexTip).Pose.position,display.FindJoint(false,XRHandJointID.IndexTip).Pose.position);
                Assert.That(previous,Is.GreaterThan(.0001));
                p.ObserveMidi("BLE",new MidiMessage(10.28,2,"runtime",MidiEventType.NoteOff,1,60,0,-1,-1),10.28,10.28);
                for(var f=1;f<=10;f++)
                {
                    for(var i=0;i<n;i++){var j=raw.RightJoints[i];j.Pose.position+=Vector3.up*.005f;raw.RightJoints[i]=j;}
                    raw.AbsoluteTimeSeconds=10.28+f/72d;p.Process(raw,display);
                    var delta=Vector3.Distance(raw.FindJoint(false,XRHandJointID.IndexTip).Pose.position,display.FindJoint(false,XRHandJointID.IndexTip).Pose.position);
                    Assert.That(delta,Is.LessThanOrEqualTo(previous+1e-6f));previous=delta;yield return null;
                }
                Assert.That(p.Contacts[1].state,Is.EqualTo(TipCorrectionState.Inactive));
            }
            finally{Object.DestroyImmediate(keyboardGo);Object.DestroyImmediate(origin);}
        }
    }
}
