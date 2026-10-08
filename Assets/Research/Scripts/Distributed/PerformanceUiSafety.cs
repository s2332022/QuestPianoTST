using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace QuestPianoMotion.Research.Distributed
{
    /// <summary>Local UI policy clock; never compares PC/BLE sender timestamps with Quest time.</summary>
    public sealed class PerformanceUiGate
    {
        public const double CooldownSeconds = 1.0;
        double m_UnlockAt = double.NegativeInfinity;
        bool m_WaitForRelease;

        public void Observe(MidiEventType type, double questNow)
        {
            if (type == MidiEventType.Other) return;
            m_UnlockAt = Math.Max(m_UnlockAt, questNow + CooldownSeconds);
            m_WaitForRelease = true;
        }

        public bool Tick(double questNow, bool heldNotes, bool uiSelectHeld)
        {
            // Snapshot-only state repairs may release a key without a MIDI callback.
            if (heldNotes) m_UnlockAt = Math.Max(m_UnlockAt, questNow + CooldownSeconds);
            if (heldNotes || questNow < m_UnlockAt)
            {
                m_WaitForRelease = true;
                return true;
            }
            if (m_WaitForRelease && uiSelectHeld) return true;
            m_WaitForRelease = false;
            return false;
        }
    }

    /// <summary>Thin safety adapter around standard UGUI/XRI; observes MIDI without consuming it.</summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class PerformanceUiSafety : MonoBehaviour
    {
        public const double ConfirmationSeconds = 10.0;
        readonly PerformanceUiGate m_Gate = new PerformanceUiGate();
        DistributedQuestUi m_Ui;
        CanvasGroup m_Group;
        VirtualPianoKeyboard m_Keyboard;
        NetworkMidiInput m_Udp;
        BleMidiInput m_Ble;
        XriHandUiInputController m_InputController;
        TMP_Text m_Status;
        TMP_Text m_ConfirmationText;
        GameObject m_Dialog;
        UnityAction m_Pending;
        double m_ExpiresAt;
        bool m_Locked;

        public bool Locked => m_Locked;
        public bool ConfirmationPending => m_Pending != null;
        public bool CanUseControls => !m_Locked && m_Pending == null;

        public void Initialize(DistributedQuestUi ui, CanvasGroup group, VirtualPianoKeyboard keyboard)
        {
            m_Ui = ui;
            m_Group = group;
            m_Keyboard = keyboard;
            BindSources();
        }

        public void SetStatusText(TMP_Text text) => m_Status = text;

        void BindSources()
        {
            if (m_Udp == null)
            {
                m_Udp = FindAnyObjectByType<NetworkMidiInput>();
                if (m_Udp != null) m_Udp.MessageReceived += OnMidi;
            }
            if (m_Ble == null)
            {
                m_Ble = FindAnyObjectByType<BleMidiInput>();
                if (m_Ble != null) m_Ble.MessageReceived += OnMidi;
            }
            if (m_InputController == null)
                m_InputController = FindAnyObjectByType<XriHandUiInputController>();
        }

        void Update()
        {
            BindSources();
            if (m_Keyboard == null) m_Keyboard = FindAnyObjectByType<VirtualPianoKeyboard>();
            var now = Time.realtimeSinceStartupAsDouble;
            var interactor = m_InputController != null ? m_InputController.RightHandUiInteractor : null;
            var selectHeld = interactor != null && interactor.uiPressInput.ReadIsPerformed();
            SetLocked(m_Gate.Tick(now, m_Keyboard != null && m_Keyboard.HasActiveMidiNotes, selectHeld));
            if (m_Pending != null && now >= m_ExpiresAt) CancelPending();
        }

        void OnMidi(MidiMessage message)
        {
            if (message.EventType == MidiEventType.Other) return;
            m_Gate.Observe(message.EventType, Time.realtimeSinceStartupAsDouble);
            SetLocked(true);
        }

        void SetLocked(bool locked)
        {
            m_Locked = locked;
            if (m_Group != null)
            {
                m_Group.interactable = !locked;
                m_Group.blocksRaycasts = !locked;
                m_Group.alpha = locked ? 0.45f : 1f;
            }
            if (locked)
            {
                CancelPending();
                // Clear navigation focus as well as blocking tracked-device ray hits.
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && selected.transform.IsChildOf(transform))
                    EventSystem.current.SetSelectedGameObject(null);
            }
            if (m_Status != null)
                m_Status.text = locked ? "PLAYING / RELEASE PINCH - UI LOCKED" :
                    (m_Pending != null ? "CONFIRM OR CANCEL (10 seconds)" : "UI READY - aim ray and pinch");
        }

        public void Run(string label, UnityAction action, bool confirm = false)
        {
            // Also protects actions dispatched programmatically or queued before a MIDI event.
            if (!CanUseControls || action == null) return;
            if (!confirm) { action(); return; }
            m_Ui.CancelIpKeyboard();
            EnsureDialog();
            m_Pending = action;
            m_ExpiresAt = Time.realtimeSinceStartupAsDouble + ConfirmationSeconds;
            m_ConfirmationText.text = label + "?\nChoose CONFIRM to continue.\nPlaying again cancels this request.";
            m_Dialog.SetActive(true);
            m_Dialog.transform.SetAsLastSibling();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        public void ConfirmPending()
        {
            if (m_Locked || Time.realtimeSinceStartupAsDouble >= m_ExpiresAt) { CancelPending(); return; }
            var action = m_Pending;
            CancelPending();
            action?.Invoke();
        }

        public void CancelPending()
        {
            m_Pending = null;
            if (m_Dialog != null) m_Dialog.SetActive(false);
        }

        void EnsureDialog()
        {
            if (m_Dialog != null) return;
            m_Dialog = new GameObject("UI operation confirmation", typeof(RectTransform), typeof(Canvas), typeof(Image));
            m_Dialog.layer = gameObject.layer;
            m_Dialog.transform.SetParent(transform, false);
            Position((RectTransform)m_Dialog.transform, Vector2.zero, new Vector2(1360, 970));
            // Cover the main panel, BLE extension and independent IP canvas with one modal.
            var canvas = m_Dialog.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = m_Ui.WorldCanvas.sortingOrder + 10;
            canvas.worldCamera = m_Ui.WorldCanvas.worldCamera;
            var raycaster = m_Dialog.AddComponent<TrackedDeviceGraphicRaycaster>();
            raycaster.ignoreReversedGraphics = false;
            raycaster.checkFor2DOcclusion = false;
            raycaster.checkFor3DOcclusion = false;
            m_Dialog.AddComponent<GraphicRaycaster>();
            ConfigureInputMode(m_Ui.XriRaycaster.enabled);
            m_Dialog.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.04f, 0.96f);
            m_ConfirmationText = Text("Confirmation", m_Dialog.transform, new Vector2(40, -300), new Vector2(640, 160), 28);
            MakeButton("CONFIRM", new Vector2(40, -650), ConfirmPending);
            MakeButton("CANCEL", new Vector2(390, -650), CancelPending);
        }

        public void ConfigureInputMode(bool useXri)
        {
            if (m_Dialog == null) return;
            m_Dialog.GetComponent<TrackedDeviceGraphicRaycaster>().enabled = useXri;
            m_Dialog.GetComponent<GraphicRaycaster>().enabled = !useXri;
        }

        void MakeButton(string caption, Vector2 position, UnityAction action)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = gameObject.layer;
            go.transform.SetParent(m_Dialog.transform, false);
            Position((RectTransform)go.transform, position, new Vector2(280, 64));
            go.GetComponent<Image>().color = new Color(0.12f, 0.32f, 0.62f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            button.onClick.AddListener(action);
            var text = Text(caption + " Label", go.transform, Vector2.zero, new Vector2(280, 64), 24);
            text.text = caption;
            text.alignment = TextAlignmentOptions.Center;
        }

        TMP_Text Text(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            Position((RectTransform)go.transform, position, size);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = m_Ui.FontAsset;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        static void Position(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition3D = new Vector3(position.x, position.y, -4);
            rect.sizeDelta = size;
        }

        void OnDisable() => CancelPending();

        void OnDestroy()
        {
            if (m_Udp != null) m_Udp.MessageReceived -= OnMidi;
            if (m_Ble != null) m_Ble.MessageReceived -= OnMidi;
        }
    }
}
