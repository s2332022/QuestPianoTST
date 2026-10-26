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
    public sealed partial class DistributedQuestUi : MonoBehaviour
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
        TMP_Text m_CorrectionStatus;
        TMP_Text m_KeyboardModeLabel;
        TMP_Text m_CaptureAButtonLabel;
        TMP_Text m_CancelCaptureButtonLabel;
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
        TMP_FontAsset m_JapaneseFont;
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
        public PerformanceUiSafety Safety { get; private set; }

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
            if (m_Keyboard == null) m_Keyboard = FindAnyObjectByType<VirtualPianoKeyboard>();
            FloatingPanel?.SetReference(m_Calibration != null && m_Calibration.Current != null && m_Calibration.Current.valid
                && m_Keyboard != null ? m_Keyboard.KeyboardRoot : null, m_Calibration?.Current?.calibrationId);
            if (m_CalibrationStatus != null && m_Calibration != null)
            {
                var message = m_Calibration.StatusText;
                var hand = m_Calibration.CaptureHandName;
                if (message != m_LastCalibrationMessage || hand != m_LastCaptureHand)
                {
                    m_LastCalibrationMessage = message;
                    m_LastCaptureHand = hand;
                    m_CalibrationStatus.text = $"登録する手：{(string.Equals(hand,"Left",StringComparison.OrdinalIgnoreCase)?"左手":"右手")}\n{message}";
                    UpdatePassthroughFailureActions();
                }
            }
            if (Time.unscaledTime < m_NextUpdate || m_Client == null || m_Status == null)
                return;
            m_NextUpdate = Time.unscaledTime + 0.25f;
            UpdateCorrectionStatus();
            UpdateOverview();
            var visual = m_Visualizer != null
                ? $"{m_Visualizer.Mode} events={m_Visualizer.DisplayEventCount} tracked=({m_Visualizer.LeftTracked},{m_Visualizer.RightTracked}) valid=({m_Visualizer.LeftValidJointCount},{m_Visualizer.RightValidJointCount})"
                : "Unavailable";
            m_Status.text =
                "接続・診断情報\n" +
                (m_Client.PcConnected ? "PC接続中\n" : "PC未接続\n") +
                $"接続時刻：{m_Client.ConnectedAt:F3}\n" +
                $"最終受信：{m_Client.LastReceivedTimestamp:F3} 秒（経過 {m_Client.LastReceivedAge:F2} 秒）\n" +
                $"受信パケット：{m_Client.ReceivedPacketCount}\n欠落シーケンス：{m_Client.MissingPacketCount}\n" +
                $"XR Loader：{m_Client.XrLoaderName}\nXR Hands動作中：{m_Client.XrHandsRunning}\n手の表示：{visual}\n" +
                $"RTT：{m_Client.RttSeconds * 1000d:F2} ms\n" +
                $"クロック同期：{(m_Client.ClockSynchronized ? "同期済み" : "利用不可")}\n" +
                $"姿勢送信率：{m_Client.PoseSendRate:F1}/秒\nMIDI受信率：{m_Client.MidiReceiveRate:F1}/秒\n" +
                $"最終MIDIイベント：{m_Client.LastMidiEvent}\n" +
                $"セッション：{m_Client.SessionState} | Raw：{(m_Client.ResearchRecording ? "記録中" : "停止中")} {m_Client.ResearchLogError}\n" +
                $"補正詳細：{(m_Client.RuleSettings.method==HandCorrectionMethod.Legacy?m_Client.Correction?.Latest.Reason:m_Client.PoseCorrection?.New.Reason)}";
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
            var camera = m_Canvas != null ? m_Canvas.worldCamera : null;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;
            FloatingPanel.SetCamera(camera);
            FloatingPanel.PlaceInFront();
        }

        public void PlaceAtCamera(Camera camera, bool fallback = false)
        {
            if (camera == null || m_Canvas == null) return;
            if (transform.parent != null) transform.SetParent(null, true);
            m_Canvas.worldCamera = camera;
            if (m_IpKeyboardCanvas != null) m_IpKeyboardCanvas.worldCamera = camera;
            FloatingPanel.SetCamera(camera);
            FloatingPanel.SetReference(m_Calibration != null && m_Calibration.Current != null && m_Calibration.Current.valid
                && m_Keyboard != null ? m_Keyboard.KeyboardRoot : null, m_Calibration?.Current?.calibrationId);
            if (!HasBeenPlaced && FloatingPanel.Restore()) { }
            else FloatingPanel.PlaceInFront(false);
            m_Canvas.enabled = true;
            HasBeenPlaced = true;
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

            m_JapaneseFont = QuestUiFontFallback.Acquire(m_Font);
            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.WorldSpace;
            m_Canvas.enabled = false;
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(720, 970);
            rect.localScale = Vector3.one * 0.00065f;
            gameObject.AddComponent<CanvasScaler>();
            m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
            m_CanvasGroup.interactable = true;
            m_CanvasGroup.blocksRaycasts = true;
            Safety = gameObject.AddComponent<PerformanceUiSafety>();
            Safety.Initialize(this, m_CanvasGroup, m_Keyboard);
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
            m_Ip.onSelect.AddListener(_ => Safety.Run("EDIT IP", OpenIpKeyboard));
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
                    ShowIpError("有効なIPv4アドレスを入力してください。");
                    OpenIpKeyboard();
                    return;
                }
                m_Client.StopNetwork();
                m_Client.StartNetwork();
            }, true);
            CreateButton("DISCONNECT", new Vector2(540, -470), 130, () => m_Client?.StopNetwork(), true);
            m_CaptureAButtonLabel = CreateButton("CALIBRATION A", new Vector2(20, -540), 140, () =>
            {
                if (m_Calibration != null && m_Calibration.PassthroughRetryAvailable)
                    m_Calibration.RetryPassthroughCapture();
                else
                    m_Calibration?.CaptureA();
            }, true);
            CreateButton("CALIBRATION B", new Vector2(180, -540), 140, () => m_Calibration?.CaptureB());
            CreateButton("CALIBRATION C", new Vector2(340, -540), 140, () => m_Calibration?.CaptureC());
            CreateButton("SAVE CALIBRATION", new Vector2(500, -540), 170, () => m_Calibration?.SaveCalibration(), true);
            m_CancelCaptureButtonLabel = CreateButton("CANCEL CAPTURE", new Vector2(500, -900), 200,
                () => m_Calibration?.CancelCalibration());
            CreateButton("RECENTER UI", new Vector2(20, -610), 150, RecenterUi);
            CreateButton("HAND GAMEOBJECTS", new Vector2(190, -610), 190,
                () => m_Visualizer?.UseGameObjectsDiagnostic());
            CreateButton("HAND GPU", new Vector2(400, -610), 150,
                () => m_Visualizer?.UseGpuInstanced());
            m_KeyboardModeLabel = CreateButton("KEYBOARD: 88 KEYS", new Vector2(560, -610), 160,
                ToggleKeyboardMode);
            UpdateKeyboardModeLabel();
            UpdatePassthroughFailureActions();

            m_HandInputDiagnostic = CreateText("Hand Input Diagnostic", transform,
                "Pointer: NONE\nHand: RIGHT\nPinch: OPEN\nDistance: 0.0 mm\nTarget: NONE\nClicks: 0",
                new Vector2(20, -680), new Vector2(680, 190), 18,
                TextAlignmentOptions.TopLeft, new Color(0.7f, 0.95f, 1f, 1f));
            m_CalibrationStatus = CreateText("Calibration Capture Status", transform,
                "登録する手：右手\n鍵盤の位置合わせが必要です", new Vector2(20, -790), new Vector2(680, 90),
                23, TextAlignmentOptions.TopLeft, Color.white);
            CreateButton("START RAW REC",new Vector2(20,-900),210,()=>m_Client?.BeginLocalResearchSession());
            CreateButton("STOP RAW REC",new Vector2(260,-900),210,()=>m_Client?.EndLocalResearchSession(), true);
            var safetyStatus = CreateText("Performance UI State", transform, "UI READY - aim ray and pinch",
                new Vector2(20, -440), new Vector2(680, 26), 19,
                TextAlignmentOptions.MidlineLeft, new Color(1f, 0.85f, 0.35f));
            Safety.SetStatusText(safetyStatus);
            BuildCorrectionControls();
            BuildFloatingLayout();
            CreateHandCursor();

            var inputController = FindAnyObjectByType<XriHandUiInputController>(FindObjectsInactive.Include);
            ConfigureInputMode(inputController != null ? inputController.Mode : UiInputMode.XriStandard);

            if (m_EnableLegacyTextFallback)
                CreateLegacyFallback();
        }

        void BuildCorrectionControls()
        {
            var panel = new GameObject("MIDI Tip Correction Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(740, -20); rect.sizeDelta = new Vector2(490, 720);
            panel.GetComponent<Image>().color = new Color(.03f, .06f, .1f, .96f);
            panel.GetComponent<Image>().raycastTarget = false;
            m_CorrectionStatus = CreateText("MIDI Tip Correction Status", transform, "指先補正の状態",
                new Vector2(760,-40),new Vector2(450,170),22,TextAlignmentOptions.TopLeft,Color.white);
            MidiTipCorrectionSettings Config() => m_Client != null ? m_Client.CorrectionSettings : null;
            CreateButton("CORRECTION ON/OFF",new Vector2(760,-220),220,()=>{var c=Config();if(c!=null)c.enabled=!c.enabled;});
            CreateButton("LEFT / RIGHT",new Vector2(990,-220),210,()=>{var c=Config();if(c!=null)c.leftHand=!c.leftHand;});
            CreateButton("NEXT FINGER",new Vector2(760,-280),220,()=>{var c=Config();if(c!=null)c.finger=(CorrectionFinger)(((int)c.finger+1)%5);});
            CreateButton("HAND MESH",new Vector2(990,-280),210,()=>m_Visualizer?.SetMode(HandVisualizationMode.SkinnedMesh));
            CreateButton("STRENGTH -0.25",new Vector2(760,-340),220,()=>{var c=Config();if(c!=null)c.strength=Mathf.Clamp01(c.strength-.25f);});
            CreateButton("STRENGTH +0.25",new Vector2(990,-340),210,()=>{var c=Config();if(c!=null)c.strength=Mathf.Clamp01(c.strength+.25f);});
            CreateButton("MAX -5 mm",new Vector2(760,-400),220,()=>{var c=Config();if(c!=null)c.maxCorrectionMeters=Mathf.Max(0,c.maxCorrectionMeters-.005f);});
            CreateButton("MAX +5 mm",new Vector2(990,-400),210,()=>{var c=Config();if(c!=null)c.maxCorrectionMeters=Mathf.Min(.03f,c.maxCorrectionMeters+.005f);});
            CreateButton("PAD Y -1 mm",new Vector2(760,-460),220,()=>{var c=Config();if(c!=null)c.tipToContactKeyboardMeters.y=Mathf.Max(-.03f,c.tipToContactKeyboardMeters.y-.001f);});
            CreateButton("PAD Y +1 mm",new Vector2(990,-460),210,()=>{var c=Config();if(c!=null)c.tipToContactKeyboardMeters.y=Mathf.Min(.03f,c.tipToContactKeyboardMeters.y+.001f);});
            CreateButton("Y BAND -1 mm",new Vector2(760,-520),220,()=>{var c=Config();if(c!=null){c.belowSurfaceToleranceMeters=Mathf.Max(0,c.belowSurfaceToleranceMeters-.001f);c.aboveSurfaceToleranceMeters=Mathf.Max(0,c.aboveSurfaceToleranceMeters-.001f);}});
            CreateButton("Y BAND +1 mm",new Vector2(990,-520),210,()=>{var c=Config();if(c!=null){c.belowSurfaceToleranceMeters+=.001f;c.aboveSurfaceToleranceMeters+=.001f;}});
            CreateText("Correction Instructions",transform,"旧方式：単一指補正　新方式：全指ルールベース補正\n手と指、補正方式を選び、補正をONにします。\n記録開始 → 演奏 → 記録停止でRawデータを保存します。\n設定変更前に鍵盤とPinchを離してください。",
                new Vector2(760,-590),new Vector2(450,120),20,TextAlignmentOptions.TopLeft,Color.white);
        }
        void UpdateCorrectionStatus()
        {
            if(m_CorrectionStatus==null||m_Client==null)return;
            var c=m_Client.CorrectionSettings;var latest=m_Client.Correction!=null?m_Client.Correction.Latest:default;
            var rules=m_Client.RuleSettings;
            if(rules.method!=HandCorrectionMethod.Legacy)
            {
                var fingerIndex=Mathf.Clamp((int)c.finger,0,4);
                var p=m_Client.PoseCorrection?.New;var contact=p!=null?p.Contacts[fingerIndex]:default;
                var delta=p!=null&&p.SolvedThisFrame?p.Solver.Metrics[fingerIndex].correctedTip-p.Solver.Metrics[fingerIndex].rawTip:Vector3.zero;
                m_CorrectionStatus.text=$"{rules.method}: {(c.enabled&&rules.method!=HandCorrectionMethod.Raw?"ON":"OFF")} / {(c.leftHand?"LEFT":"RIGHT")} {c.finger}\n"+
                    $"Strength {c.strength:F2} / max {c.maxCorrectionMeters*1000:F0} / RMS {rules.wholeHandRmsLimitMeters*1000:F0} mm\n"+
                    $"R1 {rules.contact} / ROM {rules.UseJoints} / R2 {rules.UseEnvironment&&rules.nonpenetration}\n"+
                    $"{contact.state} / note {contact.note} / {delta.magnitude*1000:F1} mm\n{p?.Reason}";
                return;
            }
            m_CorrectionStatus.text=$"MIDI TIP: {(c.enabled?"ON":"OFF")} / {(c.leftHand?"LEFT":"RIGHT")} {c.finger}\n"+
                $"Strength {c.strength:F2} / max {c.maxCorrectionMeters*1000:F0} mm\n"+
                $"Pad Y {c.tipToContactKeyboardMeters.y*1000:F0} mm / band -{c.belowSurfaceToleranceMeters*1000:F0},+{c.aboveSurfaceToleranceMeters*1000:F0} mm\n"+
                $"{latest.State} / note {latest.Note} / {latest.DeltaKeyboard.magnitude*1000:F1} mm\n{latest.Reason}";
        }

        void UpdatePassthroughFailureActions()
        {
            var retryAvailable = m_Calibration != null && m_Calibration.PassthroughRetryAvailable;
            if (m_CaptureAButtonLabel != null)
                m_CaptureAButtonLabel.text = retryAvailable ? "透過表示を再試行" : "Aを登録";
            if (m_CancelCaptureButtonLabel != null)
                m_CancelCaptureButtonLabel.text = retryAvailable ? "位置合わせを中止" : "登録を中止";
        }

        public void ConfigureInputMode(UiInputMode mode)
        {
            var useXri = mode == UiInputMode.XriStandard;
            Safety?.ConfigureInputMode(useXri);
            if (m_LegacyRaycaster != null)
                m_LegacyRaycaster.enabled = !useXri;
            if (m_XriRaycaster != null)
                m_XriRaycaster.enabled = useXri;
            if (m_IpKeyboardRaycaster != null)
                m_IpKeyboardRaycaster.enabled = useXri;
            if (m_HandInputDiagnostic != null)
                m_HandInputDiagnostic.gameObject.SetActive(!useXri);
            ApplyLayout();
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
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
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
            // Keep the 430-unit keypad outside the 720-unit main panel.
            // Both canvases inherit the same world placement and safety CanvasGroup.
            keyboardCanvasRect.anchoredPosition3D = new Vector3(0f, -150f, -8f);
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
            var text = CreateText(label + " Label", go.transform, JapaneseLabel(label), Vector2.zero, size,
                label.Length > 2 ? 13f : 20f, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform, 3f, 3f, 2f, 2f, -2f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Safety.Run(label, action));
        }

        public void OpenIpKeyboard()
        {
            if (Safety != null && !Safety.CanUseControls) return;
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
            if (Safety != null && !Safety.CanUseControls) return;
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
            if (Safety != null && !Safety.CanUseControls) return false;
            if (!IsValidIpv4(m_EditingIp))
            {
                ShowIpError("IPv4アドレスを確認してください。");
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
                ? "鍵盤：88鍵" : "鍵盤：13鍵";
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
                Debug.LogWarning("保存済みPC IPを読み込めないため既定値を使います。 " + e.Message, this);
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
                Debug.LogWarning("PC IPを保存できませんでした。 " + e.Message, this);
            }
        }

        TMP_Text CreateButton(string label, Vector2 position, float width, UnityEngine.Events.UnityAction action, bool confirm = false)
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
            var text = CreateText(label + " Label", go.transform, JapaneseLabel(label), Vector2.zero, rect.sizeDelta,
                18, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform, 3f, 3f, 2f, 2f, -2f);
            var button = go.GetComponent<Button>();
            button.interactable = true;
            button.targetGraphic = image;
            button.onClick.AddListener(() => Safety.Run(JapaneseLabel(label), action, confirm));
            return text;
        }

        static string JapaneseLabel(string label)
        {
            switch (label)
            {
                case "DETAIL SETTINGS": return "詳細設定";
                case "COMPACT / NORMAL": return "折りたたみ / 展開";
                case "RECENTER UI": return "UIを正面に移動";
                case "START RAW REC": return "記録開始";
                case "STOP RAW REC": return "記録停止";
                case "TIP ON / OFF": return "指先補正 ON / OFF";
                case "CONNECT": return "接続";
                case "DISCONNECT": return "切断";
                case "CALIBRATION A": return "Aを登録";
                case "CALIBRATION B": return "Bを登録";
                case "CALIBRATION C": return "Cを登録";
                case "SAVE CALIBRATION": return "位置合わせを保存";
                case "CANCEL CAPTURE": return "登録を中止";
                case "RETRY PASSTHROUGH": return "透過表示を再試行";
                case "CANCEL CALIBRATION": return "位置合わせを中止";
                case "CORRECTION ON/OFF": return "指先補正 ON/OFF";
                case "LEFT / RIGHT": return "使用する手を切替";
                case "NEXT FINGER": return "使用する指を切替";
                case "HAND MESH": return "手の表示";
                case "HAND GAMEOBJECTS": return "手の表示（診断）";
                case "HAND GPU": return "手の表示（高速）";
                case "KEYBOARD: 88 KEYS": return "鍵盤：88鍵";
                case "KEYBOARD: 13 KEYS": return "鍵盤：13鍵";
                case "PC / CALIBRATION": return "MIDI接続";
                case "BLE MIDI": return "BLE MIDI";
                case "CORRECTION": return "補正設定";
                case "LOG / DIAGNOSTICS": return "記録・診断";
                case "UI POSITION": return "UI配置";
                case "NEW RULE SETTINGS": return "新方式のルール設定";
                case "METHOD NEXT": return "補正方式：旧方式 / 新方式";
                case "NOTE MAP / LIMITS": return "指の割当 / 補正制限";
                case "R1 CONTACT ON/OFF": return "接触制約 ON/OFF";
                case "JOINT RULES ON/OFF": return "関節制約 ON/OFF";
                case "R2 NONPEN ON/OFF": return "非貫通制約 ON/OFF";
                case "INTERFERENCE ON/OFF": return "指同士の干渉 ON/OFF";
                case "MANUAL NOTE MAP ON/OFF": return "手動の打鍵指割当 ON/OFF";
                case "BACK TO RULE SETTINGS": return "ルール設定に戻る";
                case "ASSIGN SELECTED FINGER": return "選択中の指を割当";
                case "CLEAR THIS NOTE": return "このノートの割当解除";
                case "CLEAR ALL NOTE MAP": return "すべての割当を解除";
                case "MORE JOINTS / PAD": return "関節・接触位置の詳細";
                case "BACK TO NOTE MAP": return "指の割当に戻る";
                case "RULE NEXT FINGER": return "設定する指を切替";
                case "FACE USER": return "正面に移動";
                case "RESET PLACEMENT": return "配置をリセット";
                case "NEARER 10 cm": return "近づける 10 cm";
                case "FARTHER 10 cm": return "遠ざける 10 cm";
                case "LIMITS -10 cm": return "最短距離 -10 cm";
                case "LIMITS +10 cm": return "最長距離 +10 cm";
                case "BACKSPACE": return "1文字削除";
                case "CLEAR": return "消去";
                case "APPLY": return "適用";
                case "CANCEL": return "キャンセル";
                default: return label.Replace("STRENGTH", "補正の強さ")
                    .Replace("FINGER MAX", "指の最大距離").Replace("MAX", "最大補正距離")
                    .Replace("FINGER PAD", "指の接触位置").Replace("PAD Y", "接触位置Y")
                    .Replace("Y BAND", "上下の許容範囲").Replace("ENGAGE", "補正開始")
                    .Replace("RELEASE", "補正解除").Replace("RADIUS", "指の半径")
                    .Replace("DEPTH", "許容深度").Replace("SMOOTH", "平滑化")
                    .Replace("MAP NOTE", "MIDIノート").Replace("HAND RMS", "全指RMS上限")
                    .Replace("RAW CONE", "Rawからの角度").Replace("BEND", "屈曲")
                    .Replace("OFFSET SPEED", "補正速度").Replace("ITERATIONS", "反復回数");
            }
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
            if (m_JapaneseFont != null) QuestUiFontFallback.Release();
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
