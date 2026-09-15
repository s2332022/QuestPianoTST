using System.Collections.Generic;
using System.Text;
using QuestPianoMotion.Research.Distributed;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class MinimalHandUiInputModule : BaseInputModule
    {
        const int PointerId = -102;
        const float ClickFlashSeconds = 0.18f;
        readonly List<RaycastResult> m_RaycastResults = new List<RaycastResult>(16);
        readonly StringBuilder m_LogBuilder = new StringBuilder(1024);

        [Header("Input hand")]
        [SerializeField] bool m_UseLeftHand;
        [Header("Pinch hysteresis")]
        [SerializeField, Min(0.001f)] float m_PinchDownMeters = 0.025f;
        [SerializeField, Min(0.001f)] float m_PinchUpMeters = 0.035f;
        [Header("On-device diagnostics")]
        [SerializeField] bool m_EnableDiagnostics = true;
        [SerializeField] bool m_LogRaycastStackOnce = true;

        XRHandPoseProvider m_Hands;
        DistributedQuestUi m_Ui;
        Camera m_Camera;
        PointerEventData m_Pointer;
        bool m_PinchPressed;
        bool m_RequireOpen;
        bool m_PointerEntered;
        bool m_DownObserved;
        bool m_UpObserved;
        bool m_ClickObserved;
        bool m_RaycastStackLogged;
        bool m_PlacementErrorLogged;
        bool m_RuntimeStateLogged;
        float m_PinchDistance;
        float m_ClickFlashUntil;
        float m_NextBindAttempt;
        float m_NextDiagnosticUpdate;
        int m_ClickCount;
        string m_LastState;

        public bool UseLeftHand => m_UseLeftHand;
        public float PinchDownMeters => m_PinchDownMeters;
        public float PinchUpMeters => m_PinchUpMeters;
        public bool PinchPressed => m_PinchPressed;
        public int PointerDownCount { get; private set; }
        public int PointerUpCount { get; private set; }
        public int ClickCount => m_ClickCount;
        public GameObject CurrentTarget => m_Pointer != null ? m_Pointer.pointerCurrentRaycast.gameObject : null;
        public Vector3 LastScreenPoint { get; private set; }
        public bool LastScreenValid { get; private set; }
        public bool LastOverCanvas { get; private set; }
        public int LastRaycastCount { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            m_Pointer = new PointerEventData(eventSystem) { pointerId = PointerId };
            ValidateThresholds();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            m_RuntimeStateLogged = false;
        }

        public override void Process()
        {
            BindRuntimeReferences();
            LogRuntimeStateOnce();
            if (m_Hands == null || m_Camera == null)
            {
                CancelPressAndHover(true);
                UpdateVisuals(false, false, false, false, false, null, "missing runtime reference");
                return;
            }

            var tracked = m_UseLeftHand ? m_Hands.LeftTracked : m_Hands.RightTracked;
            var indexValid = m_Hands.TryGetWorldJoint(m_UseLeftHand, XRHandJointID.IndexTip, out var index);
            var thumbValid = m_Hands.TryGetWorldJoint(m_UseLeftHand, XRHandJointID.ThumbTip, out var thumb);
            ProcessSample(tracked, indexValid, thumbValid, index, thumb);
        }

        protected override void OnDisable()
        {
            CancelPressAndHover(true);
            SetCursor(false, false, false, Vector2.zero);
            base.OnDisable();
        }

        public void ConfigureForTests(Camera eventCamera, DistributedQuestUi ui = null)
        {
            m_Camera = eventCamera;
            m_Ui = ui;
            m_Ui?.ConfigureInputMode(UiInputMode.LegacyDiagnostic);
        }

        public void ConfigurePinch(bool useLeftHand, float downMeters, float upMeters)
        {
            m_UseLeftHand = useLeftHand;
            m_PinchDownMeters = downMeters;
            m_PinchUpMeters = upMeters;
            ValidateThresholds();
        }

        public void ProcessSample(bool tracked, bool indexValid, bool thumbValid, Pose index, Pose thumb)
        {
            BindRuntimeReferences();
            if (!tracked || !indexValid || !thumbValid || m_Camera == null)
            {
                CancelPressAndHover(true);
                UpdateVisuals(tracked, indexValid, thumbValid, false, false, null, "joint unavailable");
                return;
            }

            var screen = m_Camera.WorldToScreenPoint(index.position);
            LastScreenPoint = screen;
            m_PinchDistance = Vector3.Distance(index.position, thumb.position);
            var screenValid = screen.z > 0f && m_Camera.pixelRect.Contains(new Vector2(screen.x, screen.y));
            LastScreenValid = screenValid;
            var canvas = ResolveCanvas();
            var canvasInFront = canvas != null && canvas.worldCamera == m_Camera &&
                                m_Camera.transform.InverseTransformPoint(canvas.transform.position).z > 0f;
            if (!canvasInFront)
            {
                if (canvas != null && !m_PlacementErrorLogged)
                {
                    m_PlacementErrorLogged = true;
                    Debug.LogError($"[HandUI] Canvas is not in front of the event camera; input disabled. relative={m_Camera.transform.InverseTransformPoint(canvas.transform.position)} worldCamera={(canvas.worldCamera != null ? canvas.worldCamera.name : "null")}", this);
                }
                CancelPressAndHover(true);
                UpdateVisuals(true, true, true, screenValid, false, null, "canvas behind/wrong camera");
                return;
            }

            var uiPoint = new Vector2(screen.x, screen.y);
            var overCanvas = screenValid && RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)canvas.transform, uiPoint, m_Camera);
            LastOverCanvas = overCanvas;
            if (!overCanvas)
            {
                CancelPressAndHover(false);
                m_RequireOpen = true;
                m_PinchPressed = false;
                UpdateVisuals(true, true, true, screenValid, false, null, "outside UI");
                return;
            }

            var previous = m_Pointer.position;
            m_Pointer.position = uiPoint;
            m_Pointer.delta = m_Pointer.position - previous;
            m_Pointer.scrollDelta = Vector2.zero;

            m_RaycastResults.Clear();
            eventSystem.RaycastAll(m_Pointer, m_RaycastResults);
            LastRaycastCount = m_RaycastResults.Count;
            m_Pointer.pointerCurrentRaycast = FindFirstRaycast(m_RaycastResults);
            var current = m_Pointer.pointerCurrentRaycast.gameObject;
            var button = ResolveInteractableButton(current);
            HandlePointerExitAndEnter(m_Pointer, current);
            m_PointerEntered = current != null;
            LogRaycastStackIfRequested();

            var nextPinch = EvaluatePinch(m_PinchDistance);
            if (nextPinch && !m_PinchPressed)
                Press(m_Pointer, button != null ? button.gameObject : current);
            else if (!nextPinch && m_PinchPressed)
                Release(m_Pointer, button != null ? button.gameObject : current, true);
            m_PinchPressed = nextPinch;

            UpdateVisuals(true, true, true, true, true, button, current != null ? current.name : "UI");
        }

        bool EvaluatePinch(float distance)
        {
            if (m_RequireOpen)
            {
                if (distance >= m_PinchUpMeters)
                    m_RequireOpen = false;
                return false;
            }
            return m_PinchPressed ? distance < m_PinchUpMeters : distance <= m_PinchDownMeters;
        }

        void Press(PointerEventData pointer, GameObject current)
        {
            pointer.eligibleForClick = true;
            pointer.pressPosition = pointer.position;
            pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
            var pressed = current != null
                ? ExecuteEvents.ExecuteHierarchy(current, pointer, ExecuteEvents.pointerDownHandler)
                : null;
            if (pressed == null && current != null)
                pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(current);
            pointer.pointerPress = pressed;
            pointer.rawPointerPress = current;
            if (pressed != null)
            {
                ++PointerDownCount;
                m_DownObserved = true;
                LogStateChange("DOWN", pressed.name);
            }
        }

        void Release(PointerEventData pointer, GameObject current, bool allowClick)
        {
            var pressed = pointer.pointerPress;
            if (pressed != null)
            {
                ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerUpHandler);
                ++PointerUpCount;
                m_UpObserved = true;
                LogStateChange("UP", pressed.name);
            }
            var clickHandler = current != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(current) : null;
            if (allowClick && pointer.eligibleForClick && pressed == clickHandler && clickHandler != null)
            {
                ExecuteEvents.Execute(clickHandler, pointer, ExecuteEvents.pointerClickHandler);
                ++m_ClickCount;
                m_ClickObserved = true;
                m_ClickFlashUntil = Time.unscaledTime + ClickFlashSeconds;
                LogStateChange("CLICK", clickHandler.name);
            }
            pointer.eligibleForClick = false;
            pointer.pointerPress = null;
            pointer.rawPointerPress = null;
        }

        void CancelPressAndHover(bool trackingLost)
        {
            if (m_Pointer == null)
                return;
            if (m_Pointer.pointerPress != null)
                Release(m_Pointer, null, false);
            if (m_Pointer.pointerEnter != null)
                HandlePointerExitAndEnter(m_Pointer, null);
            m_Pointer.pointerCurrentRaycast = new RaycastResult();
            m_RaycastResults.Clear();
            m_PointerEntered = false;
            m_PinchPressed = false;
            m_RequireOpen = trackingLost || m_RequireOpen;
        }

        void BindRuntimeReferences()
        {
            if (Time.unscaledTime < m_NextBindAttempt && m_Camera != null && m_Ui != null)
                return;
            m_NextBindAttempt = Time.unscaledTime + 1f;
            if (m_Hands == null) m_Hands = FindAnyObjectByType<XRHandPoseProvider>();
            if (m_Ui == null) m_Ui = FindAnyObjectByType<DistributedQuestUi>(FindObjectsInactive.Include);
            if (m_Camera == null && m_Ui != null && m_Ui.WorldCanvas != null)
                m_Camera = m_Ui.WorldCanvas.worldCamera;
            if (m_Camera == null) m_Camera = Camera.main;
        }

        Canvas ResolveCanvas()
        {
            if (m_Ui != null && m_Ui.WorldCanvas != null)
                return m_Ui.WorldCanvas;
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < canvases.Length; ++i)
                if (canvases[i].renderMode == RenderMode.WorldSpace && canvases[i].GetComponent<GraphicRaycaster>() != null)
                    return canvases[i];
            return null;
        }

        static Button ResolveInteractableButton(GameObject hit)
        {
            if (hit == null)
                return null;
            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            var button = handler != null ? handler.GetComponent<Button>() : null;
            return button != null && button.IsActive() && button.IsInteractable() ? button : null;
        }

        void UpdateVisuals(bool tracked, bool indexValid, bool thumbValid, bool screenValid,
            bool overUi, Button button, string target)
        {
            var canvas = ResolveCanvas();
            var pointerPosition = m_Pointer != null ? m_Pointer.position : Vector2.zero;
            SetCursor(overUi, button != null, m_PinchPressed, pointerPosition);
            if (m_Ui == null || m_Ui.HandInputDiagnosticText == null)
                return;
            m_Ui.HandInputDiagnosticText.gameObject.SetActive(m_EnableDiagnostics);
            if (!m_EnableDiagnostics)
                return;
            if (Time.unscaledTime < m_NextDiagnosticUpdate)
                return;
            m_NextDiagnosticUpdate = Time.unscaledTime + 0.05f;
            var front = canvas != null && m_Camera != null && m_Camera.transform.InverseTransformPoint(canvas.transform.position).z > 0f;
            var raycast = m_RaycastResults.Count > 0;
            m_Ui.HandInputDiagnosticText.text =
                $"Pointer: {(button != null ? "BUTTON" : overUi ? "UI" : "NONE")}\n" +
                $"Hand: {(m_UseLeftHand ? "LEFT" : "RIGHT")}\n" +
                $"Pinch: {(m_PinchPressed ? "PRESSED" : "OPEN")}\n" +
                $"Distance: {(tracked && indexValid && thumbValid ? m_PinchDistance * 1000f : 0f):F1} mm\n" +
                $"Target: {(button != null ? button.name : target ?? "NONE")}\n" +
                $"Clicks: {m_ClickCount}\n" +
                $"Stages: HAND={Mark(tracked)} INDEX={Mark(indexValid)} THUMB={Mark(thumbValid)} DIST={Mark(tracked && indexValid && thumbValid)}\n" +
                $"FRONT={Mark(front)} SCREEN={Mark(screenValid)} RAYCAST={Mark(raycast)} ENTER={Mark(m_PointerEntered)}\n" +
                $"DOWN={Mark(m_DownObserved)} UP={Mark(m_UpObserved)} CLICK={Mark(m_ClickObserved)}";
        }

        void SetCursor(bool overUi, bool overButton, bool pinching, Vector2 screenPoint)
        {
            if (m_Ui == null || m_Ui.HandCursor == null || m_Ui.HandCursorImage == null)
                return;
            var visible = m_EnableDiagnostics && overUi;
            m_Ui.HandCursor.gameObject.SetActive(visible);
            if (!visible)
                return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)m_Ui.WorldCanvas.transform, screenPoint, m_Camera, out var local))
                m_Ui.HandCursor.anchoredPosition = local;
            m_Ui.HandCursorImage.color = Time.unscaledTime < m_ClickFlashUntil ? Color.blue :
                pinching ? Color.green : overButton ? Color.yellow : Color.white;
        }

        void LogRaycastStackIfRequested()
        {
            if (!m_LogRaycastStackOnce || m_RaycastStackLogged || m_RaycastResults.Count == 0)
                return;
            m_RaycastStackLogged = true;
            m_LogBuilder.Clear();
            m_LogBuilder.Append("[HandUI] Raycast stack (top first):");
            for (var i = 0; i < m_RaycastResults.Count; ++i)
            {
                var result = m_RaycastResults[i];
                var graphic = result.gameObject != null ? result.gameObject.GetComponent<Graphic>() : null;
                m_LogBuilder.Append("\n  ").Append(i).Append(": ").Append(result.gameObject != null ? result.gameObject.name : "null")
                    .Append(" module=").Append(result.module != null ? result.module.GetType().Name : "null")
                    .Append(" raycastTarget=").Append(graphic != null && graphic.raycastTarget);
            }
            Debug.Log(m_LogBuilder.ToString(), this);
        }

        void LogRuntimeStateOnce()
        {
            if (m_RuntimeStateLogged)
                return;
            m_RuntimeStateLogged = true;
            var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var modules = eventSystem != null ? eventSystem.GetComponents<BaseInputModule>() : new BaseInputModule[0];
            m_LogBuilder.Clear();
            m_LogBuilder.Append("[HandUI] runtime active=").Append(gameObject.activeInHierarchy)
                .Append(" enabled=").Append(enabled).Append(" EventSystems=").Append(systems.Length)
                .Append(" current=").Append(EventSystem.current != null ? EventSystem.current.name : "null")
                .Append(" hand=").Append(m_UseLeftHand ? "LEFT" : "RIGHT")
                .Append(" thresholdsMm=").Append(m_PinchDownMeters * 1000f).Append('/').Append(m_PinchUpMeters * 1000f)
                .Append(" modules=");
            for (var i = 0; i < modules.Length; ++i)
                m_LogBuilder.Append(i == 0 ? string.Empty : ",").Append(modules[i].GetType().Name).Append("(").Append(modules[i].enabled).Append(")");
            Debug.Log(m_LogBuilder.ToString(), this);
        }

        void LogStateChange(string state, string target)
        {
            var value = state + ":" + target;
            if (value == m_LastState)
                return;
            m_LastState = value;
            Debug.Log($"[HandUI] {state} target={target} position={m_Pointer.position} distanceMm={m_PinchDistance * 1000f:F1}", this);
        }

        void ValidateThresholds()
        {
            m_PinchDownMeters = Mathf.Max(0.001f, m_PinchDownMeters);
            m_PinchUpMeters = Mathf.Max(m_PinchDownMeters + 0.001f, m_PinchUpMeters);
        }

        static string Mark(bool value) => value ? "OK" : "--";
    }
}
