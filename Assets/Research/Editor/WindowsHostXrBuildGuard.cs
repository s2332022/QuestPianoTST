using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QuestPianoMotion.Research.Editor
{
    /// <summary>
    /// OpenXR's build helper preloads OpenXRSettings even when Standalone has no active loader.
    /// The desktop host is deliberately non-XR, so remove XR runtime settings after package
    /// preprocessors have run. Android builds are intentionally untouched.
    /// </summary>
    public sealed class WindowsHostXrBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder=>10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if(report.summary.platform!=BuildTarget.StandaloneWindows64)return;
            var source=PlayerSettings.GetPreloadedAssets();var filtered=new List<Object>(source.Length);var removed=0;
            foreach(var asset in source)
            {
                if(asset==null)continue;
                var typeName=asset.GetType().FullName;
                if(typeName=="UnityEngine.XR.OpenXR.OpenXRSettings"||typeName=="UnityEngine.XR.Management.XRGeneralSettings"){++removed;continue;}
                filtered.Add(asset);
            }
            if(removed==0)return;
            PlayerSettings.SetPreloadedAssets(filtered.ToArray());
            Debug.Log($"Windows Host XR build guard removed {removed} XR settings object(s) from Preloaded Assets.");
        }
    }
}

