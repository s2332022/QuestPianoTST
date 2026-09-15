using QuestPianoMotion.Research.Distributed;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace QuestPianoMotion.Research
{
    public enum UiInputMode
    {
        XriStandard,
        LegacyDiagnostic
    }

    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class XriHandUiInputController : MonoBehaviour
    {
        [SerializeField] UiInputMode m_Mode = UiInputMode.XriStandard;
        [SerializeField] MinimalHandUiInputModule m_LegacyInputModule;
        [SerializeField] NearFarInteractor m_RightHandUiInteractor;
        [SerializeField] XRUIInputModule m_XrUiInputModule;
        [SerializeField] InputActionReference m_RightHandIsTracked;

        bool m_LastTracked = true;

        public UiInputMode Mode => m_Mode;
        public MinimalHandUiInputModule LegacyInputModule => m_LegacyInputModule;
        public NearFarInteractor RightHandUiInteractor => m_RightHandUiInteractor;
        public XRUIInputModule XrUiInputModule => m_XrUiInputModule;
        public bool BothInputModesActive => m_LegacyInputModule != null && m_LegacyInputModule.enabled &&
                                            m_XrUiInputModule != null && m_XrUiInputModule.enabled &&
                                            m_RightHandUiInteractor != null && m_RightHandUiInteractor.gameObject.activeInHierarchy;

        void Awake()
        {
            ApplyMode();
            LogRuntimeStructure();
        }

        void Start()
        {
            ApplyMode();
        }

        void Update()
        {
            if (m_Mode != UiInputMode.XriStandard || m_RightHandUiInteractor == null ||
                m_RightHandIsTracked == null || m_RightHandIsTracked.action == null)
                return;

            var action = m_RightHandIsTracked.action;
            // In EditMode/PlayMode tests there may be no bound XR device. Only gate the
            // interactor when an actual right-hand tracking control is available.
            if (action.controls.Count == 0)
                return;

            var tracked = action.IsPressed();
            if (tracked == m_LastTracked && m_RightHandUiInteractor.gameObject.activeSelf == tracked)
                return;

            m_LastTracked = tracked;
            m_RightHandUiInteractor.gameObject.SetActive(tracked);
            Debug.Log($"[XRI Hand UI] Right hand tracking {(tracked ? "acquired" : "lost")}; interactor {(tracked ? "enabled" : "disabled")}.", this);
        }

        public void Configure(MinimalHandUiInputModule legacyInputModule, NearFarInteractor rightHandUiInteractor,
            XRUIInputModule xrUiInputModule, InputActionReference rightHandIsTracked)
        {
            m_LegacyInputModule = legacyInputModule;
            m_RightHandUiInteractor = rightHandUiInteractor;
            m_XrUiInputModule = xrUiInputModule;
            m_RightHandIsTracked = rightHandIsTracked;
        }

        public void SetMode(UiInputMode mode)
        {
            m_Mode = mode;
            ApplyMode();
        }

        public void ApplyMode()
        {
            var useXri = m_Mode == UiInputMode.XriStandard;
            if (m_LegacyInputModule != null)
                m_LegacyInputModule.enabled = !useXri;
            if (m_XrUiInputModule != null)
                m_XrUiInputModule.enabled = useXri;
            if (m_RightHandUiInteractor != null)
                m_RightHandUiInteractor.gameObject.SetActive(useXri);

            var ui = FindAnyObjectByType<DistributedQuestUi>(FindObjectsInactive.Include);
            ui?.ConfigureInputMode(m_Mode);

            if (BothInputModesActive)
            {
                m_LegacyInputModule.enabled = false;
                Debug.LogError("[XRI Hand UI] Both input paths became active. Legacy input was disabled.", this);
            }
        }

        void LogRuntimeStructure()
        {
            var eventSystems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var managers = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var inputManagers = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var xrUiModules = FindObjectsByType<XRUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var rightInteractors = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[XRI Hand UI] mode={m_Mode} EventSystem={eventSystems.Length} XRInteractionManager={managers.Length} InputActionManager={inputManagers.Length} XRUIInputModule={xrUiModules.Length} NearFarInteractor={rightInteractors.Length}", this);
        }
    }
}
