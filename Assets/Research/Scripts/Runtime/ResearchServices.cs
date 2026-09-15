using Unity.XR.CoreUtils;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    public static class ResearchServices
    {
        public static readonly MonotonicSessionClock Clock = new MonotonicSessionClock();
        public static Transform TrackingOrigin { get; internal set; }

        public static Transform FindTrackingOrigin()
        {
            var xrOrigin = Object.FindAnyObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                if (xrOrigin.CameraFloorOffsetObject != null)
                    return xrOrigin.CameraFloorOffsetObject.transform;
                if (xrOrigin.Origin != null)
                    return xrOrigin.Origin.transform;
            }

            var camera = Camera.main;
            return camera != null && camera.transform.parent != null ? camera.transform.parent : null;
        }
    }
}
