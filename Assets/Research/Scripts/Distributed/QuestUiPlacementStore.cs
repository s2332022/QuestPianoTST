using System;
using System.IO;
using UnityEngine;
namespace QuestPianoMotion.Research.Distributed
{
    [Serializable]
    public sealed class QuestUiPlacementData
    {
        public int version = 1;
        public string coordinateSpace = "KeyboardLocal";
        public string reference = "CalibratedKeyboardRoot";
        public string calibrationId;
        public Vector3 position;
        public Quaternion rotation;
    }
    public static class QuestUiPlacementStore
    {
        public static bool TryDecode(string json, out QuestUiPlacementData data)
        {
            data = null;
            try
            {
                var d = JsonUtility.FromJson<QuestUiPlacementData>(json);
                if (d == null || d.version != 1 || d.coordinateSpace != "KeyboardLocal" ||
                    d.reference != "CalibratedKeyboardRoot" || !QuestSpatialPlacement.IsFinite(d.position) ||
                    !QuestSpatialPlacement.IsFinite(d.rotation) || d.position.magnitude > 5f ||
                    Mathf.Abs(Quaternion.Dot(d.rotation, d.rotation) - 1f) > .01f) return false;
                // Require explicit fields: JsonUtility otherwise fills missing fields with defaults.
                foreach (var key in new[] { "version", "coordinateSpace", "reference", "position", "rotation" })
                    if (!json.Contains("\"" + key + "\"")) return false;
                data = d; return true;
            }
            catch (Exception) { return false; }
        }
        public static bool TryLoad(string path, out QuestUiPlacementData data)
        {
            data = null;
            try { return File.Exists(path) && TryDecode(File.ReadAllText(path), out data); }
            catch (Exception) { return false; }
        }
        public static void Save(string path, Transform panel, Transform calibratedKeyboard, string calibrationId = null)
        {
            var data = new QuestUiPlacementData {
                calibrationId = calibrationId,
                position = calibratedKeyboard.InverseTransformPoint(panel.position),
                rotation = Quaternion.Inverse(calibratedKeyboard.rotation) * panel.rotation };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
        public static bool TryResolve(QuestUiPlacementData data, Transform calibratedKeyboard, Transform head,
            float minimum, float maximum, out Pose pose, string calibrationId = null)
        {
            pose = default;
            if (data == null || calibratedKeyboard == null || head == null ||
                !TryDecode(JsonUtility.ToJson(data), out _) ||
                (!string.IsNullOrEmpty(calibrationId) && data.calibrationId != calibrationId)) return false;
            var p = calibratedKeyboard.TransformPoint(data.position);
            var q = calibratedKeyboard.rotation * data.rotation;
            var delta = p - head.position;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (!QuestSpatialPlacement.IsFinite(p) || !QuestSpatialPlacement.IsFinite(q) ||
                delta.magnitude < minimum || delta.magnitude > maximum ||
                Vector3.Angle(forward, delta) > 75 || Vector3.Dot(q * Vector3.up, Vector3.up) < .95f ||
                Vector3.Dot(q * Vector3.forward, delta.normalized) < .2f) return false;
            pose = new Pose(p, q); return true;
        }
    }
}
