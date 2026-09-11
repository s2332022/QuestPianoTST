using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    [DisallowMultipleComponent]
    public sealed class MinimalHandUiInputModule : BaseInputModule
    {
        const float PinchDownMeters = 0.022f;
        const float PinchUpMeters = 0.032f;
        readonly List<RaycastResult> m_RaycastResults = new List<RaycastResult>(16);
        XRHandPoseProvider m_Hands;
        Camera m_Camera;
        PointerEventData m_LeftPointer;
        PointerEventData m_RightPointer;
        bool m_LeftPinching;
        bool m_RightPinching;
        float m_NextBindAttempt;

        protected override void Awake()
        {
            base.Awake();
            m_LeftPointer = new PointerEventData(eventSystem) { pointerId = -101 };
            m_RightPointer = new PointerEventData(eventSystem) { pointerId = -102 };
        }

        public override void Process()
        {
            if (m_Hands == null && Time.unscaledTime >= m_NextBindAttempt)
            {
                m_NextBindAttempt = Time.unscaledTime + 1f;
                m_Hands = FindAnyObjectByType<XRHandPoseProvider>();
            }
            if (m_Camera == null) m_Camera = Camera.main;
            if (m_Hands == null || m_Camera == null)
            {
                ReleaseAndClear(m_LeftPointer, ref m_LeftPinching);
                ReleaseAndClear(m_RightPointer, ref m_RightPinching);
                return;
            }
            ProcessHand(true, m_LeftPointer, ref m_LeftPinching);
            ProcessHand(false, m_RightPointer, ref m_RightPinching);
        }

        protected override void OnDisable()
        {
            ReleaseAndClear(m_LeftPointer, ref m_LeftPinching);
            ReleaseAndClear(m_RightPointer, ref m_RightPinching);
            base.OnDisable();
        }

        void ProcessHand(bool left, PointerEventData pointer, ref bool wasPinching)
        {
            if (!m_Hands.TryGetWorldJoint(left, XRHandJointID.IndexTip, out var index) ||
                !m_Hands.TryGetWorldJoint(left, XRHandJointID.ThumbTip, out var thumb))
            {
                ReleaseAndClear(pointer, ref wasPinching);
                return;
            }

            var screen = m_Camera.WorldToScreenPoint(index.position);
            if (screen.z <= 0f)
            {
                ReleaseAndClear(pointer, ref wasPinching);
                return;
            }
            var previous = pointer.position;
            pointer.position = new Vector2(screen.x, screen.y);
            pointer.delta = pointer.position - previous;
            pointer.scrollDelta = Vector2.zero;

            m_RaycastResults.Clear();
            eventSystem.RaycastAll(pointer, m_RaycastResults);
            pointer.pointerCurrentRaycast = FindFirstRaycast(m_RaycastResults);
            var current = pointer.pointerCurrentRaycast.gameObject;
            HandlePointerExitAndEnter(pointer, current);

            var pinchDistance = Vector3.Distance(index.position, thumb.position);
            var pinching = wasPinching ? pinchDistance < PinchUpMeters : pinchDistance < PinchDownMeters;
            if (pinching && !wasPinching) Press(pointer, current);
            else if (!pinching && wasPinching) Release(pointer, current);
            wasPinching = pinching;
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
        }

        void Release(PointerEventData pointer, GameObject current)
        {
            if (pointer.pointerPress != null)
                ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            var clickHandler = current != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(current) : null;
            if (pointer.eligibleForClick && pointer.pointerPress == clickHandler && clickHandler != null)
                ExecuteEvents.Execute(clickHandler, pointer, ExecuteEvents.pointerClickHandler);
            pointer.eligibleForClick = false;
            pointer.pointerPress = null;
            pointer.rawPointerPress = null;
        }

        void ReleaseAndClear(PointerEventData pointer, ref bool wasPinching)
        {
            if (pointer == null) return;
            if (wasPinching) Release(pointer, null);
            wasPinching = false;
            HandlePointerExitAndEnter(pointer, null);
        }
    }
}
