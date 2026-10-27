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
        [UnityTest] public IEnumerator ExclusiveAssignmentsSurviveChordsRetriggersTimeoutsAndReconnects()
        {
            var root=new GameObject("Exclusive runtime keyboard",typeof(VirtualPianoKeyboard));var origin=new GameObject("Exclusive runtime origin");
            try
            {
                var common=new MidiTipCorrectionSettings{enabled=true,strength=1,maxHoldSeconds=.4f};
                var rules=new RuleHandPoseSettings{method=HandCorrectionMethod.FullRules,manualNoteMap=true};
                rules.noteToFinger[60]=1;rules.noteToFinger[64]=2;rules.noteToFinger[65]=1;
                var p=new RuleHandPoseProcessor(common,rules,root.GetComponent<VirtualPianoKeyboard>(),()=>true,()=>origin.transform);
                var raw=Frame();var display=new HandPoseFrame(raw.RightJoints.Length);
                void Midi(int note,double t,bool on=true,string source="BLE",long generation=1,int channel=1)
                    =>p.ObserveMidi(source,new MidiMessage(t,1,"exclusive runtime",on?MidiEventType.NoteOn:MidiEventType.NoteOff,channel,note,on?100:0,-1,-1),t,t,generation);
                void Pose(double t)
                {
                    raw.AbsoluteTimeSeconds=t;p.Process(raw,display);
                    var notes=new System.Collections.Generic.HashSet<int>();
                    for(var f=0;f<5;f++)if(p.Contacts[f].state==TipCorrectionState.Active || p.Contacts[f].state==TipCorrectionState.Engaging)
                    {Assert.That(notes.Add(p.Contacts[f].note),Is.True,"Two fingers must never attract to one key");Assert.That(p.AssignedFinger(p.Contacts[f].note),Is.EqualTo(f));}
                }
                Midi(60,10);Midi(64,10.001);
                for(var f=0;f<20;f++){Pose(10.01+f/100d);yield return null;}
                Assert.That(p.Contacts[1].state,Is.EqualTo(TipCorrectionState.Active));Assert.That(p.Contacts[2].state,Is.EqualTo(TipCorrectionState.Active));
                var first=p.Contacts[1];Midi(65,10.21);Assert.That(p.Contacts[1],Is.EqualTo(first));
                Assert.That(p.AssignmentRejectionJapanese,Does.Contain("割当拒否"));
                rules.noteToFinger[60]=3;Midi(60,10.22,source:"UDP");
                Assert.That(p.AssignedFinger(60),Is.EqualTo(1));Assert.That(p.Contacts[3].state,Is.EqualTo(TipCorrectionState.Inactive));
                rules.noteToFinger[60]=1;Midi(60,10.23,false,"UDP");Midi(65,10.24,false);
                Midi(60,10.25,false);Assert.That(p.AssignedFinger(60),Is.EqualTo(-1));
                Assert.That(p.Contacts[1].state,Is.EqualTo(TipCorrectionState.Releasing));
                p.TrySetManualAssignment(60,-1);p.TrySetManualAssignment(60,3);Midi(60,10.26);Pose(10.27);
                Assert.That(p.Contacts[1].state,Is.EqualTo(TipCorrectionState.Inactive));Assert.That(p.AssignedFinger(60),Is.EqualTo(3));
                Midi(60,10.28);Pose(10.29);Assert.That(p.Reason,Is.EqualTo("await_all_notes_off"));
                Assert.That(p.AssignedFinger(60),Is.EqualTo(3));
                Midi(60,10.30,false);Midi(64,10.31,false);Midi(60,10.32);
                for(var f=0;f<65;f++){Pose(10.33+f/100d);yield return null;}
                Assert.That(p.Contacts[3].state,Is.EqualTo(TipCorrectionState.Inactive));Assert.That(p.AssignedFinger(60),Is.EqualTo(3),"Missing Note Off must retain occupancy after animation timeout");
                p.EndSession(11);Assert.That(p.AssignedFinger(60),Is.EqualTo(3));
                p.DisconnectSource("BLE",11.01);Assert.That(p.HeldCount,Is.Zero);Assert.That(p.AssignedFinger(60),Is.EqualTo(-1));
                Midi(60,11.02,generation:2);Midi(60,11.03,false,generation:1);Pose(11.04);
                Assert.That(p.AssignedFinger(60),Is.EqualTo(3));Midi(60,11.05,false,generation:2);
                Assert.That(p.AssignedFinger(60),Is.EqualTo(-1));
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(origin);}
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
                var common=client.CorrectionSettings;
                var enabled=common.enabled;var left=common.leftHand;var finger=common.finger;
                var toggle=uiGo.GetComponentsInChildren<Button>(true).Single(b=>b.name=="TIP ON / OFF");
                var hand=uiGo.GetComponentsInChildren<Button>(true).Single(b=>b.name=="LEFT / RIGHT");
                toggle.onClick.Invoke();
                Assert.That(common.enabled,Is.EqualTo(!enabled));
                toggle.onClick.Invoke();
                Assert.That(common.enabled,Is.EqualTo(enabled));
                hand.onClick.Invoke();
                Assert.That(common.leftHand,Is.EqualTo(!left));
                hand.onClick.Invoke();
                for(var i=1;i<=5;i++)
                {
                    uiGo.transform.Find("NEXT FINGER").GetComponent<Button>().onClick.Invoke();
                    Assert.That(common.finger,Is.EqualTo((CorrectionFinger)(((int)finger+i)%5)));
                }
                for(var page=5;page<=7;page++)
                {
                    ui.ShowDetails(true,page);Canvas.ForceUpdateCanvases();yield return null;
                    var buttons=uiGo.GetComponentsInChildren<Button>().Select(b=>(RectTransform)b.transform).ToArray();
                    for(var a=0;a<buttons.Length;a++)for(var b=a+1;b<buttons.Length;b++)Assert.That(Bounds(buttons[a],uiGo.transform).Overlaps(Bounds(buttons[b],uiGo.transform)),Is.False,buttons[a].name+" overlaps "+buttons[b].name);
                    foreach(var label in uiGo.GetComponentsInChildren<TMP_Text>()){label.ForceMeshUpdate();Assert.That(label.fontSize,Is.GreaterThanOrEqualTo(18));if(label.transform.parent.GetComponent<Button>()!=null)Assert.That(label.isTextTruncated,Is.False,label.text);}
                }
                ui.ShowDetails(true,5);var mode=uiGo.transform.Find("METHOD NEXT").GetComponent<Button>();mode.onClick.Invoke();
                Assert.That(client.RuleSettings.method,Is.EqualTo(HandCorrectionMethod.FullRules));
                mode.onClick.Invoke();
                Assert.That(client.RuleSettings.method,Is.EqualTo(HandCorrectionMethod.Legacy));
                mode.onClick.Invoke();
                client.RuleSettings.manualNoteMap=true;
                common.finger=CorrectionFinger.Index;
                uiGo.transform.Find("ASSIGN SELECTED FINGER").GetComponent<Button>().onClick.Invoke();
                common.finger=CorrectionFinger.Middle;
                uiGo.transform.Find("ASSIGN SELECTED FINGER").GetComponent<Button>().onClick.Invoke();
                Assert.That(client.RuleSettings.noteToFinger[60],Is.EqualTo(1));
                var now=ResearchServices.Clock.AbsoluteSeconds;
                client.PoseCorrection.New.ObserveMidi("BLE",new MidiMessage(now,1,"UI assignment",MidiEventType.NoteOn,1,60,100,-1,-1),now,now,1);
                Assert.That(client.PoseCorrection.New.AssignedFinger(60),Is.EqualTo(1));
                uiGo.transform.Find("CLEAR THIS NOTE").GetComponent<Button>().onClick.Invoke();
                Assert.That(client.RuleSettings.noteToFinger[60],Is.EqualTo(1));
                typeof(DistributedQuestUi).GetMethod("UpdateRuleDetails",Private).Invoke(ui,null);
                var assignmentText=uiGo.transform.Find("Rule details 6").GetComponent<TMP_Text>().text;
                Assert.That(assignmentText,Does.Contain("60→人差し指"));Assert.That(assignmentText,Does.Contain("割当拒否"));
                client.PoseCorrection.New.DisconnectSource("BLE",now+.01);
                for(var f=0;f<5;f++)
                {
                    client.RuleSettings.noteToFinger[100+f]=f;
                    client.PoseCorrection.New.ObserveMidi("BLE",new MidiMessage(now+.02,f+2,"UI chord",MidiEventType.NoteOn,1,100+f,100,-1,-1),now+.02,now+.02,2);
                    Assert.That(client.PoseCorrection.New.AssignedFinger(100+f),Is.EqualTo(f));
                }
                client.PoseCorrection.New.TrySetManualAssignment(127,0);client.PoseCorrection.New.TrySetManualAssignment(127,1);
                ui.ShowDetails(true,6);typeof(DistributedQuestUi).GetMethod("UpdateRuleDetails",Private).Invoke(ui,null);
                Canvas.ForceUpdateCanvases();var assignmentLabel=uiGo.transform.Find("Rule details 6").GetComponent<TMP_Text>();assignmentLabel.ForceMeshUpdate();
                Assert.That(assignmentLabel.isTextTruncated,Is.False,"All five assignments and the refusal must remain readable");
                Assert.That(assignmentLabel.text,Does.Contain("104→小指"));
                client.PoseCorrection.New.DisconnectSource("BLE",now+.03);
                typeof(PerformanceUiSafety).GetMethod("SetLocked",Private).Invoke(ui.Safety,new object[]{true});mode.onClick.Invoke();
                Assert.That(client.RuleSettings.method,Is.EqualTo(HandCorrectionMethod.FullRules));
            }
            finally{Object.DestroyImmediate(uiGo);Object.DestroyImmediate(runtime);}
        }
    }
}
