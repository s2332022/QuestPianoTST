using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Hands;
using Object=UnityEngine.Object;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RuleHandIntegrationRuntimeTests
    {
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Origin(Transform t)=>typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{t});
        static HandPoseFrame Frame()
        {
            var n=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;
            var r=new HandPoseFrame(n){RightTracked=true,LeftTracked=true,UpdateType=XRHandSubsystem.UpdateType.Dynamic};
            for(var i=0;i<n;i++)r.LeftJoints[i]=r.RightJoints[i]=new HandJointPose{JointId=(XRHandJointID)((int)XRHandJointID.BeginMarker+i),PoseValid=true,Pose=Pose.identity};
            void Set(XRHandJointID id,Vector3 p){for(var i=0;i<n;i++)if(r.RightJoints[i].JointId==id){var j=r.RightJoints[i];j.Pose.position=p;r.LeftJoints[i]=r.RightJoints[i]=j;}}
            Set(XRHandJointID.Wrist,new Vector3(.04f,.07f,-.08f));
            for(var f=0;f<5;f++)
            {
                var first=RuleHandPoseSolver.First(f);var x=f*.022f;
                if(f>0)Set((XRHandJointID)((int)first-1),new Vector3(x,.06f,-.065f));
                Set(first,new Vector3(x,.06f,-.03f));Set((XRHandJointID)((int)first+1),new Vector3(x,.05f,-.005f));
                Set((XRHandJointID)((int)first+2),new Vector3(x,.035f,.01f));Set((XRHandJointID)((int)first+3),new Vector3(x,.02f,.02f));
            }
            return r;
        }
        [UnityTest] public IEnumerator ActualBleRouteWritesSeparateNewMetricsAndDrivesDisplayMesh()
        {
            var previous=ResearchServices.TrackingOrigin;var origin=new GameObject("New hand test origin");Origin(origin.transform);
            var runtime=new GameObject("New hand runtime",typeof(VirtualPianoKeyboard),typeof(XRHandPoseProvider),typeof(PianoCalibrationManager),typeof(BleMidiInput));
            var config=runtime.AddComponent<DistributedSettings>();config.questReceivePort=0;
            var client=runtime.AddComponent<DistributedQuestClient>();var calibration=runtime.GetComponent<PianoCalibrationManager>();
            typeof(PianoCalibrationManager).GetProperty("CurrentAppliedCalibration").GetSetMethod(true).Invoke(calibration,new object[]{new PianoCalibrationData{valid=true,calibrationId="new_rule_runtime"}});
            string sessionPath=null;GameObject handObject=null;
            try
            {
                yield return null;client.StopNetwork();
                client.CorrectionSettings.enabled=true;client.CorrectionSettings.strength=1;client.CorrectionSettings.aboveSurfaceToleranceMeters=0;
                client.RuleSettings.method=HandCorrectionMethod.JointConstraints;client.RuleSettings.manualNoteMap=true;
                client.RuleSettings.noteToFinger[60]=1;client.RuleSettings.noteToFinger[64]=2;
                var raw=Frame();var display=new HandPoseFrame(raw.RightJoints.Length);
                raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;client.PoseCorrection.Process(raw,display);
                Assert.That(client.BeginLocalResearchSession(),Is.True);sessionPath=client.ResearchSessionPath;
                var prefab=Resources.Load<GameObject>("CorrectionHands/RightDisplayHand");Assert.That(prefab,Is.Not.Null);
                handObject=Object.Instantiate(prefab);var hand=handObject.GetComponent<DisplayHandSkeleton>();hand.Initialize(null,null);
                var ble=runtime.GetComponent<BleMidiInput>();var session=(BleMidiSession)typeof(BleMidiInput).GetField("m_Session",Private).GetValue(ble);
                session.Begin(7,"new rule injected BLE");session.Enqueue(new BleMidiSession.Sample(7,0,0,0x90,60,100));session.Enqueue(new BleMidiSession.Sample(7,0,0,0x90,64,100));session.Flush();
                Assert.That(client.PoseCorrection.New.HeldCount,Is.EqualTo(2));
                var before=(HandJointPose[])raw.RightJoints.Clone();
                for(var frame=0;frame<25;frame++)
                {
                    raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;raw.CallbackIndex++;
                    typeof(DistributedQuestClient).GetMethod("OnRawFrame",Private).Invoke(client,new object[]{raw});
                    client.PoseCorrection.Process(raw,display);hand.Apply(display);
                    yield return new WaitForSecondsRealtime(.014f);
                }
                Assert.That(client.PoseCorrection.New.Solver.Metrics[1].valid,Is.True);
                Assert.That(client.PoseCorrection.New.Solver.Metrics[2].valid,Is.True);
                var index=hand.Joints.Single(x=>x.xrHandJointID==XRHandJointID.IndexTip).jointTransform;
                Assert.That(Vector3.Distance(index.position,origin.transform.TransformPoint(display.FindJoint(false,XRHandJointID.IndexTip).Pose.position)),Is.LessThan(1e-5));
                Assert.That(Vector3.Distance(index.position,origin.transform.TransformPoint(raw.FindJoint(false,XRHandJointID.IndexTip).Pose.position)),Is.GreaterThan(.0001));
                for(var i=0;i<before.Length;i++)Assert.That(raw.RightJoints[i].Pose,Is.EqualTo(before[i].Pose));
                client.RuleSettings.fingers[1].maxDisplacementMeters=.01f;
                raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;raw.CallbackIndex++;
                typeof(DistributedQuestClient).GetMethod("OnRawFrame",Private).Invoke(client,new object[]{raw});client.PoseCorrection.Process(raw,display);
                session.Enqueue(new BleMidiSession.Sample(7,0,0,0x80,60,0));session.Enqueue(new BleMidiSession.Sample(7,0,0,0x80,64,0));session.Flush();
                for(var frame=0;frame<12;frame++)
                {
                    raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;raw.CallbackIndex++;
                    typeof(DistributedQuestClient).GetMethod("OnRawFrame",Private).Invoke(client,new object[]{raw});client.PoseCorrection.Process(raw,display);
                    yield return new WaitForSecondsRealtime(.014f);
                }
                Assert.That(client.PoseCorrection.New.Contacts[1].state,Is.EqualTo(TipCorrectionState.Inactive));
                client.EndLocalResearchSession();Assert.That(client.ResearchLogError,Is.Empty);
                var lines=File.ReadAllLines(Path.Combine(sessionPath,"quest_rule_pose.csv"));var header=lines[0].Split(',');
                Assert.That(lines.Length,Is.GreaterThan(100));
                foreach(var row in lines.Skip(1))Assert.That(row.Split(',').Length,Is.EqualTo(header.Length));
                var first=lines[1].Split(',');string Value(string n)=>first[Array.IndexOf(header,n)];
                Assert.That(Value("requested_method"),Is.EqualTo("JointConstraints"));Assert.That(Value("snapshot_id"),Is.EqualTo("0"));
                Assert.That(Value("coordinate_space"),Is.EqualTo("KeyboardRootLocal"));
                Assert.That(File.ReadAllLines(Path.Combine(sessionPath,"rule_pose_settings.jsonl")).Length,Is.EqualTo(2));
                Assert.That(File.ReadAllLines(Path.Combine(sessionPath,"quest_rule_pose_joints.csv")).Length,Is.GreaterThan(26));
                Assert.That(File.ReadAllLines(Path.Combine(sessionPath,"quest_tip_correction.csv")).Length,Is.EqualTo(1),"NEW samples must never be mislabeled as legacy correction");
                Assert.That(File.ReadAllLines(Path.Combine(sessionPath,"quest_midi_research.csv")).Length,Is.EqualTo(5));
                Assert.That(File.ReadAllText(Path.Combine(sessionPath,"rule_pose_settings.jsonl")),Does.Contain("not independent physical tracking accuracy"));
            }
            finally
            {
                client.EndLocalResearchSession();if(handObject!=null)Object.DestroyImmediate(handObject);Object.DestroyImmediate(runtime);Object.DestroyImmediate(origin);Origin(previous);
                if(sessionPath!=null)
                {
                    var path=Path.GetFullPath(sessionPath);var allowed=Path.GetFullPath(Application.persistentDataPath)+Path.DirectorySeparatorChar;
                    if(path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(path))Directory.Delete(path,true);
                }
            }
        }
        static Rect Bounds(RectTransform rect,Transform root)
        {
            var c=new Vector3[4];rect.GetWorldCorners(c);var a=root.InverseTransformPoint(c[0]);var b=root.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        }
        [UnityTest] public IEnumerator RulePagesHaveNoOverlappingButtonsAndRetainMidiSafetyGate()
        {
            var runtime=new GameObject("Rule UI runtime",typeof(VirtualPianoKeyboard),typeof(XRHandPoseProvider),typeof(PianoCalibrationManager));
            var config=runtime.AddComponent<DistributedSettings>();config.questReceivePort=0;var client=runtime.AddComponent<DistributedQuestClient>();
            var uiGo=new GameObject("Rule UI",typeof(RectTransform),typeof(DistributedQuestUi));
            try
            {
                yield return null;client.StopNetwork();var ui=uiGo.GetComponent<DistributedQuestUi>();
                Assert.That(client.RuleSettings.method,Is.EqualTo(HandCorrectionMethod.Legacy));
                for(var page=5;page<=7;page++)
                {
                    ui.ShowDetails(true,page);Canvas.ForceUpdateCanvases();yield return null;
                    var buttons=uiGo.GetComponentsInChildren<Button>().Select(b=>(RectTransform)b.transform).ToArray();
                    for(var a=0;a<buttons.Length;a++)for(var b=a+1;b<buttons.Length;b++)Assert.That(Bounds(buttons[a],uiGo.transform).Overlaps(Bounds(buttons[b],uiGo.transform)),Is.False,buttons[a].name+" overlaps "+buttons[b].name);
                    foreach(var label in uiGo.GetComponentsInChildren<TMP_Text>()){label.ForceMeshUpdate();Assert.That(label.fontSize,Is.GreaterThanOrEqualTo(18));if(label.transform.parent.GetComponent<Button>()!=null)Assert.That(label.isTextTruncated,Is.False,label.text);}
                }
                ui.ShowDetails(true,5);var mode=uiGo.transform.Find("METHOD NEXT").GetComponent<Button>();mode.onClick.Invoke();
                Assert.That(client.RuleSettings.method,Is.EqualTo(HandCorrectionMethod.ContactOnly));
                typeof(PerformanceUiSafety).GetMethod("SetLocked",Private).Invoke(ui.Safety,new object[]{true});mode.onClick.Invoke();
                Assert.That(client.RuleSettings.method,Is.EqualTo(HandCorrectionMethod.ContactOnly));
            }
            finally{Object.DestroyImmediate(uiGo);Object.DestroyImmediate(runtime);}
        }
    }
}
