using System;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    public static class ResearchServices
    {
        public static readonly MonotonicSessionClock Clock = new MonotonicSessionClock();
        public static Transform TrackingOrigin { get; internal set; }

        public static Transform FindTrackingOrigin()
        {
            var originType = Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
            if (originType != null)
            {
                var component = UnityEngine.Object.FindAnyObjectByType(originType) as Component;
                if (component != null) return component.transform;
            }
            var camera = Camera.main;
            return camera != null && camera.transform.parent != null && camera.transform.parent.parent != null
                ? camera.transform.parent.parent : null;
        }
    }
}
