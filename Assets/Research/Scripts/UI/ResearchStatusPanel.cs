using System;
using UnityEngine;
using UnityEngine.UI;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class ResearchStatusPanel : MonoBehaviour
    {
        XRHandPoseProvider m_Hands;
        AndroidMidiInput m_Midi;
        PianoCalibrationManager m_Calibration;
        SynchronizedSessionRecorder m_Recorder;
        Text m_Status;
        Text m_DeviceButtonLabel;
        Text m_PointButtonLabel;
        Transform m_CanvasTransform;
        float m_NextRefresh;
        Font m_Font;

        public void Initialize(XRHandPoseProvider hands, AndroidMidiInput midi,
            PianoCalibrationManager calibration, SynchronizedSessionRecorder recorder)
        {
            m_Hands = hands;
            m_Midi = midi;
            m_Calibration = calibration;
            m_Recorder = recorder;
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
            var canvasObject = new GameObject("Piano Research Status Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            m_CanvasTransform = canvasObject.transform;
            canvasObject.transform.localScale = Vector3.one * 0.00075f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(760f, 900f);

            var trackedRaycaster = Type.GetType(
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            if (trackedRaycaster != null) canvasObject.AddComponent(trackedRaycaster);
            else canvasObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 0.92f);

            m_Status = CreateText(panel.transform, "Status", new Vector2(24f, -22f), new Vector2(712f, 430f), 25,
                TextAnchor.UpperLeft);
            var labels = new[] { "Refresh MIDI", "Select / Connect", "Point Hand Left/Right", "Calibration Start", "Register Next Point",
                "Save Calibration", "Load Calibration", "Start Recording", "Stop Recording", "Move Panel Here" };
            Action[] actions = { m_Midi.RefreshDeviceList, m_Midi.SelectNextDevice, m_Calibration.ToggleCaptureHand, m_Calibration.StartCalibration,
                m_Calibration.RegisterNextPoint, m_Calibration.SaveCalibration, m_Calibration.LoadCalibration,
                m_Recorder.StartRecordingAction, m_Recorder.StopRecording, MovePanelHere };
            for (var i = 0; i < labels.Length; ++i)
            {
                var text = CreateButton(panel.transform, labels[i], i % 2, i / 2, actions[i]);
                if (i == 1) m_DeviceButtonLabel = text;
                if (i == 4) m_PointButtonLabel = text;
            }

            MovePanelHere();
        }

        void MovePanelHere()
        {
            var camera = Camera.main;
            if (camera == null || m_CanvasTransform == null) return;

            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(camera.transform.up, Vector3.up);
            }
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            m_CanvasTransform.SetParent(ResearchServices.TrackingOrigin, true);
            m_CanvasTransform.SetPositionAndRotation(
                camera.transform.position + forward * 1.0f + camera.transform.right * 0.35f - Vector3.up * 0.05f,
                Quaternion.LookRotation(forward, Vector3.up));
            m_CanvasTransform.localScale = Vector3.one * 0.00075f;
        }

        Text CreateButton(Transform parent, string label, int column, int row, Action action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f + column * 360f, -475f - row * 78f);
            rect.sizeDelta = new Vector2(336f, 58f);
            go.GetComponent<Image>().color = new Color(0.13f, 0.25f, 0.38f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() => action?.Invoke());
            var text = CreateText(go.transform, "Label", Vector2.zero, rect.sizeDelta, 22,
                TextAnchor.MiddleCenter, true);
            text.text = label;
            return text;
        }

        Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize,
            TextAnchor alignment, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            if (stretch)
            {
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = size;
            }
            var text = go.GetComponent<Text>();
            text.font = m_Font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = name;
            return text;
        }

        void Refresh()
        {
            if (m_Status == null) return;
            m_Status.text =
                $"XR Hands Ready: {m_Hands.IsReady}\n" +
                $"Left Hand Tracked: {m_Hands.LeftTracked}\n" +
                $"Right Hand Tracked: {m_Hands.RightTracked}\n" +
                $"MIDI Device: {m_Midi.SelectedDeviceName}\n" +
                $"MIDI Connected: {m_Midi.IsConnected}\n" +
                $"Last MIDI Event: {m_Midi.LastEventText}\n" +
                $"Calibration Point Hand: {m_Calibration.CaptureHandName}\n" +
                $"Calibration Status: {m_Calibration.StatusText}\n" +
                $"Recording Status: {(m_Recorder.IsRecording ? "Recording" : "Stopped")}\n" +
                $"Recording Time: {m_Recorder.RecordingTimeSeconds:F1} s\n" +
                $"Session ID: {m_Recorder.SessionId}\n" +
                $"Save Path: {m_Recorder.SessionPath}";
            if (m_DeviceButtonLabel != null) m_DeviceButtonLabel.text = "Select / Connect\n" + m_Midi.SelectedDeviceName;
            if (m_PointButtonLabel != null) m_PointButtonLabel.text = "Register Next Point\n" + m_Calibration.CapturedPointCount + "/3";
        }
    }
}
