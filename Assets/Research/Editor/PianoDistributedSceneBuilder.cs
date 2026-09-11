using System;
using System.IO;
using System.Linq;
using QuestPianoMotion.Research.Distributed;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

namespace QuestPianoMotion.Research.Editor
{
    public static class PianoDistributedSceneBuilder
    {
        public const string QuestScene="Assets/Research/Scenes/PianoDistributedQuest.unity";
        public const string HostScene="Assets/Research/Scenes/PianoDistributedHost.unity";
        [MenuItem("Quest Piano Motion/Create Distributed Scenes")]
        public static void CreateScenes(){CreateQuest();CreateHost();AssetDatabase.SaveAssets();}
        static void CreateQuest()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var originObject=new GameObject("XR Origin");var origin=originObject.AddComponent<XROrigin>();var offset=new GameObject("Camera Offset");offset.transform.SetParent(originObject.transform,false);var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";cameraObject.transform.SetParent(offset.transform,false);var camera=cameraObject.AddComponent<Camera>();camera.nearClipPlane=.05f;camera.farClipPlane=20f;cameraObject.AddComponent<AudioListener>();var driver=cameraObject.AddComponent<TrackedPoseDriver>();driver.trackingType=TrackedPoseDriver.TrackingType.RotationAndPosition;driver.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;driver.positionInput=Action("HMD Position","Vector3","<XRHMD>/centerEyePosition");driver.rotationInput=Action("HMD Rotation","Quaternion","<XRHMD>/centerEyeRotation");driver.trackingStateInput=Action("HMD Tracking State","Integer","<XRHMD>/trackingState");origin.Camera=camera;origin.CameraFloorOffsetObject=offset;origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Device;
            var runtime=new GameObject("Research Runtime");runtime.AddComponent<XRHandPoseProvider>();runtime.AddComponent<VirtualPianoKeyboard>();runtime.AddComponent<PianoCalibrationManager>();runtime.AddComponent<MinimalHandVisualizer>();runtime.AddComponent<DistributedQuestComposition>();new GameObject("Piano Root");var ui=new GameObject("Research UI");ui.AddComponent<DistributedQuestUi>();var network=new GameObject("Network Client");var settings=network.AddComponent<DistributedSettings>();settings.executionMode=ResearchExecutionMode.DistributedQuestClient;network.AddComponent<NetworkMidiInput>();network.AddComponent<DistributedQuestClient>();var events=new GameObject("EventSystem");events.AddComponent<EventSystem>();events.AddComponent<MinimalHandUiInputModule>();Save(scene,QuestScene);
        }
        static void CreateHost()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Distributed Host Runtime");var settings=root.AddComponent<DistributedSettings>();settings.executionMode=ResearchExecutionMode.DistributedPcHost;var midi=new GameObject("MIDI Input");midi.AddComponent<PcMidiInput>();var server=new GameObject("Network Server");server.AddComponent<DistributedPcHost>();var recorder=new GameObject("Session Recorder");recorder.AddComponent<DistributedSessionRecorder>();var ui=new GameObject("Host UI");ui.AddComponent<DistributedHostUi>();Save(scene,HostScene);
        }
        static void Save(Scene scene,string path){Directory.CreateDirectory(Path.GetDirectoryName(path));EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene,path))throw new IOException("Failed to save "+path);}
        [MenuItem("Quest Piano Motion/Validate Distributed Scenes")]
        public static void ValidateScenes(){Validate(QuestScene,new[]{"XR Origin","Research Runtime","Piano Root","Research UI","Network Client","EventSystem"});Validate(HostScene,new[]{"Distributed Host Runtime","MIDI Input","Network Server","Session Recorder","Host UI"});Debug.Log("Distributed scene validation passed.");}
        static void Validate(string path,string[] expected){var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);var actual=scene.GetRootGameObjects().Select(x=>x.name).OrderBy(x=>x).ToArray();if(!actual.SequenceEqual(expected.OrderBy(x=>x)))throw new InvalidDataException(path+" roots: "+string.Join(",",actual));foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)!=0)throw new InvalidDataException("Missing script: "+t.name);}
        public static void BuildWindowsValidation(){Build(HostScene,BuildTarget.StandaloneWindows64,Path.Combine(Path.GetTempPath(),"QuestPianoMotion-Windows-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),"PianoDistributedHost.exe"));}
        public static void BuildAndroidValidation(){Build(QuestScene,BuildTarget.Android,Path.Combine(Path.GetTempPath(),"QuestPianoMotion-Android-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),"PianoDistributedQuest.apk"));}
        static void Build(string scene,BuildTarget target,string output){Directory.CreateDirectory(Path.GetDirectoryName(output));var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=output,target=target,options=BuildOptions.Development|BuildOptions.CompressWithLz4});if(report.summary.result!=BuildResult.Succeeded)throw new InvalidDataException(target+" build failed: "+report.summary.result);Debug.Log(target+" validation build passed: "+output);}
        static InputActionProperty Action(string name,string type,string binding){var action=new InputAction(name,InputActionType.Value,expectedControlType:type);action.AddBinding(binding);return new InputActionProperty(action);}
    }
}
