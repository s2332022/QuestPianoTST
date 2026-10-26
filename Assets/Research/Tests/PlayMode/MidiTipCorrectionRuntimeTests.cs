using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class MidiTipCorrectionRuntimeTests
    {
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Origin(Transform t)=>typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{t});
        [UnityTest]
        public IEnumerator FloatingUiModesAndPlacementPreserveCorrectionNetworkAndRawSession()
        {
            var previous=ResearchServices.TrackingOrigin;
            var origin=new GameObject("UI regression origin");Origin(origin.transform);
            var runtime=new GameObject("UI regression runtime",typeof(VirtualPianoKeyboard),typeof(XRHandPoseProvider),typeof(PianoCalibrationManager));
            var settings=runtime.AddComponent<DistributedSettings>();settings.questReceivePort=0;
            var client=runtime.AddComponent<DistributedQuestClient>();
            var cameraGo=new GameObject("UI regression camera",typeof(Camera));
            var uiGo=new GameObject("UI regression panel",typeof(RectTransform),typeof(DistributedQuestUi));
            string sessionPath=null;
            try
            {
                yield return null;
                var ui=uiGo.GetComponent<DistributedQuestUi>();ui.PlaceAtCamera(cameraGo.GetComponent<Camera>());
                var config=client.CorrectionSettings;config.enabled=true;config.leftHand=true;config.finger=CorrectionFinger.Middle;
                config.strength=.75f;config.maxCorrectionMeters=.015f;
                var before=JsonUtility.ToJson(config);
                Assert.That(client.BeginLocalResearchSession(),Is.True);sessionPath=client.ResearchSessionPath;
                Assert.That(client.NetworkRunning,Is.True);
                ui.ShowDetails(true,2);ui.SetCompact(true);ui.SetCompact(false);ui.ShowDetails(false);
                ui.RecenterUi();ui.FloatingPanel.AdjustDistance(.1f);ui.FloatingPanel.FaceUser();
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(ui.transform.Find("Performance Overview").GetComponent<TMPro.TMP_Text>().text,Does.Contain("記録：記録中"));
                Assert.That(ui.transform.Find("MIDI Tip Correction Status").GetComponent<TMPro.TMP_Text>().text,Does.Contain("LEFT"));
                Assert.That(client.CorrectionSettings,Is.SameAs(config));Assert.That(JsonUtility.ToJson(config),Is.EqualTo(before));
                Assert.That(client.ResearchRecording,Is.True);Assert.That(client.ResearchSessionPath,Is.EqualTo(sessionPath));
                Assert.That(client.NetworkRunning,Is.True);Assert.That(client.ResearchLogError,Is.Empty);
                var overview=ui.transform.Find("Performance Overview").GetComponent<TMPro.TMP_Text>();
                Assert.That(overview.text,Does.Contain("左手"));
                Assert.That(overview.text,Does.Contain("中指"));
                CaptureJapanesePreview(cameraGo.GetComponent<Camera>(),ui,"session-normal");
                ui.SetCompact(true);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(ui.transform.Find("MIDI Tip Correction Status").gameObject.activeSelf,Is.False);
                CaptureJapanesePreview(cameraGo.GetComponent<Camera>(),ui,"session-compact");
                ui.SetCompact(false);
                uiGo.transform.Find("STOP RAW REC").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(ui.Safety.ConfirmationPending,Is.True);
                Assert.That(client.ResearchRecording,Is.True,"Stopping requires a separate confirmation.");
                var confirm=uiGo.transform.Find("UI operation confirmation/CONFIRM").GetComponent<UnityEngine.UI.Button>();
                Assert.That(confirm.GetComponentInChildren<TMPro.TMP_Text>().text,Is.EqualTo("確認"));
                confirm.onClick.Invoke();
                Assert.That(client.ResearchRecording,Is.False);
            }
            finally
            {
                client.EndLocalResearchSession();
                UnityEngine.Object.DestroyImmediate(uiGo);UnityEngine.Object.DestroyImmediate(cameraGo);
                UnityEngine.Object.DestroyImmediate(runtime);UnityEngine.Object.DestroyImmediate(origin);Origin(previous);
                if(sessionPath!=null)
                {
                    var resolved=Path.GetFullPath(sessionPath);
                    var allowed=Path.GetFullPath(Application.persistentDataPath)+Path.DirectorySeparatorChar;
                    if(resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(resolved))Directory.Delete(resolved,true);
                }
            }
        }
        static void CaptureJapanesePreview(Camera camera,DistributedQuestUi ui,string name)
        {
            Canvas.ForceUpdateCanvases();
            var overview=ui.transform.Find("Performance Overview").GetComponent<TMPro.TMP_Text>();
            overview.ForceMeshUpdate();
            Assert.That(overview.isTextTruncated,Is.False,overview.text);
            foreach(var character in overview.textInfo.characterInfo)
                if(character.isVisible)Assert.That(character.textElement.unicode,Is.EqualTo((uint)character.character),"Missing glyph: "+character.character);
            var folder=Environment.GetEnvironmentVariable("QUEST_UI_CAPTURE_DIR");
            if(string.IsNullOrEmpty(folder)||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            camera.transform.SetPositionAndRotation(ui.transform.position-ui.transform.forward*2,ui.transform.rotation);
            camera.orthographic=true;camera.aspect=1;
            camera.orthographicSize=Mathf.Max(((RectTransform)ui.transform).rect.height,720)*.00065f*.55f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.1f,.12f);
            var rt=new RenderTexture(1600,1600,24);var texture=new Texture2D(1600,1600,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            var elements=ui.GetComponentsInChildren<Transform>(true);
            var layers=elements.Select(t=>t.gameObject.layer).ToArray();var mask=camera.cullingMask;
            try
            {
                foreach(var element in elements)element.gameObject.layer=31;
                camera.cullingMask=1<<31;
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,1600,1600),0,0);texture.Apply();
                Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());
            }
            finally
            {
                for(var i=0;i<elements.Length;i++)elements[i].gameObject.layer=layers[i];
                camera.cullingMask=mask;camera.targetTexture=null;RenderTexture.active=previous;
                UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator BleObservationMovesOfficialSkinnedMeshAndPairedLogPreservesRaw()
        {
            var previous=ResearchServices.TrackingOrigin;
            var origin=new GameObject("Correction runtime origin");Origin(origin.transform);
            var go=new GameObject("Correction runtime",typeof(VirtualPianoKeyboard),typeof(XRHandPoseProvider),typeof(PianoCalibrationManager),typeof(BleMidiInput));
            var settings=go.AddComponent<DistributedSettings>();settings.questReceivePort=0;
            var client=go.AddComponent<DistributedQuestClient>();var keyboard=go.GetComponent<VirtualPianoKeyboard>();
            var provider=go.GetComponent<XRHandPoseProvider>();var calibration=go.GetComponent<PianoCalibrationManager>();
            typeof(PianoCalibrationManager).GetProperty("CurrentAppliedCalibration").GetSetMethod(true).Invoke(calibration,new object[]{new PianoCalibrationData{valid=true,calibrationId="correction_runtime_test"}});
            var meshPrefab=Resources.Load<GameObject>("CorrectionHands/RightDisplayHand");
            Assert.That(meshPrefab,Is.Not.Null,"Run CorrectionHandAssetBuilder.Prepare before testing");
            var hand=UnityEngine.Object.Instantiate(meshPrefab).GetComponent<DisplayHandSkeleton>();hand.Initialize(provider,null);
            string path=null;Mesh rawMesh=null,correctedMesh=null;
            try
            {
                yield return null;client.StopNetwork();
                client.CorrectionSettings.enabled=true;client.CorrectionSettings.strength=1;
                Assert.That(client.BeginLocalResearchSession(),Is.True);path=client.ResearchSessionPath;
                keyboard.TryGetRestKeyBounds(60,out var bounds);
                var tip=new Vector3(bounds.max.x+.01f,.005f,.04f);
                var raw=Frame(tip);var display=new HandPoseFrame(raw.RightJoints.Length);
                hand.Apply(raw);
                rawMesh=new Mesh();hand.GetComponentInChildren<SkinnedMeshRenderer>().BakeMesh(rawMesh);
                var ble=go.GetComponent<BleMidiInput>();
                var session=(BleMidiSession)typeof(BleMidiInput).GetField("m_Session",Private).GetValue(ble);
                session.Begin(7,"injected BLE transport test");
                var parser=new BleMidiPacketParser();
                Assert.That(parser.Parse(new byte[]{0x80,0x80,0x90,60,100},7,0,s=>session.Enqueue(s)),Is.True);
                session.Flush();
                Assert.That(client.Correction.State,Is.EqualTo(TipCorrectionState.Engaging));
                var stop=Time.realtimeSinceStartup+.13f;
                // A first rendered frame may exceed this duration; still evaluate a later pose timestamp.
                for(var frames=0;frames<2||Time.realtimeSinceStartup<stop;frames++)
                {
                    raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;raw.CallbackIndex++;
                    typeof(DistributedQuestClient).GetMethod("OnRawFrame",Private).Invoke(client,new object[]{raw});
                    client.Correction.Process(raw,display);
                    var callback=(Action<HandPoseFrame>)typeof(XRHandPoseProvider).GetField("DisplayFrameUpdated",Private).GetValue(provider);
                    callback?.Invoke(display);
                    yield return null;
                }
                Assert.That(client.Correction.Latest.DeltaKeyboard.magnitude,Is.GreaterThan(.005f));
                var bone=hand.Joints.Single(x=>x.xrHandJointID==XRHandJointID.IndexTip).jointTransform;
                Assert.That(Vector3.Distance(bone.position,origin.transform.TransformPoint(display.FindJoint(false,XRHandJointID.IndexTip).Pose.position)),Is.LessThan(1e-5));
                Assert.That(Vector3.Distance(bone.position,tip),Is.GreaterThan(.005f),"Actual skinned mesh bone must move, not only a debug marker");
                Assert.That(hand.Visible,Is.True);
                Assert.That(hand.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.shader.name,
                    Is.EqualTo("Universal Render Pipeline/Unlit"));
                correctedMesh=new Mesh();hand.GetComponentInChildren<SkinnedMeshRenderer>().BakeMesh(correctedMesh);
                var beforeVertices=rawMesh.vertices;var afterVertices=correctedMesh.vertices;
                Assert.That(beforeVertices.Length,Is.EqualTo(afterVertices.Length));
                Assert.That(beforeVertices.Where((v,i)=>Vector3.Distance(v,afterVertices[i])>.0001f).Count(),Is.GreaterThan(0),
                    "Skinned vertices must deform, not only a Transform or debug marker");
                Assert.That(hand.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.vertexCount,Is.GreaterThan(100));
                Assert.That(hand.GetComponentsInChildren<XRHandSkeletonDriver>(true),Is.Empty);
                Assert.That(hand.GetComponentsInChildren<XRHandTrackingEvents>(true),Is.Empty);
                Assert.That(raw.FindJoint(false,XRHandJointID.IndexTip).Pose.position,Is.EqualTo(tip));
                session.Enqueue(new BleMidiSession.Sample(7,0,0,0x80,60,0));session.Flush();
                Assert.That(client.Correction.State,Is.EqualTo(TipCorrectionState.Releasing));
                var deadline=Time.realtimeSinceStartup+.15f;
                for(var frames=0;frames<2||Time.realtimeSinceStartup<deadline;frames++)
                {
                    raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;
                    client.Correction.Process(raw,display);hand.Apply(display);yield return null;
                }
                Assert.That(client.Correction.State,Is.EqualTo(TipCorrectionState.Inactive));
                Assert.That(Vector3.Distance(bone.position,tip),Is.LessThan(1e-5));
                client.EndLocalResearchSession();Assert.That(client.ResearchLogError,Is.Empty);
                var rows=File.ReadAllLines(Path.Combine(path,"quest_tip_correction.csv"));
                var header=rows[0].Split(',');var row=rows[1].Split(',');
                Assert.That(row.Length,Is.EqualTo(header.Length));
                string Value(string name)=>row[Array.IndexOf(header,name)];
                Assert.That(Value("coordinate_space"),Is.EqualTo("KeyboardRootLocal"));Assert.That(Value("position_unit"),Is.EqualTo("m"));
                Assert.That(Value("event_index"),Is.EqualTo("1"));Assert.That(Value("source"),Is.EqualTo("BLE"));
                var rawRows=File.ReadAllLines(Path.Combine(path,"quest_hand_raw.csv"));
                Assert.That(Value("quest_timestamp_sec"),Is.EqualTo(rawRows[1].Split(',')[1]));
                Assert.That(Value("snapshot_id"),Is.EqualTo(rawRows[1].Split(',')[18]));
                Assert.That(File.ReadAllText(Path.Combine(path,"tip_correction_metadata.json")),Does.Contain("not absolute hand position error"));
            }
            finally
            {
                client.EndLocalResearchSession();
                if(rawMesh!=null)UnityEngine.Object.DestroyImmediate(rawMesh);
                if(correctedMesh!=null)UnityEngine.Object.DestroyImmediate(correctedMesh);
                UnityEngine.Object.DestroyImmediate(hand.gameObject);
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(origin);Origin(previous);
                if(path!=null&&Directory.Exists(path))Directory.Delete(path,true);
            }
        }
        [UnityTest]
        public IEnumerator ActualUdpSocketUsesQuestReceiveTimeAndPreservesKeyboardProcessing()
        {
            using var peer=new UdpTransport();peer.Start(0);
            int port;using(var probe=new UdpTransport()){probe.Start(0);port=probe.LocalPort;}
            var previous=ResearchServices.TrackingOrigin;var origin=new GameObject("UDP correction origin");Origin(origin.transform);
            var go=new GameObject("UDP correction runtime",typeof(VirtualPianoKeyboard),typeof(XRHandPoseProvider),typeof(PianoCalibrationManager));
            var settings=go.AddComponent<DistributedSettings>();settings.pcIpAddress="127.0.0.1";
            settings.questReceivePort=port;settings.pcReceivePort=peer.LocalPort;
            var client=go.AddComponent<DistributedQuestClient>();var keyboard=go.GetComponent<VirtualPianoKeyboard>();
            var calibration=go.GetComponent<PianoCalibrationManager>();
            typeof(PianoCalibrationManager).GetProperty("CurrentAppliedCalibration").GetSetMethod(true).Invoke(calibration,new object[]{new PianoCalibrationData{valid=true}});
            try
            {
                client.CorrectionSettings.enabled=true;client.CorrectionSettings.strength=1;
                yield return null;
                var instance=(uint)typeof(DistributedQuestClient).GetField("m_InstanceId",Private).GetValue(client);
                var id=Guid.NewGuid();var bytes=new byte[NetworkProtocolV1.MaximumDatagramBytes];var endpoint=new IPEndPoint(IPAddress.Loopback,port);
                var n=NetworkProtocolV1.WriteStartupAck(bytes,1,-999,id,instance);peer.Send(bytes,n,endpoint);
                var deadline=Time.realtimeSinceStartup+2;while(!client.PcConnected&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(client.PcConnected,Is.True);
                var message=new MidiMessage(-999,123,"test",MidiEventType.NoteOn,1,60,100,-1,-1);
                n=NetworkProtocolV1.WriteMidi(bytes,2,-999,id,in message,0);
                var before=ResearchServices.Clock.AbsoluteSeconds;peer.Send(bytes,n,endpoint);
                deadline=Time.realtimeSinceStartup+2;while(!keyboard.State.IsPressed(60)&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(keyboard.State.IsPressed(60),Is.True,"Existing UDP key state path must still run");
                keyboard.TryGetRestKeyBounds(60,out var bounds);var raw=Frame(new Vector3(bounds.max.x+.01f,.005f,.04f));
                var display=new HandPoseFrame(raw.RightJoints.Length);deadline=Time.realtimeSinceStartup+.13f;
                while(Time.realtimeSinceStartup<deadline)
                {raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;client.Correction.Process(raw,display);yield return null;}
                Assert.That(client.Correction.Latest.Source,Is.EqualTo("UDP"));
                Assert.That(client.Correction.Latest.EventIndex,Is.EqualTo(123));
                Assert.That(client.Correction.Latest.ReceivedTime,Is.GreaterThanOrEqualTo(before));
                Assert.That(client.Correction.Latest.DeltaKeyboard.magnitude,Is.GreaterThan(.005f));
                client.StopNetwork();Assert.That(keyboard.State.IsPressed(60),Is.False);
                Assert.That(client.Correction.State,Is.EqualTo(TipCorrectionState.Releasing));
                deadline=Time.realtimeSinceStartup+.15f;
                while(Time.realtimeSinceStartup<deadline)
                {raw.AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds;client.Correction.Process(raw,display);yield return null;}
                Assert.That(client.Correction.State,Is.EqualTo(TipCorrectionState.Inactive));
                Assert.That(client.Correction.Latest.DeltaKeyboard,Is.EqualTo(Vector3.zero));
            }
            finally
            {UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(origin);Origin(previous);}
        }
        static HandPoseFrame Frame(Vector3 tip)
        {
            var count=(int)XRHandJointID.EndMarker-(int)XRHandJointID.BeginMarker;
            var frame=new HandPoseFrame(count){RightTracked=true,UpdateType=XRHandSubsystem.UpdateType.Dynamic};
            for(var i=0;i<count;++i)
            {
                var id=(XRHandJointID)((int)XRHandJointID.BeginMarker+i);
                var p=new Vector3(.1f,.2f,.3f);
                if(id==XRHandJointID.IndexProximal)p=tip+new Vector3(0,.05f,-.04f);
                if(id==XRHandJointID.IndexIntermediate)p=tip+new Vector3(0,.035f,-.022f);
                if(id==XRHandJointID.IndexDistal)p=tip+new Vector3(0,.015f,-.008f);
                if(id==XRHandJointID.IndexTip)p=tip;
                frame.RightJoints[i]=new HandJointPose{JointId=id,PoseValid=true,TrackingState=XRHandJointTrackingState.Pose,Pose=new Pose(p,Quaternion.identity)};
            }
            return frame;
        }
    }
}
