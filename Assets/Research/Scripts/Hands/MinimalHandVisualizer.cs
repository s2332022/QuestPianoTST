using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    public enum HandVisualizationMode
    {
        GameObjectsDiagnostic,
        GpuInstanced
    }

    [DisallowMultipleComponent]
    public sealed class MinimalHandVisualizer : MonoBehaviour
    {
        const float NormalJointDiameter = 0.02f;
        const float FingertipDiameter = 0.03f;

        [SerializeField] HandVisualizationMode m_Mode = HandVisualizationMode.GameObjectsDiagnostic;
        [SerializeField, Min(1f)] float m_DiagnosticLogIntervalSeconds = 1f;

        XRHandPoseProvider m_Hands;
        Mesh m_JointMesh;
        Shader m_Shader;
        Material m_LeftGpuMaterial;
        Material m_RightGpuMaterial;
        Material m_LeftDiagnosticMaterial;
        Material m_RightDiagnosticMaterial;
        Transform m_DiagnosticRoot;
        Transform[] m_LeftMarkers;
        Transform[] m_RightMarkers;
        Renderer[] m_LeftRenderers;
        Renderer[] m_RightRenderers;
        Matrix4x4[] m_LeftMatrices;
        Matrix4x4[] m_RightMatrices;
        int m_LeftCount;
        int m_RightCount;
        int m_LeftValid;
        int m_RightValid;
        long m_DisplayEventCount;
        long m_DrawCallCount;
        bool m_LeftTracked;
        bool m_RightTracked;
        float m_NextDiagnosticLog;

        public HandVisualizationMode Mode => m_Mode;
        public long DisplayEventCount => m_DisplayEventCount;
        public int LeftValidJointCount => m_LeftValid;
        public int RightValidJointCount => m_RightValid;
        public bool LeftTracked => m_LeftTracked;
        public bool RightTracked => m_RightTracked;
        public bool MeshReady => m_JointMesh != null;
        public bool MaterialsReady => m_LeftDiagnosticMaterial != null && m_RightDiagnosticMaterial != null;
        public string ShaderName => m_Shader != null ? m_Shader.name : "Unavailable";

        public void Initialize(XRHandPoseProvider hands)
        {
            if (ReferenceEquals(m_Hands, hands))
                return;
            if (m_Hands != null)
                m_Hands.DisplayFrameUpdated -= OnDisplayFrame;
            m_Hands = hands;
            if (m_Hands != null)
                m_Hands.DisplayFrameUpdated += OnDisplayFrame;
        }

        public void SetMode(HandVisualizationMode mode)
        {
            m_Mode = mode;
            SetDiagnosticRenderersEnabled(mode == HandVisualizationMode.GameObjectsDiagnostic);
        }

        public void UseGpuInstanced() => SetMode(HandVisualizationMode.GpuInstanced);
        public void UseGameObjectsDiagnostic() => SetMode(HandVisualizationMode.GameObjectsDiagnostic);

        void Awake()
        {
            m_JointMesh = BuildOctahedron();
            m_Shader = Resources.Load<Shader>("ResearchUnlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (m_Shader == null)
            {
                Debug.LogError("[HandVisual] Required shader not found: Universal Render Pipeline/Unlit", this);
                enabled = false;
                return;
            }

            m_LeftGpuMaterial = CreateMaterial(m_Shader, new Color(0.05f, 0.45f, 1f, 1f), true);
            m_RightGpuMaterial = CreateMaterial(m_Shader, new Color(1f, 0.12f, 0.08f, 1f), true);
            m_LeftDiagnosticMaterial = CreateMaterial(m_Shader, new Color(0.05f, 0.45f, 1f, 1f), false);
            m_RightDiagnosticMaterial = CreateMaterial(m_Shader, new Color(1f, 0.12f, 0.08f, 1f), false);

            var jointCount = (int)XRHandJointID.EndMarker - (int)XRHandJointID.BeginMarker;
            m_LeftMatrices = new Matrix4x4[jointCount];
            m_RightMatrices = new Matrix4x4[jointCount];
            CreateDiagnosticMarkers(jointCount);
            SetMode(m_Mode);
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextDiagnosticLog)
                return;
            m_NextDiagnosticLog = Time.unscaledTime + Mathf.Max(1f, m_DiagnosticLogIntervalSeconds);

            var camera = Camera.main;
            var layerVisible = camera != null && (camera.cullingMask & (1 << gameObject.layer)) != 0;
            var origin = ResearchServices.TrackingOrigin;
            Debug.Log(
                $"[HandVisual] mode={m_Mode} events={m_DisplayEventCount} drawCalls={m_DrawCallCount} " +
                $"tracked=({m_LeftTracked},{m_RightTracked}) valid=({m_LeftValid},{m_RightValid}) " +
                $"drawn=({m_LeftCount},{m_RightCount}) mesh={MeshReady} materials={MaterialsReady} " +
                $"shader={ShaderName} layer={LayerMask.LayerToName(gameObject.layer)}({gameObject.layer}) " +
                $"cameraMask={(camera != null ? camera.cullingMask : 0)} layerVisible={layerVisible} " +
                $"origin={(origin != null ? origin.name : "none")} " +
                $"parentPos={transform.position} parentRot={transform.rotation.eulerAngles} parentScale={transform.lossyScale}",
                this);
        }

        void LateUpdate()
        {
            if (m_Mode != HandVisualizationMode.GpuInstanced || m_JointMesh == null)
                return;

            if (m_LeftCount > 0 && m_LeftGpuMaterial != null)
            {
                Graphics.DrawMeshInstanced(m_JointMesh, 0, m_LeftGpuMaterial, m_LeftMatrices, m_LeftCount, null,
                    ShadowCastingMode.Off, false, gameObject.layer, null, LightProbeUsage.Off);
                ++m_DrawCallCount;
            }

            if (m_RightCount > 0 && m_RightGpuMaterial != null)
            {
                Graphics.DrawMeshInstanced(m_JointMesh, 0, m_RightGpuMaterial, m_RightMatrices, m_RightCount, null,
                    ShadowCastingMode.Off, false, gameObject.layer, null, LightProbeUsage.Off);
                ++m_DrawCallCount;
            }
        }

        void OnDestroy()
        {
            if (m_Hands != null)
                m_Hands.DisplayFrameUpdated -= OnDisplayFrame;
            if (m_JointMesh != null)
                Destroy(m_JointMesh);
            DestroyMaterial(m_LeftGpuMaterial);
            DestroyMaterial(m_RightGpuMaterial);
            DestroyMaterial(m_LeftDiagnosticMaterial);
            DestroyMaterial(m_RightDiagnosticMaterial);
        }

        void OnDisplayFrame(HandPoseFrame frame)
        {
            ++m_DisplayEventCount;
            m_LeftTracked = frame.LeftTracked;
            m_RightTracked = frame.RightTracked;

            var origin = ResearchServices.TrackingOrigin;
            var originMatrix = origin != null ? origin.localToWorldMatrix : Matrix4x4.identity;
            m_LeftCount = FillMatrices(frame.LeftJoints, frame.LeftTracked, originMatrix, m_LeftMatrices, out m_LeftValid);
            m_RightCount = FillMatrices(frame.RightJoints, frame.RightTracked, originMatrix, m_RightMatrices, out m_RightValid);

            if (m_Mode == HandVisualizationMode.GameObjectsDiagnostic)
            {
                UpdateDiagnosticMarkers(frame.LeftJoints, frame.LeftTracked, originMatrix, m_LeftMarkers, m_LeftRenderers);
                UpdateDiagnosticMarkers(frame.RightJoints, frame.RightTracked, originMatrix, m_RightMarkers, m_RightRenderers);
            }
        }

        void CreateDiagnosticMarkers(int jointCount)
        {
            var root = new GameObject("Hand Joints Diagnostic");
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
            m_DiagnosticRoot = root.transform;
            m_LeftMarkers = new Transform[jointCount];
            m_RightMarkers = new Transform[jointCount];
            m_LeftRenderers = new Renderer[jointCount];
            m_RightRenderers = new Renderer[jointCount];

            for (var i = 0; i < jointCount; ++i)
            {
                var id = (XRHandJointID)((int)XRHandJointID.BeginMarker + i);
                CreateMarker($"Left {id}", id, m_LeftDiagnosticMaterial, m_LeftMarkers, m_LeftRenderers, i);
                CreateMarker($"Right {id}", id, m_RightDiagnosticMaterial, m_RightMarkers, m_RightRenderers, i);
            }
        }

        void CreateMarker(string markerName, XRHandJointID id, Material material,
            Transform[] transforms, Renderer[] renderers, int index)
        {
            var marker = new GameObject(markerName, typeof(MeshFilter), typeof(MeshRenderer));
            marker.layer = gameObject.layer;
            marker.transform.SetParent(m_DiagnosticRoot, false);
            marker.GetComponent<MeshFilter>().sharedMesh = m_JointMesh;
            var renderer = marker.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            transforms[index] = marker.transform;
            renderers[index] = renderer;
        }

        static void UpdateDiagnosticMarkers(HandJointPose[] joints, bool tracked, Matrix4x4 origin,
            Transform[] markers, Renderer[] renderers)
        {
            if (markers == null || renderers == null)
                return;

            for (var i = 0; i < markers.Length; ++i)
            {
                var visible = tracked && i < joints.Length && joints[i].PoseValid;
                renderers[i].enabled = visible;
                if (!visible)
                    continue;

                var joint = joints[i];
                var matrix = origin * Matrix4x4.TRS(joint.Pose.position, joint.Pose.rotation, Vector3.one);
                markers[i].SetPositionAndRotation(matrix.GetPosition(), matrix.rotation);
                SetWorldDiameter(markers[i], IsFingertip(joint.JointId) ? FingertipDiameter : NormalJointDiameter);
            }
        }

        static int FillMatrices(HandJointPose[] joints, bool tracked, Matrix4x4 origin,
            Matrix4x4[] destination, out int validCount)
        {
            validCount = 0;
            if (!tracked || destination == null)
                return 0;

            var count = 0;
            for (var i = 0; i < joints.Length; ++i)
            {
                if (!joints[i].PoseValid)
                    continue;
                ++validCount;
                var pose = joints[i].Pose;
                var radius = (IsFingertip(joints[i].JointId) ? FingertipDiameter : NormalJointDiameter) * 0.5f;
                destination[count++] = origin * Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one * radius);
            }
            return count;
        }

        static bool IsFingertip(XRHandJointID id)
        {
            return id == XRHandJointID.ThumbTip || id == XRHandJointID.IndexTip ||
                   id == XRHandJointID.MiddleTip || id == XRHandJointID.RingTip ||
                   id == XRHandJointID.LittleTip;
        }

        static void SetWorldDiameter(Transform target, float diameter)
        {
            var radius = diameter * 0.5f;
            var parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
            target.localScale = new Vector3(
                radius / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
                radius / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
                radius / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f));
        }

        void SetDiagnosticRenderersEnabled(bool modeEnabled)
        {
            if (m_LeftRenderers != null)
                for (var i = 0; i < m_LeftRenderers.Length; ++i)
                    m_LeftRenderers[i].enabled = modeEnabled && m_LeftTracked;
            if (m_RightRenderers != null)
                for (var i = 0; i < m_RightRenderers.Length; ++i)
                    m_RightRenderers[i].enabled = modeEnabled && m_RightTracked;
        }

        static Material CreateMaterial(Shader shader, Color color, bool enableInstancing)
        {
            var material = new Material(shader)
            {
                name = enableInstancing ? "Hand Joint GPU" : "Hand Joint Diagnostic",
                enableInstancing = enableInstancing,
                renderQueue = -1
            };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 1f);
            return material;
        }

        static void DestroyMaterial(Material material)
        {
            if (material != null)
                Destroy(material);
        }

        static Mesh BuildOctahedron()
        {
            var mesh = new Mesh { name = "Diagnostic Hand Joint" };
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
