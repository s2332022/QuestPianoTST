using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    [DefaultExecutionOrder(-190)]
    [DisallowMultipleComponent]
    public sealed class DistributedQuestComposition : MonoBehaviour
    {
        XRHandPoseProvider m_Hands;
        VirtualPianoKeyboard m_Keyboard;
        PianoCalibrationManager m_Calibration;
        MinimalHandVisualizer m_Visualizer;
        QuestHmdPoseGate m_PlacementGate;

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
                if (m_Calibration.Current.valid)
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
                m_Calibration.Current.valid)
                yield break;

            var xrOrigin = FindAnyObjectByType<XROrigin>();
            QuestSpatialPlacement.PlacePiano(m_Keyboard.KeyboardRoot, camera, xrOrigin);
            DefaultKeyboardPlaced = true;
            Debug.Log($"[QuestPlacement] Piano placed fallback={m_PlacementGate.UsedFallback} cameraRelative={camera.transform.InverseTransformPoint(m_Keyboard.KeyboardRoot.position)}", this);
        }

        void OnDestroy()
        {
            if (m_Calibration != null && m_Keyboard != null)
                m_Calibration.CalibrationChanged -= m_Keyboard.ApplyCalibration;
        }
    }
}