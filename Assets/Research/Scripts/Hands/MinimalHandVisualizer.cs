using UnityEngine;
using UnityEngine.Rendering;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class MinimalHandVisualizer : MonoBehaviour
    {
        const float JointSize = 0.009f;
        XRHandPoseProvider m_Hands;
        Mesh m_JointMesh;
        Material m_LeftMaterial;
        Material m_RightMaterial;
        Matrix4x4[] m_LeftMatrices;
        Matrix4x4[] m_RightMatrices;
        int m_LeftCount;
        int m_RightCount;

        public void Initialize(XRHandPoseProvider hands)
        {
            if (ReferenceEquals(m_Hands, hands)) return;
            if (m_Hands != null) m_Hands.DisplayFrameUpdated -= OnDisplayFrame;
            m_Hands = hands;
            if (m_Hands != null) m_Hands.DisplayFrameUpdated += OnDisplayFrame;
        }

        void Awake()
        {
            m_JointMesh = BuildOctahedron();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                enabled = false;
                return;
            }
            m_LeftMaterial = CreateMaterial(shader, new Color(0.15f, 0.75f, 1f, 1f));
            m_RightMaterial = CreateMaterial(shader, new Color(1f, 0.45f, 0.25f, 1f));
        }

        void LateUpdate()
        {
            if (m_JointMesh == null) return;
            if (m_LeftCount > 0)
                Graphics.DrawMeshInstanced(m_JointMesh, 0, m_LeftMaterial, m_LeftMatrices, m_LeftCount, null,
                    ShadowCastingMode.Off, false, gameObject.layer, null, LightProbeUsage.Off);
            if (m_RightCount > 0)
                Graphics.DrawMeshInstanced(m_JointMesh, 0, m_RightMaterial, m_RightMatrices, m_RightCount, null,
                    ShadowCastingMode.Off, false, gameObject.layer, null, LightProbeUsage.Off);
        }

        void OnDestroy()
        {
            if (m_Hands != null) m_Hands.DisplayFrameUpdated -= OnDisplayFrame;
            if (m_JointMesh != null) Destroy(m_JointMesh);
            if (m_LeftMaterial != null) Destroy(m_LeftMaterial);
            if (m_RightMaterial != null) Destroy(m_RightMaterial);
        }

        void OnDisplayFrame(HandPoseFrame frame)
        {
            EnsureCapacity(frame.LeftJoints.Length);
            var origin = ResearchServices.TrackingOrigin;
            var originMatrix = origin != null ? origin.localToWorldMatrix : Matrix4x4.identity;
            m_LeftCount = FillMatrices(frame.LeftJoints, frame.LeftTracked, originMatrix, m_LeftMatrices);
            m_RightCount = FillMatrices(frame.RightJoints, frame.RightTracked, originMatrix, m_RightMatrices);
        }

        void EnsureCapacity(int count)
        {
            if (m_LeftMatrices != null && m_LeftMatrices.Length >= count) return;
            m_LeftMatrices = new Matrix4x4[count];
            m_RightMatrices = new Matrix4x4[count];
        }

        static int FillMatrices(HandJointPose[] joints, bool tracked, Matrix4x4 origin,
            Matrix4x4[] destination)
        {
            if (!tracked) return 0;
            var count = 0;
            var scale = Vector3.one * JointSize;
            for (var i = 0; i < joints.Length; ++i)
            {
                if (!joints[i].PoseValid) continue;
                var pose = joints[i].Pose;
                destination[count++] = origin * Matrix4x4.TRS(pose.position, pose.rotation, scale);
            }
            return count;
        }

        static Material CreateMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            return material;
        }

        static Mesh BuildOctahedron()
        {
            var mesh = new Mesh { name = "Minimal Hand Joint" };
            mesh.vertices = new[]
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };
            mesh.triangles = new[]
            {
                0, 4, 3, 0, 2, 4, 0, 5, 2, 0, 3, 5,
                1, 3, 4, 1, 4, 2, 1, 2, 5, 1, 5, 3
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
