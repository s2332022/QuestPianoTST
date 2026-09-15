using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class QuestSpatialDiagnostics : MonoBehaviour
    {
        const double DurationSeconds = 10d;
        readonly List<XRInputSubsystem> m_InputSubsystems = new List<XRInputSubsystem>(4);
        readonly StringBuilder m_Buffer = new StringBuilder(8192);
        XRHandPoseProvider m_Hands;
        XROrigin m_XrOrigin;
        XRInputSubsystem m_Input;
        double m_StopTime;
        double m_NextLog;
        int m_TrackingOriginUpdatedCount;
        bool m_LoggedCameras;
        bool m_LoggedText;

        public void Initialize(XRHandPoseProvider hands) { m_Hands = hands; }

        void OnEnable()
        {
            m_StopTime = Time.realtimeSinceStartupAsDouble + DurationSeconds;
            m_NextLog = 0d;
            m_XrOrigin = FindAnyObjectByType<XROrigin>();
            TryBindInputSubsystem();
        }

        void OnDisable() { BindInputSubsystem(null); }

        void Update()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (now > m_StopTime) return;
            if (m_Input == null || !m_Input.running) TryBindInputSubsystem();
            if (now < m_NextLog) return;
            m_NextLog = now + 1d;
            LogSnapshot(now);
        }

        void TryBindInputSubsystem()
        {
            m_InputSubsystems.Clear();
            SubsystemManager.GetSubsystems(m_InputSubsystems);
            XRInputSubsystem selected = null;
            for (var i = 0; i < m_InputSubsystems.Count; ++i)
                if (m_InputSubsystems[i] != null && m_InputSubsystems[i].running) { selected = m_InputSubsystems[i]; break; }
            if (selected == null && m_InputSubsystems.Count > 0) selected = m_InputSubsystems[0];
            BindInputSubsystem(selected);
        }

        void BindInputSubsystem(XRInputSubsystem input)
        {
            if (ReferenceEquals(m_Input, input)) return;
            if (m_Input != null) m_Input.trackingOriginUpdated -= OnTrackingOriginUpdated;
            m_Input = input;
            if (m_Input != null) m_Input.trackingOriginUpdated += OnTrackingOriginUpdated;
        }

        void OnTrackingOriginUpdated(XRInputSubsystem input) { ++m_TrackingOriginUpdatedCount; }

        void LogSnapshot(double now)
        {
            if (m_XrOrigin == null) m_XrOrigin = FindAnyObjectByType<XROrigin>();
            if (m_Hands == null) m_Hands = FindAnyObjectByType<XRHandPoseProvider>();
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            var camera = ResolveXrCamera();
            var cameraOffset = m_XrOrigin != null && m_XrOrigin.CameraFloorOffsetObject != null ? m_XrOrigin.CameraFloorOffsetObject.transform : null;
            var canvas = FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            var pianoRootObject = GameObject.Find("Piano Root");
            var keyboard = FindAnyObjectByType<VirtualPianoKeyboard>();
            var calibration = FindAnyObjectByType<PianoCalibrationManager>();

            m_Buffer.Clear();
            m_Buffer.Append("[QuestSpatialDiag] t=").Append(now.ToString("F3"));
            m_Buffer.Append(" xrInit=").Append(manager != null && manager.isInitializationComplete);
            m_Buffer.Append(" loader=").Append(manager != null && manager.activeLoader != null ? manager.activeLoader.name : "none");
            m_Buffer.Append(" inputRunning=").Append(m_Input != null && m_Input.running);
            m_Buffer.Append(" currentMode=").Append(m_Input != null ? m_Input.GetTrackingOriginMode().ToString() : "none");
            m_Buffer.Append(" supportedModes=").Append(m_Input != null ? m_Input.GetSupportedTrackingOriginModes().ToString() : "none");
            m_Buffer.Append(" originEvents=").Append(m_TrackingOriginUpdatedCount);
            if (m_XrOrigin != null)
            {
                m_Buffer.Append(" xrRequested=").Append(m_XrOrigin.RequestedTrackingOriginMode);
                m_Buffer.Append(" xrCurrent=").Append(m_XrOrigin.CurrentTrackingOriginMode);
                m_Buffer.Append(" cameraYOffset=").Append(m_XrOrigin.CameraYOffset.ToString("F4"));
                m_Buffer.Append(" floorOffset=").Append(cameraOffset != null ? Path(cameraOffset) : "none");
                m_Buffer.Append(" originProperty=").Append(m_XrOrigin.Origin != null ? Path(m_XrOrigin.Origin.transform) : "none");
            }
            m_Buffer.Append(" calibrationValid=").Append(calibration != null && calibration.Current != null && calibration.Current.valid);
            m_Buffer.Append(" calibrationFile=").Append(calibration != null && File.Exists(calibration.PersistentPath));
            AppendTransform("XR Origin", m_XrOrigin != null ? m_XrOrigin.transform : null);
            AppendTransform("Camera Offset", cameraOffset);
            AppendTransform("Main Camera", camera != null ? camera.transform : null);
            AppendTransform("Hand Visualizer", m_Hands != null ? m_Hands.transform : null);
            AppendTransform("Research UI", FindAnyObjectByType<DistributedQuestUi>(FindObjectsInactive.Include)?.transform);
            AppendTransform("World Canvas", canvas != null && canvas.renderMode == RenderMode.WorldSpace ? canvas.transform : null);
            AppendTransform("Piano Root", pianoRootObject != null ? pianoRootObject.transform : null);
            AppendTransform("Keyboard", keyboard != null ? keyboard.KeyboardRoot : null);
            AppendRelative("uiCamera", camera, canvas != null ? canvas.transform : null);
            AppendRelative("pianoCamera", camera, keyboard != null ? keyboard.KeyboardRoot : null);
            AppendWrist("left", true, camera, cameraOffset);
            AppendWrist("right", false, camera, cameraOffset);
            m_Buffer.Append(" counts ui=").Append(FindObjectsByType<DistributedQuestUi>(FindObjectsInactive.Include).Length);
            m_Buffer.Append(" canvas=").Append(CountWorldCanvases());
            m_Buffer.Append(" pianoRoot=").Append(CountNamed("Piano Root"));
            m_Buffer.Append(" keyboard=").Append(FindObjectsByType<VirtualPianoKeyboard>(FindObjectsInactive.Include).Length);
            m_Buffer.Append(" visualizer=").Append(FindObjectsByType<MinimalHandVisualizer>(FindObjectsInactive.Include).Length);
            m_Buffer.Append(" hands=").Append(FindObjectsByType<XRHandPoseProvider>(FindObjectsInactive.Include).Length);
            Debug.Log(m_Buffer.ToString(), this);
            if (!m_LoggedCameras) { m_LoggedCameras = true; LogCameras(camera); }
            if (!m_LoggedText) { m_LoggedText = true; LogText(canvas, camera); }
        }

        Camera ResolveXrCamera() => m_XrOrigin != null && m_XrOrigin.Camera != null ? m_XrOrigin.Camera : Camera.main;

        void AppendWrist(string label, bool left, Camera camera, Transform cameraOffset)
        {
            var frame = m_Hands != null ? m_Hands.LatestDisplayFrame : null;
            if (frame == null) { m_Buffer.Append(' ').Append(label).Append("Wrist=none"); return; }
            var joint = frame.FindJoint(left, XRHandJointID.Wrist);
            if (!joint.PoseValid) { m_Buffer.Append(' ').Append(label).Append("Wrist=invalid"); return; }
            var world = TransformPose(ResearchServices.TrackingOrigin, joint.Pose);
            var offsetWorld = TransformPose(cameraOffset, joint.Pose);
            m_Buffer.Append(' ').Append(label).Append("WristLocal=").Append(V(joint.Pose.position));
            m_Buffer.Append(' ').Append(label).Append("WristWorld=").Append(V(world.position));
            m_Buffer.Append(' ').Append(label).Append("WristOffsetWorld=").Append(V(offsetWorld.position));
            m_Buffer.Append(' ').Append(label).Append("WristCamera=").Append(camera != null ? V(camera.transform.InverseTransformPoint(world.position)) : "none");
            m_Buffer.Append(' ').Append(label).Append("WristOffsetCamera=").Append(camera != null ? V(camera.transform.InverseTransformPoint(offsetWorld.position)) : "none");
        }

        void AppendRelative(string label, Camera camera, Transform target)
        {
            m_Buffer.Append(' ').Append(label).Append('=').Append(camera != null && target != null ? V(camera.transform.InverseTransformPoint(target.position)) : "none");
        }

        void AppendTransform(string label, Transform target)
        {
            m_Buffer.Append("\n  ").Append(label).Append(": ");
            if (target == null) { m_Buffer.Append("none"); return; }
            m_Buffer.Append("path=").Append(Path(target));
            m_Buffer.Append(" parent=").Append(target.parent != null ? target.parent.name : "none");
            m_Buffer.Append(" localPos=").Append(V(target.localPosition));
            m_Buffer.Append(" worldPos=").Append(V(target.position));
            m_Buffer.Append(" localRot=").Append(Q(target.localRotation));
            m_Buffer.Append(" worldRot=").Append(Q(target.rotation));
            m_Buffer.Append(" localScale=").Append(V(target.localScale));
            m_Buffer.Append(" lossyScale=").Append(V(target.lossyScale));
        }

        void LogCameras(Camera resolved)
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
            var cameraMain = Camera.main;
            var xrCamera = m_XrOrigin != null ? m_XrOrigin.Camera : null;
            var builder = new StringBuilder(2048);
            builder.Append("[QuestCameraDiag] enabled=").Append(CountEnabled(cameras));
            builder.Append(" mainTagged=").Append(CountMainTagged(cameras));
            builder.Append(" same main/xr/ui/piano=").Append(cameraMain == xrCamera && xrCamera == resolved);
            for (var i = 0; i < cameras.Length; ++i)
            {
                var c = cameras[i];
                builder.Append("\n  entityId=").Append(c.GetEntityId().ToString()).Append(" path=").Append(Path(c.transform));
                builder.Append(" pos=").Append(V(c.transform.position)).Append(" rot=").Append(Q(c.transform.rotation));
                builder.Append(" enabled=").Append(c.isActiveAndEnabled).Append(" tag=").Append(c.tag);
            }
            Debug.Log(builder.ToString(), this);
        }

        void LogText(Canvas canvas, Camera camera)
        {
            var legacy = FindAnyObjectByType<Text>(FindObjectsInactive.Include);
            var tmp = FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            var builder = new StringBuilder(4096);
            builder.Append("[QuestTextDiag]");
            if (legacy != null)
            {
                var material = legacy.material;
                builder.Append(" legacy name=").Append(legacy.name).Append(" value=").Append(legacy.text);
                builder.Append(" active=").Append(legacy.gameObject.activeInHierarchy).Append(" enabled=").Append(legacy.enabled);
                builder.Append(" font=").Append(legacy.font != null ? legacy.font.name : "null");
                builder.Append(" material=").Append(material != null ? material.name : "null");
                builder.Append(" shader=").Append(material != null && material.shader != null ? material.shader.name : "null");
                builder.Append(" fontSize=").Append(legacy.fontSize).Append(" color=").Append(legacy.color);
                AppendUiState(builder, legacy.rectTransform, legacy.transform, canvas, camera, material);
            }
            if (tmp != null)
            {
                var material = tmp.fontSharedMaterial;
                builder.Append(" tmp name=").Append(tmp.name).Append(" value=").Append(tmp.text);
                builder.Append(" active=").Append(tmp.gameObject.activeInHierarchy).Append(" enabled=").Append(tmp.enabled);
                builder.Append(" font=").Append(tmp.font != null ? tmp.font.name : "null");
                builder.Append(" material=").Append(material != null ? material.name : "null");
                builder.Append(" shader=").Append(material != null && material.shader != null ? material.shader.name : "null");
                builder.Append(" fontSize=").Append(tmp.fontSize).Append(" color=").Append(tmp.color);
                AppendUiState(builder, tmp.rectTransform, tmp.transform, canvas, camera, material);
            }
            Debug.Log(builder.ToString(), this);
        }

        static void AppendUiState(StringBuilder builder, RectTransform rect, Transform target, Canvas canvas, Camera camera, Material material)
        {
            var group = target.GetComponentInParent<CanvasGroup>(true);
            builder.Append(" rect=").Append(rect.rect.width.ToString("F1")).Append('x').Append(rect.rect.height.ToString("F1"));
            builder.Append(" localPos=").Append(V(rect.localPosition)).Append(" localScale=").Append(V(rect.localScale));
            builder.Append(" parent=").Append(target.parent != null ? target.parent.name : "none");
            builder.Append(" canvas=").Append(canvas != null ? canvas.renderMode.ToString() : "none");
            builder.Append(" sorting=").Append(canvas != null ? canvas.sortingOrder : 0);
            builder.Append(" layer=").Append(target.gameObject.layer).Append(" cameraMask=").Append(camera != null ? camera.cullingMask : 0);
            builder.Append(" canvasGroupAlpha=").Append(group != null ? group.alpha.ToString("F3") : "none");
            builder.Append(" mask=").Append(target.GetComponentInParent<Mask>(true) != null);
            builder.Append(" rectMask=").Append(target.GetComponentInParent<RectMask2D>(true) != null);
            builder.Append(" queue=").Append(material != null ? material.renderQueue : 0);
        }

        static Pose TransformPose(Transform origin, Pose local) => origin == null ? local : new Pose(origin.TransformPoint(local.position), origin.rotation * local.rotation);

        static int CountWorldCanvases()
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include); var count = 0;
            for (var i = 0; i < canvases.Length; ++i) if (canvases[i].renderMode == RenderMode.WorldSpace) ++count;
            return count;
        }

        static int CountNamed(string objectName)
        {
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include); var count = 0;
            for (var i = 0; i < transforms.Length; ++i) if (transforms[i].name == objectName) ++count;
            return count;
        }

        static int CountEnabled(Camera[] cameras) { var count = 0; for (var i = 0; i < cameras.Length; ++i) if (cameras[i].isActiveAndEnabled) ++count; return count; }
        static int CountMainTagged(Camera[] cameras) { var count = 0; for (var i = 0; i < cameras.Length; ++i) if (cameras[i].CompareTag("MainCamera")) ++count; return count; }

        static string Path(Transform target)
        {
            if (target == null) return "none"; var value = target.name;
            while (target.parent != null) { target = target.parent; value = target.name + "/" + value; }
            return value;
        }

        static string V(Vector3 value) => $"({value.x:F4},{value.y:F4},{value.z:F4})";
        static string Q(Quaternion value) => $"({value.x:F4},{value.y:F4},{value.z:F4},{value.w:F4})";
    }
}