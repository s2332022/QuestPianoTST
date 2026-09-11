using System.IO;
using System.Linq;
using QuestPianoMotion.Research;
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
    public static class PianoMinimalSceneBuilder
    {
        const string ScenePath = "Assets/Research/Scenes/PianoMinimalTest.unity";

        [MenuItem("Quest Piano Motion/Create Minimal Test Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var originObject = new GameObject("XR Origin");
            var origin = originObject.AddComponent<XROrigin>();
            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originObject.transform, false);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.016f, 0.025f, 1f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.allowHDR = false;
            cameraObject.AddComponent<AudioListener>();

            var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            poseDriver.positionInput = Action("HMD Position", "Vector3", "<XRHMD>/centerEyePosition");
            poseDriver.rotationInput = Action("HMD Rotation", "Quaternion", "<XRHMD>/centerEyeRotation");
            poseDriver.trackingStateInput = Action("HMD Tracking State", "Integer", "<XRHMD>/trackingState");

            origin.Camera = camera;
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;

            var runtimeObject = new GameObject("Research Runtime");
            var pianoRoot = new GameObject("Piano Root");
            var uiRoot = new GameObject("Research UI");
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<MinimalHandUiInputModule>();

            var runtime = runtimeObject.AddComponent<PianoResearchRuntime>();
            runtime.ConfigureMinimalScene(pianoRoot.transform, uiRoot.transform);
            EditorUtility.SetDirty(runtime);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.18f, 0.2f, 1f);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Failed to save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Created minimal research scene: " + ScenePath);
        }

        public static void ValidateScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var expectedRoots = new[] { "XR Origin", "Research Runtime", "Piano Root", "Research UI", "EventSystem" };
            var actualRoots = roots.Select(root => root.name).OrderBy(name => name).ToArray();
            var expectedSorted = expectedRoots.OrderBy(name => name).ToArray();
            Require(actualRoots.SequenceEqual(expectedSorted),
                "Root hierarchy differs: " + string.Join(", ", actualRoots));

            Require(FindComponents<Camera>(roots).Length == 1, "Scene must contain exactly one Camera.");
            Require(Camera.main != null && Camera.main.name == "Main Camera", "Main Camera tag/reference is invalid.");
            Require(FindComponents<EventSystem>(roots).Length == 1, "Scene must contain exactly one EventSystem.");
            Require(FindComponents<PianoResearchRuntime>(roots).Length == 1,
                "Scene must contain exactly one PianoResearchRuntime.");
            Require(FindComponents<MinimalHandUiInputModule>(roots).Length == 1,
                "Scene must contain exactly one MinimalHandUiInputModule.");
            Require(FindComponents<Collider>(roots).Length == 0, "Serialized scene contains a Collider.");
            Require(FindComponents<Rigidbody>(roots).Length == 0, "Serialized scene contains a Rigidbody.");
            Require(FindComponents<AudioSource>(roots).Length == 0, "Serialized scene contains an AudioSource.");
            Require(FindComponents<Light>(roots).Length == 0, "Serialized scene contains a Light.");

            var missingScripts = 0;
            var missingReferences = 0;
            foreach (var root in roots)
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var iterator = new SerializedObject(component).GetIterator();
                    while (iterator.NextVisible(true))
                    {
                        if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                            iterator.objectReferenceValue == null &&
                            !iterator.objectReferenceEntityIdValue.Equals(default(UnityEngine.EntityId)))
                            ++missingReferences;
                    }
                }
            }
            Require(missingScripts == 0, $"Missing Script count: {missingScripts}");
            Require(missingReferences == 0, $"Missing Reference count: {missingReferences}");
            Debug.Log("PianoMinimalTest validation passed: 5 roots, 0 missing scripts, 0 missing references, " +
                      "0 serialized colliders/rigidbodies/audio sources/lights.");
        }

        public static void BuildAndroidValidationPlayer()
        {
            var outputDirectory = Path.Combine(Path.GetTempPath(),
                "QuestPianoMotion-Android-" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "PianoMinimalTest.apk");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.CompressWithLz4
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidDataException("Android validation build failed: " + report.summary.result);
            Debug.Log($"Android validation build passed: {outputPath} ({report.summary.totalSize} bytes)");
        }

        static T[] FindComponents<T>(GameObject[] roots) where T : Component =>
            roots.SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        static InputActionProperty Action(string name, string controlType, string binding)
        {
            var action = new InputAction(name, InputActionType.Value, expectedControlType: controlType);
            action.AddBinding(binding);
            return new InputActionProperty(action);
        }
    }
}
