using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedQuestUi : MonoBehaviour
    {
        const string FontResourcePath = "Fonts & Materials/LiberationSans SDF";

        [SerializeField] bool m_EnableLegacyTextFallback;
        DistributedQuestClient m_Client;
        DistributedSettings m_Settings;
        PianoCalibrationManager m_Calibration;
        MinimalHandVisualizer m_Visualizer;
        Canvas m_Canvas;
        TMP_Text m_Status;
        TMP_InputField m_Ip;
        TMP_FontAsset m_Font;
        TMP_Text m_HandInputDiagnostic;
        RectTransform m_HandCursor;
        Image m_HandCursorImage;
        GraphicRaycaster m_LegacyRaycaster;
        TrackedDeviceGraphicRaycaster m_XriRaycaster;
        CanvasGroup m_CanvasGroup;
        Texture2D m_HandCursorTexture;
        Sprite m_HandCursorSprite;
        QuestHmdPoseGate m_PlacementGate;
        float m_NextUpdate;

        public bool HasBeenPlaced { get; private set; }
        public TMP_FontAsset FontAsset => m_Font;
        public TMP_Text StatusText => m_Status;
        public Canvas WorldCanvas => m_Canvas;
        public TMP_Text HandInputDiagnosticText => m_HandInputDiagnostic;
        public RectTransform HandCursor => m_HandCursor;
        public Image HandCursorImage => m_HandCursorImage;
        public GraphicRaycaster LegacyRaycaster => m_LegacyRaycaster;
        public TrackedDeviceGraphicRaycaster XriRaycaster => m_XriRaycaster;
        public CanvasGroup CanvasGroup => m_CanvasGroup;

        void Start()
        {
            m_Client = FindAnyObjectByType<DistributedQuestClient>();
            m_Settings = FindAnyObjectByType<DistributedSettings>();
            m_Calibration = FindAnyObjectByType<PianoCalibrationManager>();
            m_Visualizer = FindAnyObjectByType<MinimalHandVisualizer>();
            m_PlacementGate = new QuestHmdPoseGate(Time.realtimeSinceStartupAsDouble);
            Build();
            StartCoroutine(PlaceWhenHeadPoseIsReady());
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextUpdate || m_Client == null || m_Status == null)
                return;
            m_NextUpdate = Time.unscaledTime + 0.25f;
            var visual = m_Visualizer != null
                ? $"{m_Visualizer.Mode} events={m_Visualizer.DisplayEventCount} tracked=({m_Visualizer.LeftTracked},{m_Visualizer.RightTracked}) valid=({m_Visualizer.LeftValidJointCount},{m_Visualizer.RightValidJointCount})"
                : "Unavailable";
            m_Status.text =
                "QUEST UI TEST 123\n" +
                (m_Client.PcConnected ? "PC CONNECTED\n" : "PC DISCONNECTED\n") +
                $"Connected at: {m_Client.ConnectedAt:F3}\n" +
                $"Last received: {m_Client.LastReceivedTimestamp:F3} s (age {m_Client.LastReceivedAge:F2} s)\n" +
                $"Received packets: {m_Client.ReceivedPacketCount}\n" +
                $"Sequence missing: {m_Client.MissingPacketCount}\n" +
                $"XR Loader: {m_Client.XrLoaderName}\n" +
                $"XR Hands running: {m_Client.XrHandsRunning}\n" +
                $"Hand Visual: {visual}\n" +
                $"RTT: {m_Client.RttSeconds * 1000d:F2} ms\n" +
                $"Clock Sync Status: {(m_Client.ClockSynchronized ? "Synchronized" : "Unavailable")}\n" +
                $"Pose Send Rate: {m_Client.PoseSendRate:F1}/s\n" +
                $"MIDI Receive Rate: {m_Client.MidiReceiveRate:F1}/s\n" +
                $"Last MIDI Event: {m_Client.LastMidiEvent}\n" +
                $"Session State: {m_Client.SessionState}";
        }

        IEnumerator PlaceWhenHeadPoseIsReady()
        {
            Camera camera;
            while (!m_PlacementGate.TryGetReadyCamera(Time.realtimeSinceStartupAsDouble, out camera))
                yield return null;
            PlaceAtCamera(camera, m_PlacementGate.UsedFallback);
        }

        public void RecenterUi()
        {
            if (m_PlacementGate != null &&
                m_PlacementGate.TryGetReadyCamera(Time.realtimeSinceStartupAsDouble, out var camera))
                PlaceAtCamera(camera, m_PlacementGate.UsedFallback);
        }

        public void PlaceAtCamera(Camera camera, bool fallback = false)
        {
            if (camera == null || m_Canvas == null)
                return;
            var xrOrigin = FindAnyObjectByType<XROrigin>();
            if (transform.parent != null)
                transform.SetParent(null, false);
            QuestSpatialPlacement.PlaceUi(transform, camera, xrOrigin);
            m_Canvas.worldCamera = camera;
            m_Canvas.enabled = true;
            HasBeenPlaced = true;
            Debug.Log($"[QuestPlacement] UI placed fallback={fallback} cameraRelative={camera.transform.InverseTransformPoint(transform.position)}", this);
        }

        void Build()
        {
            if (m_Canvas != null)
                return;

            m_Font = Resources.Load<TMP_FontAsset>(FontResourcePath);
            if (m_Font == null || m_Font.material == null || m_Font.material.shader == null)
            {
                Debug.LogError($"[QuestText] Required TMP font/material/shader is unavailable at Resources/{FontResourcePath}.", this);
                enabled = false;
                return;
            }

            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.WorldSpace;
            m_Canvas.enabled = false;
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(720, 900);
            rect.localScale = Vector3.one * 0.0009f;
            gameObject.AddComponent<CanvasScaler>();
            m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
            m_CanvasGroup.interactable = true;
            m_CanvasGroup.blocksRaycasts = true;
            m_LegacyRaycaster = gameObject.AddComponent<GraphicRaycaster>();
            // The runtime canvas uses a double-sided TMP material and is placed with the same
            // horizontal heading as the XR camera. Do not discard its otherwise valid graphics
            // solely because their transform normal is considered reversed by UGUI.
            m_LegacyRaycaster.ignoreReversedGraphics = true;
            m_LegacyRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            m_XriRaycaster = gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            m_XriRaycaster.ignoreReversedGraphics = false;
            m_XriRaycaster.checkFor2DOcclusion = false;
            m_XriRaycaster.checkFor3DOcclusion = false;

            var background = CreateImage("UI Background", transform, new Color(0.025f, 0.03f, 0.04f, 1f));
            Stretch(background.rectTransform, 0f, 0f, 0f, 0f, 0f);

            m_Status = CreateText("Status", transform, "QUEST UI TEST 123\nPC DISCONNECTED",
                new Vector2(20, -20), new Vector2(680, 420), 24, TextAlignmentOptions.TopLeft, Color.white);
            m_Ip = CreateInput("PC IP", new Vector2(20, -470), new Vector2(360, 48));
            if (m_Settings != null)
                m_Ip.text = m_Settings.pcIpAddress;

            CreateButton("CONNECT", new Vector2(400, -470), 130, () =>
            {
                if (m_Settings == null || m_Client == null) return;
                m_Settings.pcIpAddress = m_Ip.text;
                m_Client.StopNetwork();
                m_Client.StartNetwork();
            });
            CreateButton("DISCONNECT", new Vector2(540, -470), 130, () => m_Client?.StopNetwork());
            CreateButton("CALIBRATION A", new Vector2(20, -540), 140, () => m_Calibration?.CaptureA());
            CreateButton("CALIBRATION B", new Vector2(180, -540), 140, () => m_Calibration?.CaptureB());
            CreateButton("CALIBRATION C", new Vector2(340, -540), 140, () => m_Calibration?.CaptureC());
            CreateButton("SAVE CALIBRATION", new Vector2(500, -540), 170, () => m_Calibration?.SaveCalibration());
            CreateButton("RECENTER UI", new Vector2(20, -610), 150, RecenterUi);
            CreateButton("HAND GAMEOBJECTS", new Vector2(190, -610), 190,
                () => m_Visualizer?.UseGameObjectsDiagnostic());
            CreateButton("HAND GPU", new Vector2(400, -610), 150,
                () => m_Visualizer?.UseGpuInstanced());

            m_HandInputDiagnostic = CreateText("Hand Input Diagnostic", transform,
                "Pointer: NONE\nHand: RIGHT\nPinch: OPEN\nDistance: 0.0 mm\nTarget: NONE\nClicks: 0",
                new Vector2(20, -680), new Vector2(680, 190), 18,
                TextAlignmentOptions.TopLeft, new Color(0.7f, 0.95f, 1f, 1f));
            CreateHandCursor();

            var inputController = FindAnyObjectByType<XriHandUiInputController>(FindObjectsInactive.Include);
            ConfigureInputMode(inputController != null ? inputController.Mode : UiInputMode.XriStandard);

            if (m_EnableLegacyTextFallback)
                CreateLegacyFallback();
        }

        public void ConfigureInputMode(UiInputMode mode)
        {
            var useXri = mode == UiInputMode.XriStandard;
            if (m_LegacyRaycaster != null)
                m_LegacyRaycaster.enabled = !useXri;
            if (m_XriRaycaster != null)
                m_XriRaycaster.enabled = useXri;
            if (m_HandInputDiagnostic != null)
                m_HandInputDiagnostic.gameObject.SetActive(!useXri);
            if (useXri && m_HandCursor != null)
                m_HandCursor.gameObject.SetActive(false);
        }

        TMP_Text CreateText(string objectName, Transform parent, string value, Vector2 position, Vector2 size,
            float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition3D = new Vector3(position.x, position.y, -2f);
            rect.sizeDelta = size;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = m_Font;
            text.fontSharedMaterial = m_Font.material;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        TMP_InputField CreateInput(string objectName, Vector2 position, Vector2 size)
        {
            var background = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            background.layer = gameObject.layer;
            background.transform.SetParent(transform, false);
            var rect = (RectTransform)background.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            background.GetComponent<Image>().color = Color.white;

            var text = CreateText(objectName + " Text", background.transform, string.Empty, Vector2.zero,
                size - new Vector2(16, 6), 24, TextAlignmentOptions.MidlineLeft, Color.black);
            var textRect = text.rectTransform;
            Stretch(textRect, 8f, 8f, 3f, 3f, -2f);
            var input = background.GetComponent<TMP_InputField>();
            input.textViewport = rect;
            input.textComponent = text;
            input.richText = false;
            return input;
        }

        void CreateButton(string label, Vector2 position, float width, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, 48);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.32f, 0.62f, 1f);
            image.raycastTarget = true;
            var text = CreateText(label + " Label", go.transform, label, Vector2.zero, rect.sizeDelta,
                18, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform, 3f, 3f, 2f, 2f, -2f);
            var button = go.GetComponent<Button>();
            button.interactable = true;
            button.targetGraphic = image;
            button.onClick.AddListener(action);
        }

        void CreateHandCursor()
        {
            const int textureSize = 32;
            m_HandCursorTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "Hand UI Cursor Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[textureSize * textureSize];
            var center = (textureSize - 1) * 0.5f;
            var radius = textureSize * 0.45f;
            for (var y = 0; y < textureSize; ++y)
                for (var x = 0; x < textureSize; ++x)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    pixels[y * textureSize + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - distance + 0.5f) * 255f));
                }
            m_HandCursorTexture.SetPixels32(pixels);
            m_HandCursorTexture.Apply(false, true);
            m_HandCursorSprite = Sprite.Create(m_HandCursorTexture, new Rect(0, 0, textureSize, textureSize),
                new Vector2(0.5f, 0.5f), textureSize);
            m_HandCursorSprite.name = "Hand UI Cursor Circle";

            var cursor = new GameObject("Hand UI Cursor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cursor.layer = gameObject.layer;
            cursor.transform.SetParent(transform, false);
            m_HandCursor = (RectTransform)cursor.transform;
            m_HandCursor.anchorMin = m_HandCursor.anchorMax = new Vector2(0.5f, 0.5f);
            m_HandCursor.pivot = new Vector2(0.5f, 0.5f);
            m_HandCursor.sizeDelta = new Vector2(16f, 16f);
            m_HandCursor.anchoredPosition3D = new Vector3(0f, 0f, -6f);
            m_HandCursorImage = cursor.GetComponent<Image>();
            m_HandCursorImage.sprite = m_HandCursorSprite;
            m_HandCursorImage.color = Color.white;
            m_HandCursorImage.raycastTarget = false;
            cursor.SetActive(false);
        }

        void OnDestroy()
        {
            if (m_HandCursorSprite != null)
                Destroy(m_HandCursorSprite);
            if (m_HandCursorTexture != null)
                Destroy(m_HandCursorTexture);
        }

        static Image CreateImage(string objectName, Transform parent, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rect, float left, float right, float top, float bottom, float z)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            rect.anchoredPosition3D = new Vector3(rect.anchoredPosition.x, rect.anchoredPosition.y, z);
            rect.localScale = Vector3.one;
        }

        void CreateLegacyFallback()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Legacy Text Fallback", typeof(RectTransform), typeof(Text));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition3D = new Vector3(20, -680, -2f);
            rect.sizeDelta = new Vector2(680, 60);
            var text = go.GetComponent<Text>();
            text.text = "UI TEXT FALLBACK 123";
            text.font = font;
            text.fontSize = 28;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
        }
    }
}
