using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed class QuestHmdPoseGate
    {
        public const int RequiredConsecutiveFrames = 3;
        public const double MinimumStartupSeconds = 0.5d;
        public const double FallbackSeconds = 5d;

        readonly double m_StartTime;
        readonly List<XRInputSubsystem> m_Inputs = new List<XRInputSubsystem>(4);
        bool m_HasInitialPose;
        Vector3 m_InitialPosition;
        Quaternion m_InitialRotation;
        int m_ConsecutiveFrames;
        bool m_FallbackLogged;

        public QuestHmdPoseGate(double startTime) { m_StartTime = startTime; }
        public int ConsecutiveFrames => m_ConsecutiveFrames;
        public bool UsedFallback { get; private set; }

        public bool TryGetReadyCamera(double now, out Camera camera)
        {
            camera = null;
            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (!QuestSpatialPlacement.TryResolveXrCamera(origin, out camera))
            {
                m_ConsecutiveFrames = 0;
                return false;
            }

            m_Inputs.Clear();
            SubsystemManager.GetSubsystems(m_Inputs);
            var inputRunning = false;
            for (var i = 0; i < m_Inputs.Count; ++i)
                if (m_Inputs[i] != null && m_Inputs[i].running) { inputRunning = true; break; }

            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            var headPoseValid = head.isValid &&
                head.TryGetFeatureValue(CommonUsages.devicePosition, out var headPosition) &&
                head.TryGetFeatureValue(CommonUsages.deviceRotation, out var headRotation) &&
                QuestSpatialPlacement.IsFinite(headPosition) && QuestSpatialPlacement.IsFinite(headRotation) &&
                (!head.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || tracked);

            var scaleValid = origin != null && QuestSpatialPlacement.IsNonZeroFiniteScale(origin.transform.lossyScale);
            var strictReady = Observe(now, inputRunning, headPoseValid, scaleValid,
                camera.transform.position, camera.transform.rotation);
            if (strictReady)
                return true;

            if (now - m_StartTime < FallbackSeconds || !scaleValid ||
                !QuestSpatialPlacement.IsFinite(camera.transform.position) ||
                !QuestSpatialPlacement.IsFinite(camera.transform.rotation))
                return false;

            UsedFallback = true;
            if (!m_FallbackLogged)
            {
                m_FallbackLogged = true;
                Debug.LogWarning("[QuestPlacement] HMD pose did not satisfy the strict gate within 5 seconds; using the finite XR Origin camera pose fallback.");
            }
            return true;
        }

        public bool Observe(double now, bool inputRunning, bool headPoseValid, bool scaleValid,
            Vector3 cameraPosition, Quaternion cameraRotation)
        {
            var finite = QuestSpatialPlacement.IsFinite(cameraPosition) && QuestSpatialPlacement.IsFinite(cameraRotation);
            if (!inputRunning || !headPoseValid || !scaleValid || !finite || now - m_StartTime < MinimumStartupSeconds)
            {
                m_ConsecutiveFrames = 0;
                return false;
            }

            if (!m_HasInitialPose)
            {
                m_HasInitialPose = true;
                m_InitialPosition = cameraPosition;
                m_InitialRotation = cameraRotation;
                m_ConsecutiveFrames = 0;
                return false;
            }

            var poseUpdated = (cameraPosition - m_InitialPosition).sqrMagnitude > 0.00000001f ||
                              Quaternion.Angle(cameraRotation, m_InitialRotation) > 0.001f;
            if (!poseUpdated)
            {
                m_ConsecutiveFrames = 0;
                return false;
            }

            ++m_ConsecutiveFrames;
            return m_ConsecutiveFrames >= RequiredConsecutiveFrames;
        }
    }

    public static class QuestSpatialPlacement
    {
        public static bool TryResolveXrCamera(XROrigin origin, out Camera camera)
        {
            camera = origin != null ? origin.Camera : null;
            return camera != null && camera.isActiveAndEnabled &&
                   camera.transform.IsChildOf(origin.transform) &&
                   IsFinite(camera.transform.position) && IsFinite(camera.transform.rotation) &&
                   IsNonZeroFiniteScale(origin.transform.lossyScale);
        }

        public static Vector3 HorizontalForward(Camera camera, XROrigin origin = null)
        {
            var forward = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up) : Vector3.zero;
            if (forward.sqrMagnitude < 0.000001f && origin != null)
                forward = Vector3.ProjectOnPlane(origin.transform.forward, Vector3.up);
            return forward.sqrMagnitude >= 0.000001f ? forward.normalized : Vector3.forward;
        }

        public static void PlaceUi(Transform target, Camera camera, XROrigin origin = null)
        {
            var forward = HorizontalForward(camera, origin);
            target.SetPositionAndRotation(camera.transform.position + forward * 1.1f + Vector3.up * -0.1f,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        // World metres, with KeyboardRoot +X lateral, +Y normal, +Z rear/depth.
        // Keep the existing shared PlaceUi behavior for other/diagnostic scenes.
        public static void PlacePerformanceUi(Transform target, Vector3 keyboardCenterWorld,
            Quaternion keyboardRotation)
        {
            target.SetPositionAndRotation(keyboardCenterWorld +
                keyboardRotation * new Vector3(0.15f, 0.40f, 0.30f),
                keyboardRotation * Quaternion.Euler(30f, 0f, 0f));
        }

        public static void PlacePiano(Transform target, Camera camera, XROrigin origin = null)
        {
            var forward = HorizontalForward(camera, origin);
            target.SetPositionAndRotation(camera.transform.position + forward * 0.75f + Vector3.up * -0.35f,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        public static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        public static bool IsFinite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        public static bool IsNonZeroFiniteScale(Vector3 value) => IsFinite(value) &&
            Mathf.Abs(value.x) > 0.000001f && Mathf.Abs(value.y) > 0.000001f && Mathf.Abs(value.z) > 0.000001f;
    }
}