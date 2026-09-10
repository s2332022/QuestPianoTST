using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class PianoResearchRuntime : MonoBehaviour
    {
        XRHandPoseProvider m_Hands;
        AndroidMidiInput m_Midi;
        VirtualPianoKeyboard m_Keyboard;
        PianoCalibrationManager m_Calibration;
        SynchronizedSessionRecorder m_Recorder;

        void Awake()
        {
            ResearchServices.TrackingOrigin = ResearchServices.FindTrackingOrigin();
            m_Hands = gameObject.AddComponent<XRHandPoseProvider>();
            m_Midi = gameObject.AddComponent<AndroidMidiInput>();
            m_Keyboard = gameObject.AddComponent<VirtualPianoKeyboard>();
            m_Calibration = gameObject.AddComponent<PianoCalibrationManager>();
            m_Recorder = gameObject.AddComponent<SynchronizedSessionRecorder>();

            var camera = Camera.main;
            if (camera != null && m_Keyboard.KeyboardRoot != null)
            {
                var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
                m_Keyboard.KeyboardRoot.SetPositionAndRotation(
                    camera.transform.position + forward * 0.65f + Vector3.down * 0.35f,
                    Quaternion.LookRotation(forward, Vector3.up));
            }

            m_Calibration.Initialize(m_Hands);
            m_Midi.MessageReceived += OnMidi;
            m_Calibration.CalibrationChanged += m_Keyboard.ApplyCalibration;
            if (m_Calibration.Current.valid) m_Keyboard.ApplyCalibration(m_Calibration.Current);
            m_Recorder.Initialize(m_Hands, m_Midi, m_Keyboard, m_Calibration,
                camera != null ? camera.transform : null);

            var panel = gameObject.AddComponent<ResearchStatusPanel>();
            panel.Initialize(m_Hands, m_Midi, m_Calibration, m_Recorder);
        }

        void OnDestroy()
        {
            if (m_Midi != null) m_Midi.MessageReceived -= OnMidi;
            if (m_Calibration != null && m_Keyboard != null)
                m_Calibration.CalibrationChanged -= m_Keyboard.ApplyCalibration;
        }

        void OnMidi(MidiMessage message)
        {
            m_Keyboard.ApplyMidi(in message);
        }
    }

    static class PianoResearchBootstrap
    {
        static bool s_Registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (s_Registered) return;
            s_Registered = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "HandTrackingResearch" ||
                UnityEngine.Object.FindAnyObjectByType<PianoResearchRuntime>() != null) return;
            new GameObject("Piano Research Runtime").AddComponent<PianoResearchRuntime>();
        }
    }
}
