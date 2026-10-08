using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using QuestPianoMotion.Research.Distributed;

namespace QuestPianoMotion.Research
{
    /// <summary>Standalone diagnostic subscriber. Never calls keyboard, pose or PC recorders.</summary>
    public sealed class BleMidiDiagnostics : MonoBehaviour
    {
        BleMidiInput m_Input;
        TMP_Text m_Status;
        GameObject m_Panel;
        int m_Selected = -1;
        PerformanceUiSafety m_Safety;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Install;
            SceneManager.sceneLoaded += Install;
        }
        static void Install(Scene scene, LoadSceneMode mode)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (scene.name != "PianoDistributedQuest") return;
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<BleMidiDiagnostics>(true) != null) return;
            var go = new GameObject("BLE MIDI Diagnostics");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<BleMidiInput>(); go.AddComponent<BleMidiDiagnostics>();
#endif
        }
        IEnumerator Start()
        {
            m_Input = GetComponent<BleMidiInput>();
            DistributedQuestUi ui;
            while ((ui = FindFirstObjectByType<DistributedQuestUi>()) == null || ui.WorldCanvas == null) yield return null;
            m_Safety = ui.Safety;
            m_Panel = new GameObject("BLE diagnostic panel", typeof(RectTransform), typeof(Image));
            m_Panel.layer = ui.WorldCanvas.gameObject.layer;
            var rect = (RectTransform)m_Panel.transform; rect.SetParent(ui.WorldCanvas.transform, false);
            Position(rect, new Vector2(740, -20), new Vector2(620, 650));
            m_Panel.GetComponent<Image>().color = new Color(.025f, .03f, .04f, .98f);
            m_Status = Text("Status", rect, ui.FontAsset, new Vector2(15, -15), new Vector2(590, 395), "BLE MIDI DIAGNOSTICS", 22);
            Button(rect, ui.FontAsset, "PERMISSIONS", 15, -425, m_Input.RequestPermissions);
            Button(rect, ui.FontAsset, "SCAN 12s", 215, -425, m_Input.StartScan);
            Button(rect, ui.FontAsset, "STOP SCAN", 415, -425, m_Input.StopScan);
            Button(rect, ui.FontAsset, "NEXT DEVICE", 15, -485, () =>
            {
                if (m_Input.Devices.Count == 0) return;
                m_Selected = (m_Selected + 1) % m_Input.Devices.Count; m_Input.SelectDevice(m_Selected);
            });
            Button(rect, ui.FontAsset, "CONNECT", 215, -485, () => m_Input.ConnectSelectedDevice(), true);
            Button(rect, ui.FontAsset, "DISCONNECT", 415, -485, m_Input.Disconnect, true);
            Text("Help", rect, ui.FontAsset, new Vector2(215, -550), new Vector2(390, 85), "Select an address before CONNECT.\nDirect GATT; manual reconnect.\nMIDI drives keyboard and research logs.", 18);
        }
        void Update()
        {
            if (m_Status == null || m_Input == null) return;
            m_Status.text = "BLE MIDI DIAGNOSTICS\n" +
                $"Capability: {m_Input.Capability}\nScan: {m_Input.ScanState} found={m_Input.Devices.Count}\n" +
                $"Selected: {m_Input.SelectedDevice}\nState: {m_Input.ConnectionState} generation={m_Input.Generation}\n" +
                $"Device: {m_Input.DeviceInfo}\nLast: {m_Input.LastEventText}\n" +
                $"Dropped: {m_Input.Dropped} stale: {m_Input.Stale}\nReconnect: press CONNECT\n" +
                "Clock: Android nanoTime -> Quest Stopwatch\n" +
                (string.IsNullOrEmpty(m_Input.LogError) ? "Log: ble_diagnostics/quest_ble_*.csv" : "Log error: " + m_Input.LogError);
        }
        void OnDestroy() { if (m_Panel != null) Destroy(m_Panel); }
        static void Position(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size; }
        static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, Vector2 position, Vector2 size, string value, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            Position((RectTransform)go.transform, position, size);
            ((RectTransform)go.transform).anchoredPosition3D = new Vector3(position.x, position.y, -2);
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = font; text.text = value; text.fontSize = fontSize;
            text.color = Color.white; text.raycastTarget = false; return text;
        }
        void Button(Transform parent, TMP_FontAsset font, string caption, float x, float y, UnityEngine.Events.UnityAction action, bool confirm = false)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            Position((RectTransform)go.transform, new Vector2(x, y), new Vector2(190, 48));
            go.GetComponent<Image>().color = new Color(.12f, .2f, .3f, 1);
            go.GetComponent<Button>().onClick.AddListener(() => m_Safety.Run("BLE " + caption, action, confirm));
            var label = Text("Label", go.transform, font, new Vector2(5, -5), new Vector2(180, 38), caption, 20);
            label.alignment = TextAlignmentOptions.Center;
        }
    }
}
