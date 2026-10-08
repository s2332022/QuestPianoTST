using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEditor.Build;

namespace QuestPianoMotion.Research.Editor
{
    /// <summary>Additive generated-manifest patch. BLE and MIDI remain optional so UDP works independently.</summary>
    public sealed class AndroidBleMidiBuildGuard : IPostGenerateGradleAndroidProject
    {
        const string Android = "http://schemas.android.com/apk/res/android";
        public int callbackOrder => 10010;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) throw new BuildFailedException("Generated manifest missing: " + manifestPath);
            var document = new XmlDocument { PreserveWhitespace = true }; document.Load(manifestPath);
            AddPermissions(document); document.Save(manifestPath);
        }
        public static void AddPermissions(XmlDocument document)
        {
            var root = document.DocumentElement;
            if (root == null || root.Name != "manifest") throw new BuildFailedException("Invalid generated manifest");
            // Make the diagnostic service lookup reliable under Android package visibility.
            var queries = root.SelectSingleNode("queries") as XmlElement;
            if (queries == null) { queries = document.CreateElement("queries"); root.AppendChild(queries); }
            Ensure(document, queries, "package", "com.android.bluetoothmidiservice");
            Ensure(document, root, "uses-permission", "android.permission.BLUETOOTH_SCAN")
                .SetAttribute("usesPermissionFlags", Android, "neverForLocation");
            Ensure(document, root, "uses-permission", "android.permission.BLUETOOTH_CONNECT");
            Ensure(document, root, "uses-feature", "android.hardware.bluetooth_le").SetAttribute("required", Android, "false");
            Ensure(document, root, "uses-feature", "android.software.midi").SetAttribute("required", Android, "false");
        }
        static XmlElement Ensure(XmlDocument document, XmlElement root, string tag, string name)
        {
            foreach (XmlNode node in root.SelectNodes(tag))
                if (node is XmlElement element && element.GetAttribute("name", Android) == name) return element;
            var created = document.CreateElement(tag); created.SetAttribute("name", Android, name); root.PrependChild(created); return created;
        }
    }
}
