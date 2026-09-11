using System;
using UnityEngine;
using UnityEngine.UI;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class MinimalResearchStatusPanel : MonoBehaviour
    {
        XRHandPoseProvider m_Hands;
        AndroidMidiInput m_Midi;
        PianoCalibrationManager m_Calibration;
        SynchronizedSessionRecorder m_Recorder;
        Transform m_UiRoot;
        Text m_Status;
        float m_NextRefresh;
        Font m_Font;

        public void Initialize(XRHandPoseProvider hands, AndroidMidiInput midi,
            PianoCalibrationManager calibration, SynchronizedSessionRecorder recorder, Transform uiRoot)
        {
            m_Hands = hands;
            m_Midi = midi;
            m_Calibration = calibration;
            m_Recorder = recorder;
            m_UiRoot = uiRoot;
            BuildUi();
            Refresh();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        void BuildUi()
        {
            var camera = Camera.main;
            if (camera == null) return;
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasObject = new GameObject("Minimal Research Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (m_UiRoot != null) canvasObject.transform.SetParent(m_UiRoot, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(760f, 820f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 0.94f);

            m_Status = CreateText(panel.transform, "Status", new Vector2(24f, -20f),
                new Vector2(712f, 260f), 25, TextAnchor.UpperLeft);

            var labels = new[]
            {
                "Refresh MIDI", "Connect MIDI", "Point Hand Left", "Point Hand Right",
                "Capture A", "Capture B", "Capture C", "Save Calibration", "Load Calibration",
                "Start Recording", "Stop Recording"
            };
            Action[] actions =
            {
                m_Midi.RefreshDeviceList, m_Midi.ConnectSelectedDevice,
                m_Calibration.SelectLeftHand, m_Calibration.SelectRightHand,
                m_Calibration.CaptureA, m_Calibration.CaptureB, m_Calibration.CaptureC,
                m_Calibration.SaveCalibration, m_Calibration.LoadCalibration,
                m_Recorder.StartRecordingAction, m_Recorder.StopRecording
            };
            for (var i = 0; i < labels.Length; ++i)
                CreateButton(panel.transform, labels[i], i % 2, i / 2, actions[i]);

            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            canvasObject.transform.SetPositionAndRotation(
                camera.transform.position + forward * 0.95f + camera.transform.right * 0.34f,
                Quaternion.LookRotation(forward, Vector3.up));
            canvasObject.transform.localScale = Vector3.one * 0.00075f;
        }

        void CreateButton(Transform parent, string label, int column, int row, Action action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f + column * 360f, -295f - row * 82f);
            rect.sizeDelta = new Vector2(336f, 62f);
            go.GetComponent<Image>().color = new Color(0.13f, 0.25f, 0.38f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() => action?.Invoke());
            var text = CreateText(go.transform, "Label", Vector2.zero, rect.sizeDelta, 22,
                TextAnchor.MiddleCenter, true);
            text.text = label;
        }

        Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize,
            TextAnchor alignment, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }
            var text = go.GetComponent<Text>();
            text.font = m_Font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        void Refresh()
        {
            if (m_Status == null || m_Hands == null || m_Midi == null || m_Calibration == null || m_Recorder == null)
                return;
            m_Status.text =
                $"Left Hand Tracked: {m_Hands.LeftTracked}\n" +
                $"Right Hand Tracked: {m_Hands.RightTracked}\n" +
                $"MIDI Connected: {m_Midi.IsConnected}\n" +
                $"Last MIDI Event: {m_Midi.LastEventText}\n" +
                $"Calibration Status: {m_Calibration.StatusText}\n" +
                $"Recording Status: {(m_Recorder.IsRecording ? "Recording" : "Stopped")}\n" +
                $"Recording Time: {m_Recorder.RecordingTimeSeconds:F1} s";
        }
    }
}
