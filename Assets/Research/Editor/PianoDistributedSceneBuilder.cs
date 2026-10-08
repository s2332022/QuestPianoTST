using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
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
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.ARFoundation;

namespace QuestPianoMotion.Research.Editor
{
    public static class PianoDistributedSceneBuilder
    {
        public const string QuestScene="Assets/Research/Scenes/PianoDistributedQuest.unity";
        public const string HostScene="Assets/Research/Scenes/PianoDistributedHost.unity";
        const string BuildInfoPath="Assets/Research/Resources/DistributedBuildInfo.txt";
        const string BuildMetadataPath="Assets/Research/Resources/DistributedBuildMetadata.json";
        const string XriInputActionsPath="Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
        const string RightNearFarPrefabPath="Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Interactors/Right_NearFarInteractor.prefab";
        [MenuItem("Quest Piano Motion/Create Distributed Scenes")]
        public static void CreateScenes(){CreateQuest();CreateHost();AssetDatabase.SaveAssets();}
        [MenuItem("Quest Piano Motion/Create Distributed Quest Scene")]
        public static void CreateQuestScene(){CreateQuest();AssetDatabase.SaveAssets();}
        static void CreateQuest()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var originObject=new GameObject("XR Origin");var origin=originObject.AddComponent<XROrigin>();var offset=new GameObject("Camera Offset");offset.transform.SetParent(originObject.transform,false);var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";cameraObject.transform.SetParent(offset.transform,false);var camera=cameraObject.AddComponent<Camera>();camera.nearClipPlane=.05f;camera.farClipPlane=20f;cameraObject.AddComponent<AudioListener>();var arCamera=cameraObject.AddComponent<ARCameraManager>();arCamera.enabled=false;var driver=cameraObject.AddComponent<TrackedPoseDriver>();driver.trackingType=TrackedPoseDriver.TrackingType.RotationAndPosition;driver.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;driver.positionInput=Action("HMD Position","Vector3","<XRHMD>/centerEyePosition");driver.rotationInput=Action("HMD Rotation","Quaternion","<XRHMD>/centerEyeRotation");driver.trackingStateInput=Action("HMD Tracking State","Integer","<XRHMD>/trackingState");origin.Camera=camera;origin.CameraFloorOffsetObject=offset;origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Device;
            var xriActions=AssetDatabase.LoadAssetAtPath<InputActionAsset>(XriInputActionsPath);if(xriActions==null)throw new FileNotFoundException("Missing XRI input actions",XriInputActionsPath);var rightPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(RightNearFarPrefabPath);if(rightPrefab==null)throw new FileNotFoundException("Missing right Near-Far Interactor prefab",RightNearFarPrefabPath);
            var interactionManagerObject=new GameObject("XR Interaction Manager");var interactionManager=interactionManagerObject.AddComponent<XRInteractionManager>();var inputManagerObject=new GameObject("Input Action Manager");var inputManager=inputManagerObject.AddComponent<InputActionManager>();inputManager.actionAssets=new List<InputActionAsset>{xriActions};
            var rightInteractorObject=(GameObject)PrefabUtility.InstantiatePrefab(rightPrefab,scene);rightInteractorObject.name="Right Hand UI Near-Far Interactor";rightInteractorObject.transform.SetParent(offset.transform,false);var rightInteractor=rightInteractorObject.GetComponent<NearFarInteractor>();rightInteractor.interactionManager=interactionManager;rightInteractor.handedness=InteractorHandedness.Right;rightInteractor.enableNearCasting=false;rightInteractor.enableFarCasting=true;rightInteractor.enableUIInteraction=true;var aimDriver=rightInteractorObject.AddComponent<TrackedPoseDriver>();aimDriver.trackingType=TrackedPoseDriver.TrackingType.RotationAndPosition;aimDriver.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;aimDriver.positionInput=new InputActionProperty(Reference(xriActions,"XRI Right/Aim Position"));aimDriver.rotationInput=new InputActionProperty(Reference(xriActions,"XRI Right/Aim Rotation"));aimDriver.trackingStateInput=new InputActionProperty(Reference(xriActions,"XRI Right/Tracking State"));
            var runtime=new GameObject("Research Runtime");runtime.AddComponent<XRHandPoseProvider>();runtime.AddComponent<VirtualPianoKeyboard>();runtime.AddComponent<PianoCalibrationManager>();runtime.AddComponent<MinimalHandVisualizer>();runtime.AddComponent<DistributedQuestComposition>();var inputController=runtime.AddComponent<XriHandUiInputController>();new GameObject("Piano Root");var ui=new GameObject("Research UI");ui.AddComponent<DistributedQuestUi>();var network=new GameObject("Network Client");var settings=network.AddComponent<DistributedSettings>();settings.executionMode=ResearchExecutionMode.DistributedQuestClient;settings.pcIpAddress=DistributedSettings.DefaultPcIpAddress;network.AddComponent<NetworkMidiInput>();network.AddComponent<DistributedQuestClient>();var events=new GameObject("EventSystem");events.AddComponent<EventSystem>();var legacy=events.AddComponent<MinimalHandUiInputModule>();legacy.enabled=false;var xrUi=events.AddComponent<XRUIInputModule>();inputController.Configure(legacy,rightInteractor,xrUi,Reference(xriActions,"XRI Right/Is Tracked"));inputController.ApplyMode();Save(scene,QuestScene);
        }
        static void CreateHost()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Distributed Host Runtime");var settings=root.AddComponent<DistributedSettings>();settings.executionMode=ResearchExecutionMode.DistributedPcHost;var midi=new GameObject("MIDI Input");midi.AddComponent<PcMidiInput>();var server=new GameObject("Network Server");server.AddComponent<DistributedPcHost>();var recorder=new GameObject("Session Recorder");recorder.AddComponent<DistributedSessionRecorder>();var ui=new GameObject("Host UI");ui.AddComponent<DistributedHostUi>();Save(scene,HostScene);
        }
        static void Save(Scene scene,string path){Directory.CreateDirectory(Path.GetDirectoryName(path));EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene,path))throw new IOException("Failed to save "+path);}
        [MenuItem("Quest Piano Motion/Validate Distributed Scenes")]
        public static void ValidateScenes(){Validate(QuestScene,new[]{"XR Origin","XR Interaction Manager","Input Action Manager","Research Runtime","Piano Root","Research UI","Network Client","EventSystem"});Validate(HostScene,new[]{"Distributed Host Runtime","MIDI Input","Network Server","Session Recorder","Host UI"});Debug.Log("Distributed scene validation passed.");}
        static void Validate(string path,string[] expected)
        {
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var actual=scene.GetRootGameObjects().Select(x=>x.name).OrderBy(x=>x).ToArray();
            if(!actual.SequenceEqual(expected.OrderBy(x=>x)))throw new InvalidDataException(path+" roots: "+string.Join(",",actual));
            foreach(var root in scene.GetRootGameObjects())
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)!=0)
                    throw new InvalidDataException("Missing script: "+t.name);
                foreach(var component in t.GetComponents<Component>())
                {
                    if(component==null)continue;
                    var iterator=new SerializedObject(component).GetIterator();
                    while(iterator.NextVisible(true))
                        if(iterator.propertyType==SerializedPropertyType.ObjectReference &&
                           iterator.objectReferenceValue==null &&
                           !iterator.objectReferenceEntityIdValue.Equals(default(UnityEngine.EntityId)))
                            throw new InvalidDataException("Missing reference: "+path+" "+t.name+"/"+component.GetType().Name+"."+iterator.propertyPath);
                }
            }
        }
        public static void BuildWindowsValidation(){BuildWindowsDevelopmentValidation();}
        public static void BuildWindowsReleaseValidation(){Build(HostScene,BuildTarget.StandaloneWindows64,Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds","WindowsHost","PianoDistributedHost.exe"),BuildOptions.CompressWithLz4);}
        public static void BuildWindowsDevelopmentValidation(){Build(HostScene,BuildTarget.StandaloneWindows64,Path.Combine(Path.GetTempPath(),"QuestPianoMotion-Windows-Development-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),"PianoDistributedHost.exe"),BuildOptions.Development|BuildOptions.CompressWithLz4);}        [MenuItem("Quest Piano Motion/Build Android Quest Validation")]
        public static void BuildAndroidValidation()
        {
            const string output = @"C:\UnityProjects\QuestPianoMotion\QuestPianoMotion\Builds\Android\PianoDistributedQuest.apk";
            var androidTarget = UnityEditor.Build.NamedBuildTarget.Android;
            var previousBackend = PlayerSettings.GetScriptingBackend(androidTarget);
            var previousArchitectures = PlayerSettings.Android.targetArchitectures;
            var previousBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            var previousBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            try
            {
                PlayerSettings.SetScriptingBackend(androidTarget, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new InvalidOperationException("Failed to switch the active build target to Android.");
                Build(QuestScene, BuildTarget.Android, output, BuildOptions.None);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(androidTarget, previousBackend);
                PlayerSettings.Android.targetArchitectures = previousArchitectures;
                if (EditorUserBuildSettings.activeBuildTarget != previousBuildTarget &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(previousBuildTargetGroup, previousBuildTarget))
                    throw new InvalidOperationException("Failed to restore the previous active build target: " + previousBuildTarget + ".");
            }
        }
        static void Build(string scene,BuildTarget target,string output,BuildOptions options=BuildOptions.Development|BuildOptions.CompressWithLz4)
        {
            var buildInfoExisted=File.Exists(BuildInfoPath);
            var previousBuildInfo=buildInfoExisted?File.ReadAllBytes(BuildInfoPath):null;
            var previousBuildMetadata=ReadExistingBuildMetadata(BuildMetadataPath);
            var preloadedBeforeBuild=PlayerSettings.GetPreloadedAssets();
            var activeTargetBeforeBuild=EditorUserBuildSettings.activeBuildTarget;
            var activeTargetGroupBeforeBuild=EditorUserBuildSettings.selectedBuildTargetGroup;
            BuildReport report;
            try
            {
                PrepareBuildInfo();
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=output,target=target,options=options});
            }
            finally
            {
                try
                {
                    if(buildInfoExisted)
                    {
                        File.WriteAllBytes(BuildInfoPath,previousBuildInfo);
                        AssetDatabase.ImportAsset(BuildInfoPath,ImportAssetOptions.ForceSynchronousImport);
                    }
                    else if(File.Exists(BuildInfoPath))
                    {
                        AssetDatabase.DeleteAsset(BuildInfoPath);
                    }
                    RestoreBuildMetadata(BuildMetadataPath,previousBuildMetadata);
                }
                finally
                {
                    if(!preloadedBeforeBuild.SequenceEqual(PlayerSettings.GetPreloadedAssets()))PlayerSettings.SetPreloadedAssets(preloadedBeforeBuild);
                    if(EditorUserBuildSettings.activeBuildTarget!=activeTargetBeforeBuild&&!EditorUserBuildSettings.SwitchActiveBuildTarget(activeTargetGroupBeforeBuild,activeTargetBeforeBuild))throw new InvalidOperationException("Failed to restore the previous active build target: "+activeTargetBeforeBuild+".");
                }
            }
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidDataException(target+" build failed: "+report.summary.result);
            Debug.Log(target+" validation build passed: "+output);
        }
        static byte[] ReadExistingBuildMetadata(string path)=>File.Exists(path)?File.ReadAllBytes(path):null;
        static void RestoreBuildMetadata(string path,byte[] previous)
        {
            if(previous!=null)
            {
                File.WriteAllBytes(path,previous);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            }
            else if(File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
        static void PrepareBuildInfo()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BuildInfoPath));
            var gitCommit=GitOutput("rev-parse HEAD");
            var gitStatus=GitOutput("status --porcelain");
            var buildInfoUtc=DateTime.UtcNow.ToString("O");
            File.WriteAllText(BuildInfoPath,buildInfoUtc);
            AssetDatabase.ImportAsset(BuildInfoPath,ImportAssetOptions.ForceSynchronousImport);
            var metadata=new DistributedBuildInfo.BuildMetadata
            {
                git_commit_hash=gitCommit,
                git_branch=GitOutput("branch --show-current"),
                git_dirty=gitStatus==null?null:(gitStatus.Length==0?"false":"true"),
                xr_hands_version=PackageVersion("com.unity.xr.hands"),
                xr_interaction_toolkit_version=PackageVersion("com.unity.xr.interaction.toolkit"),
                openxr_version=PackageVersion("com.unity.xr.openxr"),
                meta_openxr_version=PackageVersion("com.unity.xr.meta-openxr"),
                input_system_version=PackageVersion("com.unity.inputsystem")
            };
            File.WriteAllText(BuildMetadataPath,JsonUtility.ToJson(metadata,true));
            AssetDatabase.ImportAsset(BuildMetadataPath,ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Distributed BuildInfo UTC: "+buildInfoUtc);
        }
        static string PackageVersion(string packageName)=>UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
            .FirstOrDefault(package=>package.name==packageName)?.version;
        static string GitOutput(string arguments)
        {
            try
            {
                var start=new ProcessStartInfo(GitExecutable(),arguments){WorkingDirectory=Directory.GetParent(Application.dataPath).FullName,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                using(var process=Process.Start(start))
                {
                    if(process==null)return null;
                    var output=process.StandardOutput.ReadToEndAsync();
                    var error=process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(3000)){process.Kill();return null;}
                    if(process.ExitCode!=0)return null;
                    error.GetAwaiter().GetResult();
                    return output.GetAwaiter().GetResult().Trim();
                }
            }
            catch(Exception){return null;}
        }
        static string GitExecutable()
        {
#if UNITY_EDITOR_WIN
            try
            {
                using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GitForWindows"))
                {
                    var path=key?.GetValue("InstallPath") as string;
                    var executable=Path.Combine(path??string.Empty,"cmd","git.exe");
                    if(File.Exists(executable))return executable;
                }
            }
            catch(Exception){}
#endif
            foreach(var drive in Environment.GetLogicalDrives())
            {
                var executable=Path.Combine(drive,"Git","cmd","git.exe");
                if(File.Exists(executable))return executable;
            }
            return "git";
        }
        static InputActionProperty Action(string name,string type,string binding){var action=new InputAction(name,InputActionType.Value,expectedControlType:type);action.AddBinding(binding);return new InputActionProperty(action);}
        static InputActionReference Reference(InputActionAsset asset,string actionPath)
        {
            var action=asset.FindAction(actionPath,true);
            var reference=AssetDatabase.LoadAllAssetsAtPath(XriInputActionsPath).OfType<InputActionReference>().FirstOrDefault(x=>x.action!=null&&x.action.id==action.id);
            if(reference==null)throw new InvalidDataException("Missing InputActionReference sub-asset: "+actionPath);
            return reference;
        }
    }
}
