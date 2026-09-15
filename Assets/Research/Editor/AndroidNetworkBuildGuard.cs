using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace QuestPianoMotion.Research.Editor
{
    /// <summary>Enforces and verifies the permissions required by Quest UDP networking.</summary>
    public sealed class AndroidNetworkBuildGuard : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject, IPostprocessBuildWithReport
    {
        const string OpenXrSettingsPath = "Assets/XR/Settings/OpenXR Package Settings.asset";
        const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        const string InternetPermission = "android.permission.INTERNET";
        const string NetworkStatePermission = "android.permission.ACCESS_NETWORK_STATE";

        public int callbackOrder => 10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            if (!PlayerSettings.Android.forceInternetPermission)
                throw new BuildFailedException("Android Player Settings > Internet Access must be Require.");

            AssertMetaDoesNotRemoveInternetPermission();
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
                throw new BuildFailedException("Unity library AndroidManifest.xml was not generated: " + manifestPath);

            var document = new XmlDocument { PreserveWhitespace = true };
            document.Load(manifestPath);
            var manifest = document.DocumentElement;
            if (manifest == null || manifest.Name != "manifest")
                throw new BuildFailedException("Generated AndroidManifest.xml has no manifest root.");

            EnsurePermission(document, manifest, InternetPermission);
            EnsurePermission(document, manifest, NetworkStatePermission);
            document.Save(manifestPath);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android)
                VerifyBuiltApk(report.summary.outputPath);
        }

        static void EnsurePermission(XmlDocument document, XmlElement manifest, string permission)
        {
            var nodes = manifest.SelectNodes("uses-permission");
            if (nodes != null)
                foreach (XmlNode node in nodes)
                    if (node.Attributes?["name", AndroidNamespace]?.Value == permission)
                        return;

            var element = document.CreateElement("uses-permission");
            element.SetAttribute("name", AndroidNamespace, permission);
            manifest.PrependChild(element);
        }

        public static void VerifyBuiltApk(string apkPath)
        {
            if (!PlayerSettings.Android.forceInternetPermission)
                throw new BuildFailedException("Android Player Settings > Internet Access is not Require.");
            AssertMetaDoesNotRemoveInternetPermission();

            var startInfo = new ProcessStartInfo
            {
                FileName = FindAapt(),
                Arguments = "dump permissions \"" + apkPath + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process == null)
                throw new BuildFailedException("Could not start aapt permission verification.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new BuildFailedException("aapt permission verification failed: " + error);
            if (!output.Contains(InternetPermission) || !output.Contains(NetworkStatePermission))
                throw new BuildFailedException("APK is missing required network permissions.\n" + output);

            UnityEngine.Debug.Log("Android network validation passed: INTERNET, ACCESS_NETWORK_STATE, forceRemoveInternetPermission=false");
        }

        static void AssertMetaDoesNotRemoveInternetPermission()
        {
            var found = false;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(OpenXrSettingsPath))
            {
                if (asset == null)
                    continue;
                var property = new SerializedObject(asset).FindProperty("forceRemoveInternetPermission");
                if (property == null)
                    continue;
                found = true;
                if (property.boolValue)
                    throw new BuildFailedException("Meta Quest Support forceRemoveInternetPermission must be false.");
            }
            if (!found)
                throw new BuildFailedException("Meta Quest Support forceRemoveInternetPermission setting was not found.");
        }

        static string FindAapt()
        {
            var editorData = Path.GetFullPath(Path.Combine(EditorApplication.applicationPath, "..", "Data"));
            var buildTools = Path.Combine(editorData, "PlaybackEngines", "AndroidPlayer", "SDK", "build-tools");
            if (!Directory.Exists(buildTools))
                throw new BuildFailedException("Android SDK build-tools directory was not found: " + buildTools);
            var result = Directory.GetFiles(buildTools, "aapt.exe", SearchOption.AllDirectories)
                .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (string.IsNullOrEmpty(result))
                throw new BuildFailedException("aapt.exe was not found under: " + buildTools);
            return result;
        }
    }
}
