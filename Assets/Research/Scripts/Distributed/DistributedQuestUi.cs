using System;
using System.Collections;
using System.Globalization;
using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace QuestPianoMotion.Research.Distributed
{
    [DisallowMultipleComponent]
    public sealed class DistributedQuestUi : MonoBehaviour
    {
        const string FontResourcePath = "Fonts & Materials/LiberationSans SDF";
        const int MaxIpLength = 15;
        const string IpSaveFileName = "distributed_host_ip.json";

        [SerializeField] bool m_EnableLegacyTextFallback;
        DistributedQuestClient m_Client;
        DistributedSettings m_Settings;
        PianoCalibrationManager m_Calibration;
        MinimalHandVisualizer m_Visualizer;
        VirtualPianoKeyboard m_Keyboard;
        TMP_Text m_KeyboardModeLabel;
        Canvas m_Canvas;
        TMP_Text m_Status;
        TMP_Text m_CalibrationStatus;
        string m_LastCalibrationMessage;
        string m_LastCaptureHand;
        TMP_InputField m_Ip;
        GameObject m_IpKeyboard;
        Canvas m_IpKeyboardCanvas;
        TrackedDeviceGraphicRaycaster m_IpKeyboardRaycaster;
        TMP_Text m_IpKeyboardError;
        string m_EditingIp;
        string m_DefaultIp;
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
        public TMP_Text CalibrationStatusText => m_CalibrationStatus;
        public Canvas WorldCanvas => m_Canvas;
        public TMP_Text HandInputDiagnosticText => m_HandInputDiagnostic;
        public RectTransform HandCursor => m_HandCursor;
        public Image HandCursorImage => m_HandCursorImage;
        public GraphicRaycaster LegacyRaycaster => m_LegacyRaycaster;
        public TrackedDeviceGraphicRaycaster XriRaycaster => m_XriRaycaster;
        public CanvasGroup CanvasGroup => m_CanvasGroup;
        public TMP_InputField IpInput => m_Ip;
        public bool IpKeyboardVisible => m_IpKeyboard != null && m_IpKeyboard.activeSelf;
        public Canvas IpKeyboardCanvas => m_IpKeyboardCanvas;
        public TrackedDeviceGraphicRaycaster IpKeyboardRaycaster => m_IpKeyboardRaycaster;
        public RectTransform IpKeyboardRect => m_IpKeyboardCanvas != null ? m_IpKeyboardCanvas.GetComponent<RectTransform>() : null;
        public string EditingIp => m_EditingIp ?? string.Empty;
        public string IpValidationError => m_IpKeyboardError != null ? m_IpKeyboardError.text : string.Empty;

        void Start()
        {
            m_Client = FindAnyObjectByType<DistributedQuestClient>();
            m_Settings = FindAnyObjectByType<DistributedSettings>();
            m_Calibration = FindAnyObjectByType<PianoCalibrationManager>();
            m_Visualizer = FindAnyObjectByType<MinimalHandVisualizer>();
            m_Keyboard = FindAnyObjectByType<VirtualPianoKeyboard>();
            m_PlacementGate = new QuestHmdPoseGate(Time.realtimeSinceStartupAsDouble);
            Build();
            StartCoroutine(PlaceWhenHeadPoseIsReady());
        }

        void Update()
        {
            if (m_CalibrationStatus != null && m_Calibration != null)
            {
                var message = m_Calibration.StatusText;
                var hand = m_Calibration.CaptureHandName;
                if (message != m_LastCalibrationMessage || hand != m_LastCaptureHand)
                {
                    m_LastCalibrationMessage = message;
                    m_LastCaptureHand = hand;
                    m_CalibrationStatus.text = $"Capture Hand: {hand.ToUpperInvariant()}\n{message}";
                }
            }
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
            if (m_IpKeyboardCanvas != null)
                m_IpKeyboardCanvas.worldCamera = camera;
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
            m_Ip.readOnly = true;
            m_Ip.contentType = TMP_InputField.ContentType.DecimalNumber;
            m_Ip.onSelect.AddListener(_ => OpenIpKeyboard());
            m_DefaultIp = DistributedSettings.DefaultPcIpAddress;
            var savedIp = LoadSavedIp();
            if (m_Keyboard != null)
                m_Keyboard.SetDisplayMode(LoadSavedDisplayMode());
            if (m_Settings != null)
                m_Settings.pcIpAddress = savedIp ?? m_DefaultIp;
            m_Ip.text = m_Settings != null ? m_Settings.pcIpAddress : m_DefaultIp;
            m_EditingIp = m_Ip.text;
            CreateIpKeyboard();

            CreateButton("CONNECT", new Vector2(400, -470), 130, () =>
            {
                if (m_Settings == null || m_Client == null) return;
                if (!IsValidIpv4(m_Settings.pcIpAddress))
                {
                    ShowIpError("Enter a valid IPv4 address.");
                    OpenIpKeyboard();
                    return;
                }
                m_Client.StopNetwork();
                m_Client.StartNetwork();
            });
            CreateButton("DISCONNECT", new Vector2(540, -470), 130, () => m_Client?.StopNetwork());
            CreateButton("CALIBRATION A", new Vector2(20, -540), 140, () => m_Calibration?.CaptureA());
            CreateButton("CALIBRATION B", new Vector2(180, -540), 140, () => m_Calibration?.CaptureB());
            CreateButton("CALIBRATION C", new Vector2(340, -540), 140, () => m_Calibration?.CaptureC());
            CreateButton("SAVE CALIBRATION", new Vector2(500, -540), 170, () => m_Calibration?.SaveCalibration());
            CreateButton("CANCEL CAPTURE", new Vector2(560, -610), 140, () => m_Calibration?.CancelCapture());
            CreateButton("RECENTER UI", new Vector2(20, -610), 150, RecenterUi);
            CreateButton("HAND GAMEOBJECTS", new Vector2(190, -610), 190,
                () => m_Visualizer?.UseGameObjectsDiagnostic());
            CreateButton("HAND GPU", new Vector2(400, -610), 150,
                () => m_Visualizer?.UseGpuInstanced());
            m_KeyboardModeLabel = CreateButton("KEYBOARD: 88 KEYS", new Vector2(560, -610), 160,
                ToggleKeyboardMode);
            UpdateKeyboardModeLabel();

            m_HandInputDiagnostic = CreateText("Hand Input Diagnostic", transform,
                "Pointer: NONE\nHand: RIGHT\nPinch: OPEN\nDistance: 0.0 mm\nTarget: NONE\nClicks: 0",
                new Vector2(20, -680), new Vector2(680, 190), 18,
                TextAlignmentOptions.TopLeft, new Color(0.7f, 0.95f, 1f, 1f));
            m_CalibrationStatus = CreateText("Calibration Capture Status", transform,
                "Capture Hand: RIGHT\nNot calibrated", new Vector2(20, -790), new Vector2(680, 90),
                23, TextAlignmentOptions.TopLeft, Color.white);
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
            if (m_IpKeyboardRaycaster != null)
                m_IpKeyboardRaycaster.enabled = useXri;
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

        void CreateIpKeyboard()
        {
            var keyboardCanvasObject = new GameObject("IPv4 Keyboard Canvas", typeof(RectTransform), typeof(Canvas));
            keyboardCanvasObject.layer = gameObject.layer;
            keyboardCanvasObject.transform.SetParent(transform, false);
            var keyboardCanvasRect = (RectTransform)keyboardCanvasObject.transform;
            keyboardCanvasRect.anchorMin = keyboardCanvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            keyboardCanvasRect.pivot = new Vector2(0.5f, 0.5f);
            // The root UI is 720 units wide at a 0.0009 world scale. This puts the
            // 430-unit keypad to the right with a stable 67.5 mm local-space gap.
            keyboardCanvasRect.anchoredPosition3D = new Vector3(650f, -40f, -8f);
            keyboardCanvasRect.sizeDelta = new Vector2(430f, 390f);
            keyboardCanvasRect.localRotation = Quaternion.identity;
            keyboardCanvasRect.localScale = Vector3.one;
            m_IpKeyboardCanvas = keyboardCanvasObject.GetComponent<Canvas>();
            m_IpKeyboardCanvas.renderMode = RenderMode.WorldSpace;
            m_IpKeyboardCanvas.overrideSorting = true;
            m_IpKeyboardCanvas.sortingOrder = m_Canvas.sortingOrder + 1;
            m_IpKeyboardCanvas.worldCamera = m_Canvas.worldCamera;
            m_IpKeyboardCanvas.enabled = false;
            m_IpKeyboardRaycaster = keyboardCanvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            m_IpKeyboardRaycaster.ignoreReversedGraphics = false;
            m_IpKeyboardRaycaster.checkFor2DOcclusion = false;
            m_IpKeyboardRaycaster.checkFor3DOcclusion = false;

            var panel = new GameObject("IPv4 Keyboard", typeof(RectTransform), typeof(Image));
            panel.layer = gameObject.layer;
            panel.transform.SetParent(keyboardCanvasObject.transform, false);
            m_IpKeyboard = panel;
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition3D = Vector3.zero;
            panelRect.sizeDelta = new Vector2(430f, 390f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.025f, 0.04f, 0.07f, 0.98f);
            panelImage.raycastTarget = false;

            CreateText("IPv4 Keyboard Title", panel.transform, "ENTER PC IPv4", new Vector2(20, -16),
                new Vector2(390, 34), 20, TextAlignmentOptions.Center, Color.white);
            m_IpKeyboardError = CreateText("IPv4 Keyboard Error", panel.transform, string.Empty,
                new Vector2(20, -50), new Vector2(390, 28), 16, TextAlignmentOptions.Center,
                new Color(1f, 0.65f, 0.35f, 1f));

            var labels = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "BACKSPACE", "CLEAR", "APPLY", "CANCEL" };
            for (var i = 0; i < labels.Length; ++i)
            {
                var label = labels[i];
                var row = i < 12 ? i / 3 : 4;
                var column = i < 12 ? i % 3 : i - 12;
                var width = i < 12 ? 120f : 125f;
                var x = -190f + column * 130f;
                var y = -92f - row * 54f;
                CreateKeyboardButton(label, panel.transform, new Vector2(x, y), new Vector2(width, 44f),
                    () => HandleIpKey(label));
            }
            panel.SetActive(false);
        }

        void CreateKeyboardButton(string label, Transform parent, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject("IPv4 Key " + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.32f, 0.62f, 1f);
            image.raycastTarget = true;
            var text = CreateText(label + " Label", go.transform, label, Vector2.zero, size,
                label.Length > 2 ? 13f : 20f, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform, 3f, 3f, 2f, 2f, -2f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
        }

        public void OpenIpKeyboard()
        {
            if (m_IpKeyboard == null || m_Ip == null)
                return;
            m_EditingIp = m_Ip.text ?? string.Empty;
            m_IpKeyboardError.text = string.Empty;
            if (m_IpKeyboardCanvas != null)
                m_IpKeyboardCanvas.enabled = true;
            m_IpKeyboard.SetActive(true);
            m_Ip.Select();
            m_Ip.caretPosition = m_EditingIp.Length;
        }

        public void HandleIpKey(string key)
        {
            if (m_IpKeyboard == null || !m_IpKeyboard.activeSelf)
                return;
            if (key == "BACKSPACE")
            {
                if (!string.IsNullOrEmpty(m_EditingIp))
                    m_EditingIp = m_EditingIp.Substring(0, m_EditingIp.Length - 1);
            }
            else if (key == "CLEAR")
                m_EditingIp = string.Empty;
            else if (key == "APPLY")
            {
                ApplyIpKeyboardValue();
                return;
            }
            else if (key == "CANCEL")
            {
                CancelIpKeyboard();
                return;
            }
            else if ((key.Length == 1 && (char.IsDigit(key[0]) || key[0] == '.')) &&
                     m_EditingIp.Length < MaxIpLength)
                m_EditingIp += key;
            m_Ip.text = m_EditingIp;
            m_Ip.caretPosition = m_EditingIp.Length;
        }

        public bool ApplyIpKeyboardValue()
        {
            if (!IsValidIpv4(m_EditingIp))
            {
                ShowIpError("Invalid IPv4 address.");
                return false;
            }
            if (m_Settings != null)
                m_Settings.pcIpAddress = m_EditingIp;
            m_Ip.text = m_EditingIp;
            SaveIp(m_EditingIp);
            m_IpKeyboard.SetActive(false);
            if (m_IpKeyboardCanvas != null)
                m_IpKeyboardCanvas.enabled = false;
            EventSystem.current?.SetSelectedGameObject(null);
            return true;
        }

        public void CancelIpKeyboard()
        {
            m_EditingIp = m_Settings != null ? m_Settings.pcIpAddress : m_Ip.text;
            m_Ip.text = m_EditingIp;
            m_IpKeyboardError.text = string.Empty;
            m_IpKeyboard.SetActive(false);
            if (m_IpKeyboardCanvas != null)
                m_IpKeyboardCanvas.enabled = false;
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void ShowIpError(string message)
        {
            if (m_IpKeyboardCanvas != null)
                m_IpKeyboardCanvas.enabled = true;
            if (m_IpKeyboard != null)
                m_IpKeyboard.SetActive(true);
            if (m_IpKeyboardError != null)
                m_IpKeyboardError.text = message;
        }

        public static bool IsValidIpv4(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxIpLength)
                return false;
            var parts = value.Split('.');
            if (parts.Length != 4)
                return false;
            for (var i = 0; i < parts.Length; ++i)
                if (parts[i].Length == 0 || parts[i].Length > 3 ||
                    !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var octet) ||
                    octet < 0 || octet > 255)
                    return false;
            return true;
        }

        string IpSavePath => Path.Combine(Application.persistentDataPath, "PianoResearch", IpSaveFileName);

        [Serializable]
        sealed class SavedIp
        {
            public string ip;
            public string keyboardMode;
        }

        public static KeyboardDisplayMode ParseSavedDisplayMode(string json)
        {
            try
            {
                var saved = JsonUtility.FromJson<SavedIp>(json);
                return saved != null &&
                    Enum.TryParse(saved.keyboardMode, out KeyboardDisplayMode mode) &&
                    (mode == KeyboardDisplayMode.Research13Keys || mode == KeyboardDisplayMode.Full88Keys)
                    ? mode : KeyboardDisplayMode.Full88Keys;
            }
            catch (Exception) { return KeyboardDisplayMode.Full88Keys; }
        }

        public static string SerializeSavedSettings(string ip, KeyboardDisplayMode mode) =>
            JsonUtility.ToJson(new SavedIp { ip = ip, keyboardMode = mode.ToString() }, true);

        KeyboardDisplayMode LoadSavedDisplayMode()
        {
            return LoadSavedDisplayModeFromPath(IpSavePath);
        }

        public static KeyboardDisplayMode LoadSavedDisplayModeFromPath(string path)
        {
            try { return File.Exists(path) ? ParseSavedDisplayMode(File.ReadAllText(path)) : KeyboardDisplayMode.Full88Keys; }
            catch (Exception) { return KeyboardDisplayMode.Full88Keys; }
        }

        public static void SaveSettingsToPath(string path, string ip, KeyboardDisplayMode mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, SerializeSavedSettings(ip, mode));
        }

        void ToggleKeyboardMode()
        {
            if (m_Keyboard == null) return;
            m_Keyboard.SetDisplayMode(m_Keyboard.DisplayMode == KeyboardDisplayMode.Full88Keys
                ? KeyboardDisplayMode.Research13Keys : KeyboardDisplayMode.Full88Keys);
            UpdateKeyboardModeLabel();
            SaveIp(m_Settings != null ? m_Settings.pcIpAddress : m_DefaultIp);
        }

        void UpdateKeyboardModeLabel()
        {
            if (m_KeyboardModeLabel != null && m_Keyboard != null)
                m_KeyboardModeLabel.text = m_Keyboard.DisplayMode == KeyboardDisplayMode.Full88Keys
                    ? "KEYBOARD: 88 KEYS" : "KEYBOARD: 13 KEYS";
        }

        string LoadSavedIp()
        {
            try
            {
                if (!File.Exists(IpSavePath))
                    return null;
                var saved = JsonUtility.FromJson<SavedIp>(File.ReadAllText(IpSavePath));
                return saved != null && IsValidIpv4(saved.ip) ? saved.ip : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Saved PC IP could not be loaded; using the default. " + e.Message, this);
                return null;
            }
        }

        void SaveIp(string ip)
        {
            try
            {
                SaveSettingsToPath(IpSavePath, ip,
                    m_Keyboard != null ? m_Keyboard.DisplayMode : LoadSavedDisplayMode());
            }
            catch (Exception e)
            {
                Debug.LogWarning("PC IP could not be saved: " + e.Message, this);
            }
        }

        TMP_Text CreateButton(string label, Vector2 position, float width, UnityEngine.Events.UnityAction action)
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
            return text;
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

        void OnDisable() => m_Calibration?.CancelCapture();

        void OnDestroy()
        {
            m_Calibration?.CancelCapture();
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
