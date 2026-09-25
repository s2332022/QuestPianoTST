using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed class CalibrationPassthroughSession
    {
        readonly Behaviour m_Passthrough;
        readonly VirtualPianoKeyboard m_Keyboard;
        Camera m_Camera;
        bool m_WasEnabled;
        CameraClearFlags m_PreviousClearFlags;
        Color m_PreviousBackgroundColor;
        public bool Active { get; private set; }
        public bool Ready { get; private set; }
        public Camera TargetCamera => m_Camera;
        public Behaviour PassthroughManager => m_Passthrough;
        public CameraClearFlags PreviousClearFlags => m_PreviousClearFlags;
        public Color PreviousBackgroundColor => m_PreviousBackgroundColor;
        public bool PreviousManagerEnabled => m_WasEnabled;

        public CalibrationPassthroughSession(Behaviour passthrough, VirtualPianoKeyboard keyboard)
        {
            m_Passthrough = passthrough;
            m_Keyboard = keyboard;
        }

        public bool Begin(Camera camera, out string failureReason)
        {
            failureReason = null;
            if (Active) return true;
            if (camera == null)
            {
                failureReason = "Main Camera is unavailable";
                return false;
            }
            if (m_Passthrough == null)
            {
                failureReason = "ARCameraManager is missing from Main Camera";
                return false;
            }

            m_Camera = camera;
            m_WasEnabled = m_Passthrough.enabled;
            m_PreviousClearFlags = camera.clearFlags;
            m_PreviousBackgroundColor = camera.backgroundColor;
            Active = true;
            Ready = false;

            camera.clearFlags = CameraClearFlags.SolidColor;
            var color = m_PreviousBackgroundColor;
            color.a = 0f;
            camera.backgroundColor = color;
            try
            {
                m_Passthrough.enabled = true;
            }
            catch (System.Exception exception)
            {
                failureReason = "ARCameraManager could not be enabled: " + exception.GetType().Name + ": " + exception.Message;
                End();
                return false;
            }
            return true;
        }

        public void MarkReady()
        {
            if (!Active || Ready) return;
            Ready = true;
            m_Keyboard?.SetCalibrationTransparency(true);
        }

        public void End()
        {
            if (!Active) return;
            m_Keyboard?.SetCalibrationTransparency(false);
            if (m_Passthrough != null) m_Passthrough.enabled = m_WasEnabled;
            if (m_Camera != null)
            {
                m_Camera.clearFlags = m_PreviousClearFlags;
                m_Camera.backgroundColor = m_PreviousBackgroundColor;
            }
            Active = false;
            Ready = false;
        }
    }

    public static class CalibrationPassthroughReadiness
    {
        public static string GetFailureReason(bool xrInitialized, bool cameraManagerPresent,
            bool subsystemPresent, bool subsystemRunning, int passthroughLayerCount,
            bool passthroughLayerEnabled, int passthroughLayerOrder, string blendType)
        {
            if (!cameraManagerPresent) return "ARCameraManager is missing from Main Camera";
            if (!xrInitialized) return "OpenXR loader failed to initialize";
            if (!subsystemPresent) return "Camera subsystem was not created";
            if (!subsystemRunning) return "Camera subsystem failed to start";
            if (passthroughLayerCount == 0) return "Passthrough Composition Layer was not created";
            if (passthroughLayerCount > 1) return "Multiple Passthrough Composition Layers were found";
            if (!passthroughLayerEnabled) return "Passthrough Composition Layer is disabled";
            if (passthroughLayerOrder >= 0) return "Passthrough Composition Layer is not an underlay";
            if (!string.Equals(blendType, "Alpha", System.StringComparison.Ordinal))
                return "Passthrough Composition Layer blend type is not Alpha";
            return null;
        }
    }

    [DefaultExecutionOrder(-190)]
    [DisallowMultipleComponent]
    public sealed class DistributedQuestComposition : MonoBehaviour
    {
        XRHandPoseProvider m_Hands;
        VirtualPianoKeyboard m_Keyboard;
        PianoCalibrationManager m_Calibration;
        MinimalHandVisualizer m_Visualizer;
        QuestHmdPoseGate m_PlacementGate;
        CalibrationPassthroughSession m_PassthroughSession;
        Camera m_PassthroughCamera;
        ARCameraManager m_PassthroughCameraManager;
        Coroutine m_PassthroughStartup;
        string m_LastCameraSubsystemLookupError;
        const float PassthroughStartTimeoutSeconds = 2f;
        const string CompositionLayerTypeName = "Unity.XR.CompositionLayers.CompositionLayer";
        const string PassthroughLayerDataTypeName = "UnityEngine.XR.OpenXR.Features.Meta.PassthroughLayerData";

        sealed class PassthroughLayerSnapshot
        {
            public int GameObjectCount;
            public int LayerCount;
            public bool Enabled;
            public int Order;
            public string BlendType = "unavailable";
        }

        public bool DefaultKeyboardPlaced { get; private set; }

        void Awake()
        {
            ResearchServices.TrackingOrigin = ResearchServices.FindTrackingOrigin();
        }

        void Start()
        {
            m_Hands = GetComponent<XRHandPoseProvider>();
            m_Keyboard = GetComponent<VirtualPianoKeyboard>();
            m_Calibration = GetComponent<PianoCalibrationManager>();
            m_Visualizer = GetComponent<MinimalHandVisualizer>();
            m_PlacementGate = new QuestHmdPoseGate(Time.realtimeSinceStartupAsDouble);
            var mainCamera = Camera.main;
            var cameraManager = mainCamera != null ? mainCamera.GetComponent<ARCameraManager>() : null;
            m_PassthroughCamera = mainCamera;
            m_PassthroughCameraManager = cameraManager;
            m_PassthroughSession = new CalibrationPassthroughSession(m_PassthroughCameraManager, m_Keyboard);

            var piano = GameObject.Find("Piano Root");
            if (m_Keyboard != null && piano != null)
                m_Keyboard.ConfigureMinimal(piano.transform);

            m_Visualizer?.Initialize(m_Hands);
            var diagnostics = GetComponent<QuestSpatialDiagnostics>();
            if (diagnostics == null)
                diagnostics = gameObject.AddComponent<QuestSpatialDiagnostics>();
            diagnostics.Initialize(m_Hands);
            m_Calibration?.Initialize(m_Hands);

            if (m_Calibration != null && m_Keyboard != null)
            {
                m_Calibration.CalibrationChanged += m_Keyboard.ApplyCalibration;
                m_Calibration.CaptureSessionStarted += OnCaptureSessionStarted;
                m_Calibration.CaptureSessionEnded += OnCaptureSessionEnded;
                if (m_Calibration.Current != null && m_Calibration.Current.valid)
                    m_Keyboard.ApplyCalibration(m_Calibration.Current);
                else
                    StartCoroutine(PlaceDefaultKeyboardWhenHeadReady());
            }
        }

        IEnumerator PlaceDefaultKeyboardWhenHeadReady()
        {
            Camera camera;
            while (!m_PlacementGate.TryGetReadyCamera(Time.realtimeSinceStartupAsDouble, out camera))
                yield return null;

            if (m_Calibration == null || m_Keyboard == null || m_Keyboard.KeyboardRoot == null ||
                (m_Calibration.Current != null && m_Calibration.Current.valid))
                yield break;

            var xrOrigin = FindAnyObjectByType<XROrigin>();
            QuestSpatialPlacement.PlacePiano(m_Keyboard.KeyboardRoot, camera, xrOrigin);
            DefaultKeyboardPlaced = true;
            Debug.Log($"[QuestPlacement] Piano placed fallback={m_PlacementGate.UsedFallback} cameraRelative={camera.transform.InverseTransformPoint(m_Keyboard.KeyboardRoot.position)}", this);
        }

        void OnDestroy()
        {
            if (m_Calibration != null && m_Keyboard != null)
            {
                m_Calibration.CalibrationChanged -= m_Keyboard.ApplyCalibration;
                m_Calibration.CaptureSessionStarted -= OnCaptureSessionStarted;
                m_Calibration.CaptureSessionEnded -= OnCaptureSessionEnded;
            }
            EndPassthroughSession("Scene destroyed");
        }

        void OnDisable()
        {
            m_Calibration?.CancelCapture();
            EndPassthroughSession("Composition disabled");
        }

        void OnApplicationQuit() => EndPassthroughSession("Application quitting");

        void OnCaptureSessionStarted()
        {
            if (m_PassthroughStartup != null)
                StopCoroutine(m_PassthroughStartup);
            m_PassthroughStartup = StartCoroutine(EnablePassthroughForCalibration());
        }

        void OnCaptureSessionEnded() => EndPassthroughSession("Capture session ended");

        IEnumerator EnablePassthroughForCalibration()
        {
            m_PassthroughCamera = Camera.main;
            m_PassthroughCameraManager = m_PassthroughCamera != null
                ? m_PassthroughCamera.GetComponent<ARCameraManager>() : null;
            m_PassthroughSession = new CalibrationPassthroughSession(m_PassthroughCameraManager, m_Keyboard);

            var cameraColorBefore = m_PassthroughCamera != null ? m_PassthroughCamera.backgroundColor : default;
            string failureReason;
            var requested = m_PassthroughSession.Begin(m_PassthroughCamera, out failureReason);
            if (!requested)
            {
                LogPassthroughState("Passthrough enable requested", cameraColorBefore, failureReason);
                if (m_Calibration != null)
                {
                    var userMessage = failureReason != null && failureReason.Contains("Camera subsystem")
                        ? "Camera subsystem failed to start" : failureReason;
                    m_Calibration.FailPassthroughStartup(userMessage);
                }
                m_PassthroughStartup = null;
                yield break;
            }

            LogPassthroughState("Passthrough enable requested", cameraColorBefore, "Waiting for Camera Subsystem and Passthrough Layer");
            var deadline = Time.realtimeSinceStartupAsDouble + PassthroughStartTimeoutSeconds;
            PassthroughLayerSnapshot layer = null;
            var subsystem = GetCameraSubsystem();
            var xrInitialized = IsOpenXrInitialized();
            var failure = string.Empty;
            var nextLayerCheck = 0d;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                subsystem = GetCameraSubsystem();
                xrInitialized = IsOpenXrInitialized();
                if (subsystem != null && subsystem.running && Time.realtimeSinceStartupAsDouble >= nextLayerCheck)
                {
                    layer = FindPassthroughLayer();
                    nextLayerCheck = Time.realtimeSinceStartupAsDouble + 0.1d;
                }
                failure = CalibrationPassthroughReadiness.GetFailureReason(
                    xrInitialized,
                    m_PassthroughCameraManager != null,
                    subsystem != null,
                    subsystem != null && subsystem.running,
                    layer != null ? layer.LayerCount : 0,
                    layer != null && layer.Enabled,
                    layer != null ? layer.Order : int.MaxValue,
                    layer != null ? layer.BlendType : "unavailable");
                if (failure == null)
                {
                    m_PassthroughSession.MarkReady();
                    LogPassthroughState("Passthrough enabled", cameraColorBefore, "None");
                    m_PassthroughStartup = null;
                    yield break;
                }
                yield return null;
            }

            subsystem = GetCameraSubsystem();
            layer = FindPassthroughLayer();
            failure = CalibrationPassthroughReadiness.GetFailureReason(
                IsOpenXrInitialized(),
                m_PassthroughCameraManager != null,
                subsystem != null,
                subsystem != null && subsystem.running,
                layer != null ? layer.LayerCount : 0,
                layer != null && layer.Enabled,
                layer != null ? layer.Order : int.MaxValue,
                layer != null ? layer.BlendType : "unavailable");
            if (string.IsNullOrEmpty(failure))
                failure = "Passthrough startup timed out";
            LogPassthroughState("Passthrough enable failed", cameraColorBefore, failure);
            m_PassthroughSession.End();
            LogPassthroughState("Passthrough state restored after failure", cameraColorBefore, failure);
            if (m_Calibration != null)
            {
                var userMessage = failure.Contains("Camera subsystem")
                    ? "Camera subsystem failed to start" : failure;
                m_Calibration.FailPassthroughStartup(userMessage);
            }
            m_PassthroughStartup = null;
        }

        void EndPassthroughSession(string reason)
        {
            if (m_PassthroughStartup != null)
            {
                StopCoroutine(m_PassthroughStartup);
                m_PassthroughStartup = null;
            }
            if (m_PassthroughSession == null || !m_PassthroughSession.Active)
                return;

            var cameraColorBefore = m_PassthroughCamera != null ? m_PassthroughCamera.backgroundColor : default;
            m_PassthroughSession.End();
            LogPassthroughState("Passthrough session ended", cameraColorBefore, reason);
        }

        XRCameraSubsystem GetCameraSubsystem()
        {
            m_LastCameraSubsystemLookupError = null;
            try { return m_PassthroughCameraManager != null ? m_PassthroughCameraManager.subsystem : null; }
            catch (System.Exception exception)
            {
                m_LastCameraSubsystemLookupError = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
        }

        static bool IsOpenXrInitialized()
        {
            var settings = XRGeneralSettings.Instance;
            return settings != null && settings.Manager != null && settings.Manager.isInitializationComplete &&
                   settings.Manager.activeLoader != null;
        }

        static PassthroughLayerSnapshot FindPassthroughLayer()
        {
            var result = new PassthroughLayerSnapshot();
            var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            foreach (var transform in transforms)
            {
                if (transform == null || !transform.gameObject.scene.IsValid() || transform.name != "Passthrough")
                    continue;

                ++result.GameObjectCount;
                var components = transform.GetComponents<MonoBehaviour>();
                foreach (var component in components)
                {
                    if (component == null || component.GetType().FullName != CompositionLayerTypeName)
                        continue;
                    try
                    {
                        var dataProperty = component.GetType().GetProperty("LayerData", BindingFlags.Instance | BindingFlags.Public);
                        var data = dataProperty != null ? dataProperty.GetValue(component) : null;
                        if (data == null || data.GetType().FullName != PassthroughLayerDataTypeName)
                            continue;

                        ++result.LayerCount;
                        result.Enabled = component.enabled && component.gameObject.activeInHierarchy;
                        var orderProperty = component.GetType().GetProperty("Order", BindingFlags.Instance | BindingFlags.Public);
                        if (orderProperty != null)
                            result.Order = (int)orderProperty.GetValue(component);
                        var blendProperty = data.GetType().GetProperty("BlendTypeDirectly", BindingFlags.Instance | BindingFlags.Public);
                        if (blendProperty != null)
                            result.BlendType = blendProperty.GetValue(data)?.ToString() ?? "unavailable";
                    }
                    catch (System.Exception exception)
                    {
                        result.BlendType = "inspection error: " + exception.GetType().Name;
                    }
                }
            }
            return result;
        }

        static int GetPrivateInt(object target, string fieldName, int fallback)
        {
            if (target == null) return fallback;
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null || field.FieldType != typeof(int)) continue;
                try { return (int)field.GetValue(target); }
                catch (System.Exception) { return fallback; }
            }
            return fallback;
        }

        static void AppendRenderPipelineDiagnostics(StringBuilder log, Camera camera)
        {
            var pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cameraData = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
            var defaultRendererIndex = GetPrivateInt(pipelineAsset, "m_DefaultRendererIndex", 0);
            var configuredRendererIndex = GetPrivateInt(cameraData, "m_RendererIndex", -1);
            var rendererIndex = configuredRendererIndex < 0 ? defaultRendererIndex : configuredRendererIndex;
            ScriptableRendererData rendererAsset = null;
            if (pipelineAsset != null)
            {
                var rendererListField = typeof(UniversalRenderPipelineAsset).GetField(
                    "m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                try
                {
                    var rendererAssets = rendererListField != null
                        ? rendererListField.GetValue(pipelineAsset) as ScriptableRendererData[] : null;
                    if (rendererAssets != null && rendererIndex >= 0 && rendererIndex < rendererAssets.Length)
                        rendererAsset = rendererAssets[rendererIndex];
                }
                catch (System.Exception) { }
            }

            var cameraStackCount = 0;
            var cameraPostProcessing = false;
            var renderType = "Base (default; no UniversalAdditionalCameraData)";
            if (cameraData != null)
            {
                renderType = cameraData.renderType.ToString();
                cameraPostProcessing = cameraData.renderPostProcessing;
                try { cameraStackCount = cameraData.cameraStack != null ? cameraData.cameraStack.Count : 0; }
                catch (System.Exception) { cameraStackCount = -1; }
            }

            var intermediateTexture = "unavailable";
            var rendererFeatures = "unavailable";
            if (rendererAsset != null)
            {
                try
                {
                    var intermediateProperty = rendererAsset.GetType().GetProperty("intermediateTextureMode");
                    if (intermediateProperty != null)
                        intermediateTexture = intermediateProperty.GetValue(rendererAsset)?.ToString() ?? "unavailable";
                    var featuresProperty = rendererAsset.GetType().GetProperty("rendererFeatures");
                    var featureList = featuresProperty != null
                        ? featuresProperty.GetValue(rendererAsset) as System.Collections.IList : null;
                    if (featureList != null)
                    {
                        if (featureList.Count == 0) rendererFeatures = "none";
                        else
                        {
                            var featureNames = new StringBuilder();
                            for (var i = 0; i < featureList.Count; ++i)
                            {
                                if (i > 0) featureNames.Append(',');
                                var feature = featureList[i] as UnityEngine.Object;
                                featureNames.Append(feature != null ? feature.name : "null");
                            }
                            rendererFeatures = featureNames.ToString();
                        }
                    }
                }
                catch (System.Exception) { }
            }

            var target = camera != null ? camera.targetTexture : null;
            log.Append("Camera HDR allowed: ").AppendLine(camera != null ? camera.allowHDR.ToString() : "unavailable");
            log.Append("Camera target texture: ").AppendLine(target != null
                ? target.name + " (" + target.width + "x" + target.height + ")" : "none");
            log.Append("Universal Additional Camera Data: ").AppendLine(cameraData != null ? "present" : "absent (URP defaults)");
            log.Append("Camera Render Type: ").AppendLine(renderType);
            log.Append("Camera Renderer Index: ").Append(rendererIndex)
                .Append(" (configured=").Append(configuredRendererIndex).AppendLine(")");
            log.Append("Camera Stack count: ").AppendLine(cameraStackCount.ToString());
            log.Append("Camera Post Processing: ").AppendLine(cameraPostProcessing.ToString());
            log.Append("Active URP Asset: ").AppendLine(pipelineAsset != null ? pipelineAsset.name : "none (Built-in)");
            log.Append("URP HDR support: ").AppendLine(pipelineAsset != null ? pipelineAsset.supportsHDR.ToString() : "unavailable");
            log.Append("URP Opaque Texture: ").AppendLine(pipelineAsset != null ? pipelineAsset.supportsCameraOpaqueTexture.ToString() : "unavailable");
            log.Append("URP Depth Texture: ").AppendLine(pipelineAsset != null ? pipelineAsset.supportsCameraDepthTexture.ToString() : "unavailable");
            log.Append("URP Post Process Alpha Output: ").AppendLine(pipelineAsset != null ? pipelineAsset.allowPostProcessAlphaOutput.ToString() : "unavailable");
            log.Append("Active Renderer Asset: ").AppendLine(rendererAsset != null ? rendererAsset.name : "unavailable");
            log.Append("Renderer Intermediate Texture: ").AppendLine(intermediateTexture);
            log.Append("Renderer Features: ").AppendLine(rendererFeatures);
            log.Append("Graphics API: ").AppendLine(SystemInfo.graphicsDeviceType.ToString());
            log.Append("Color Space: ").AppendLine(QualitySettings.activeColorSpace.ToString());
        }
        void LogPassthroughState(string heading, Color cameraColorBefore, string failureReason)
        {
            var camera = m_PassthroughCamera;
            var cameraManager = m_PassthroughCameraManager;
            XRCameraSubsystem subsystem = null;
            try { subsystem = cameraManager != null ? cameraManager.subsystem : null; }
            catch (System.Exception) { }

            var xrSettings = XRGeneralSettings.Instance;
            var xrManager = xrSettings != null ? xrSettings.Manager : null;
            var loader = xrManager != null ? xrManager.activeLoader : null;
            var descriptors = new List<XRCameraSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(descriptors);
            var descriptorIds = new StringBuilder();
            for (var i = 0; i < descriptors.Count; ++i)
            {
                if (i > 0) descriptorIds.Append(',');
                descriptorIds.Append(descriptors[i] != null ? descriptors[i].id : "null");
            }

            var layer = FindPassthroughLayer();
            var mainCameraIsRendering = false;
            if (camera != null && camera.isActiveAndEnabled)
                mainCameraIsRendering = System.Array.IndexOf(Camera.allCameras, camera) >= 0;

            var extensionEnabled = false;
            try { extensionEnabled = loader != null && OpenXRRuntime.IsExtensionEnabled("XR_FB_passthrough"); }
            catch (System.Exception) { }

            var cameraColorAfter = camera != null ? camera.backgroundColor : default;
            var clearFlagsBefore = m_PassthroughSession != null
                ? m_PassthroughSession.PreviousClearFlags.ToString() : "unavailable";
            var savedCameraColor = m_PassthroughSession != null
                ? m_PassthroughSession.PreviousBackgroundColor : default;
            var managerEnabledBefore = m_PassthroughSession != null && m_PassthroughSession.PreviousManagerEnabled;
            var blend = layer != null ? layer.BlendType : "unavailable";
            var order = layer != null && layer.LayerCount == 1 ? layer.Order.ToString() : "unavailable";
            var permission = "unavailable";
            try { if (cameraManager != null) permission = cameraManager.permissionGranted.ToString(); }
            catch (System.Exception exception) { permission = "error:" + exception.GetType().Name; }

            var log = new StringBuilder(768);
            log.AppendLine(heading);
            log.Append("Main Camera: ").Append(camera != null ? camera.gameObject.name : "null")
                .Append("; rendered: ").AppendLine(mainCameraIsRendering.ToString());
            log.Append("Camera clearFlags before/after: ").Append(clearFlagsBefore).Append('/')
                .AppendLine(camera != null ? camera.clearFlags.ToString() : "unavailable");
            log.Append("Camera alpha before: ").AppendLine(cameraColorBefore.a.ToString("F3"));
            log.Append("Camera alpha after: ").AppendLine(cameraColorAfter.a.ToString("F3"));
            log.Append("Camera alpha saved for restore: ").AppendLine(savedCameraColor.a.ToString("F3"));
            AppendRenderPipelineDiagnostics(log, camera);
            log.Append("Camera RGB before/after: ").Append(cameraColorBefore.r.ToString("F3")).Append(',')
                .Append(cameraColorBefore.g.ToString("F3")).Append(',').Append(cameraColorBefore.b.ToString("F3"))
                .Append('/').Append(cameraColorAfter.r.ToString("F3")).Append(',')
                .Append(cameraColorAfter.g.ToString("F3")).Append(',').AppendLine(cameraColorAfter.b.ToString("F3"));
            log.Append("ARCameraManager enabled: exists=").Append(cameraManager != null)
                .Append(" current=").Append(cameraManager != null && cameraManager.enabled)
                .Append(" saved=").Append(managerEnabledBefore).Append(" permissionGranted=").AppendLine(permission);
            if (!string.IsNullOrEmpty(m_LastCameraSubsystemLookupError))
                log.Append("Camera subsystem lookup exception: ").AppendLine(m_LastCameraSubsystemLookupError);
            log.Append("Camera subsystem: ").Append(subsystem != null && subsystem.subsystemDescriptor != null
                ? subsystem.subsystemDescriptor.id : "null").Append("; registered descriptor IDs: ").AppendLine(descriptorIds.ToString());
            log.Append("Camera subsystem running: ").AppendLine(subsystem != null && subsystem.running ? "true" : "false");
            log.Append("XR General Settings initialized: ").AppendLine(xrManager != null && xrManager.isInitializationComplete ? "true" : "false");
            log.Append("OpenXR loader: ").Append(loader != null ? loader.GetType().Name : "none")
                .Append("; active: ").Append(loader != null).Append("; XR_FB_passthrough enabled: ").AppendLine(extensionEnabled.ToString());
            log.Append("Passthrough runtime objects: ").Append(layer != null ? layer.GameObjectCount : 0)
                .Append("; Composition Layer count: ").AppendLine(layer != null ? layer.LayerCount.ToString() : "0");
            log.Append("Composition Layer enabled: ").AppendLine(layer != null && layer.Enabled ? "true" : "false");
            log.Append("Composition Layer Layer Order: ").AppendLine(order);
            log.Append("Composition Layer Blend Type: ").AppendLine(blend);
            log.Append("Failure reason: ").Append(string.IsNullOrEmpty(failureReason) ? "None" : failureReason);
            Debug.Log(log.ToString(), this);
        }
    }
}
