using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.ARFoundation;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class XriQuestSceneStructureTests
    {
        [Test]
        public void QuestScene_ContainsOneStandardRightHandUiPathAndDisabledLegacyPath()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Research/Scenes/PianoDistributedQuest.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var camera = Components<Camera>(roots).Single(x => x.CompareTag("MainCamera"));
                var passthroughManagers = camera.GetComponents<ARCameraManager>();
                Assert.That(passthroughManagers, Has.Length.EqualTo(1));
                Assert.That(passthroughManagers[0].enabled, Is.False);
                Assert.That(camera.backgroundColor.a, Is.Zero);
                Assert.That(roots.Any(x => x.name == "Passthrough"), Is.False);
                var eventSystems = Components<EventSystem>(roots);
                var interactionManagers = Components<XRInteractionManager>(roots);
                var inputManagers = Components<InputActionManager>(roots);
                var xrUiModules = Components<XRUIInputModule>(roots);
                var nearFarInteractors = Components<NearFarInteractor>(roots);
                var controllers = Components<XriHandUiInputController>(roots);
                var legacyModules = Components<MinimalHandUiInputModule>(roots);

                Assert.That(eventSystems.Length, Is.EqualTo(1));
                Assert.That(interactionManagers.Length, Is.EqualTo(1));
                Assert.That(inputManagers.Length, Is.EqualTo(1));
                Assert.That(xrUiModules.Length, Is.EqualTo(1));
                Assert.That(nearFarInteractors.Length, Is.EqualTo(1));
                Assert.That(controllers.Length, Is.EqualTo(1));
                Assert.That(legacyModules.Length, Is.EqualTo(1));
                Assert.That(controllers[0].Mode, Is.EqualTo(UiInputMode.XriStandard));
                Assert.That(legacyModules[0].enabled, Is.False);
                Assert.That(xrUiModules[0].enabled, Is.True);
                Assert.That(nearFarInteractors[0].handedness, Is.EqualTo(InteractorHandedness.Right));
                Assert.That(nearFarInteractors[0].enableUIInteraction, Is.True);
                Assert.That(nearFarInteractors[0].enableFarCasting, Is.True);
                Assert.That(nearFarInteractors[0].enableNearCasting, Is.False);
                Assert.That(inputManagers[0].actionAssets.Single().name, Is.EqualTo("XRI Default Input Actions"));

                var poseDriver = nearFarInteractors[0].GetComponent<TrackedPoseDriver>();
                Assert.That(poseDriver, Is.Not.Null);
                Assert.That(poseDriver.positionInput.reference.action.name, Is.EqualTo("Aim Position"));
                Assert.That(poseDriver.rotationInput.reference.action.name, Is.EqualTo("Aim Rotation"));
                Assert.That(poseDriver.trackingStateInput.reference.action.name, Is.EqualTo("Tracking State"));
                Assert.That(nearFarInteractors[0].GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty,
                    "The UI interactor must not add a second hand mesh.");
                Assert.That(nearFarInteractors[0].GetComponentsInChildren<MeshRenderer>(true), Is.Empty,
                    "Only the standard LineRenderer is expected on the UI interactor.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static T[] Components<T>(GameObject[] roots) where T : Component =>
            roots.SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }
}
