using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    /// <summary>Official XR Hands sample mesh/mapping, driven only by the provider's display copy.</summary>
    public sealed class DisplayHandSkeleton : MonoBehaviour
    {
        [SerializeField] bool m_Left;
        [SerializeField] List<JointToTransformReference> m_Joints;
        XRHandPoseProvider m_Provider;
        MinimalHandVisualizer m_Diagnostic;
        SkinnedMeshRenderer[] m_Renderers;
        Material m_Material;
        double m_LastFrame;
        public bool IsLeft => m_Left;
        public IReadOnlyList<JointToTransformReference> Joints => m_Joints;
        public bool Visible => m_Renderers != null && m_Renderers.Length > 0 && m_Renderers[0].enabled;
        public void Configure(bool left, List<JointToTransformReference> joints)
        { m_Left = left; m_Joints = joints; }
        public void Initialize(XRHandPoseProvider provider, MinimalHandVisualizer diagnostic)
        {
            if (m_Provider != null) m_Provider.DisplayFrameUpdated -= Apply;
            m_Provider = provider; m_Diagnostic = diagnostic;
            m_Joints.Sort((a, b) => Depth(a.jointTransform).CompareTo(Depth(b.jointTransform)));
            m_Renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var material = Resources.Load<Material>("CorrectionHands/DisplayHandOpaque");
            if (material != null)
            {
                m_Material = new Material(material);
                var color = m_Left ? new Color(.15f, .5f, .9f) : new Color(.9f, .5f, .3f);
                m_Material.SetColor("_BaseColor", color); m_Material.SetColor("_Color", color);
            }
            foreach (var renderer in m_Renderers)
            {
                if (m_Material != null) renderer.sharedMaterial = m_Material;
                renderer.updateWhenOffscreen = true;
                renderer.enabled = false;
            }
            if (m_Provider != null) m_Provider.DisplayFrameUpdated += Apply;
        }
        static int Depth(Transform t) { var d = 0; while (t != null) { ++d; t = t.parent; } return d; }
        public void Apply(HandPoseFrame display)
        {
            m_LastFrame = ResearchServices.Clock.AbsoluteSeconds;
            var origin = ResearchServices.TrackingOrigin;
            var visible = isActiveAndEnabled && origin != null && (m_Left ? display.LeftTracked : display.RightTracked) &&
                (m_Diagnostic == null || m_Diagnostic.Mode == HandVisualizationMode.SkinnedMesh);
            // Do not display stale fingers when any mapped joint is invalid.
            foreach (var joint in m_Joints)
            {
                var sample = display.FindJoint(m_Left, joint.xrHandJointID);
                if (!sample.PoseValid || !DisplayFingerSolver.Finite(sample.Pose.position)) visible = false;
            }
            if (visible)
                foreach (var joint in m_Joints)
                {
                    var pose = display.FindJoint(m_Left, joint.xrHandJointID).Pose;
                    joint.jointTransform.SetPositionAndRotation(origin.TransformPoint(pose.position), origin.rotation * pose.rotation);
                }
            foreach (var renderer in m_Renderers) renderer.enabled = visible;
        }
        void Update()
        {
            if (m_Renderers == null) return;
            if (ResearchServices.Clock.AbsoluteSeconds - m_LastFrame > .25 ||
                (m_Diagnostic != null && m_Diagnostic.Mode != HandVisualizationMode.SkinnedMesh)) Hide();
        }
        void Hide() { if (m_Renderers != null) foreach (var renderer in m_Renderers) renderer.enabled = false; }
        void OnDisable() => Hide();
        void OnDestroy()
        {
            if (m_Provider != null) m_Provider.DisplayFrameUpdated -= Apply;
            if (m_Material != null) Destroy(m_Material);
        }
    }
}
