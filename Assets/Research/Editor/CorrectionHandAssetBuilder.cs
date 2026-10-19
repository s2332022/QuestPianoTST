using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Editor
{
    public static class CorrectionHandAssetBuilder
    {
        [MenuItem("Quest Piano Motion/Prepare Correction Hand Mesh Assets")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/Research/Resources/CorrectionHands");
            const string materialPath = "Assets/Research/Resources/CorrectionHands/DisplayHandOpaque.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new System.InvalidOperationException("Official URP Unlit shader missing.");
                material = new Material(shader) { name = "Display Hand Opaque" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            foreach (var side in new[] { "Left", "Right" })
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Samples/XR Hands/1.8.1/HandVisualizer/Prefabs/" + side + " Hand Tracking.prefab");
                if (source == null) throw new FileNotFoundException("Official XR Hands sample mesh missing: " + side);
                var staging = new GameObject("Inactive display asset staging"); staging.SetActive(false);
                var instance = Object.Instantiate(source, staging.transform, false);
                try
                {
                    var driver = instance.GetComponent<XRHandSkeletonDriver>();
                    var references = new List<JointToTransformReference>(driver.jointTransformReferences);
                    // Strip every raw-subsystem consumer from the derived prefab; the imported sample is untouched.
                    foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
                    var display = instance.AddComponent<DisplayHandSkeleton>();
                    display.Configure(side == "Left", references);
                    foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    }
                    instance.name = side + " Display Hand";
                    PrefabUtility.SaveAsPrefabAsset(instance, "Assets/Research/Resources/CorrectionHands/" + side + "DisplayHand.prefab");
                }
                finally { Object.DestroyImmediate(staging); }
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }
    }
}
