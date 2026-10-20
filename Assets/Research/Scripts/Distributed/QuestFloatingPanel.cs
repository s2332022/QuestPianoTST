using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;
namespace QuestPianoMotion.Research.Distributed
{
    /// <summary>World placement only. Never writes tracking, keyboard or research transforms.</summary>
    [DisallowMultipleComponent]
    public sealed class QuestFloatingPanel : MonoBehaviour
    {
        [SerializeField, Range(.25f, 1.5f)] float m_MinimumDistance = .45f;
        [SerializeField, Range(1.5f, 5f)] float m_MaximumDistance = 2.5f;
        [SerializeField, Range(0f, 30f)] float m_Smoothing = 18f;
        DistributedQuestUi m_Ui;
        Transform m_Head;
        Transform m_Reference;
        string m_CalibrationId;
        Vector3 m_StartPanel, m_StartRayPoint, m_Target;
        float m_RayDistance;
        int m_Pointer;
        bool m_Moving;
        public bool Moving => m_Moving;
        public float MinimumDistance => m_MinimumDistance;
        public float MaximumDistance => m_MaximumDistance;
        public string PersistenceStatus { get; private set; } = "Placement not saved";
        public string SavePath => Path.Combine(Application.persistentDataPath, "QuestUi", "placement-v1.json");
        public void Initialize(DistributedQuestUi ui) => m_Ui = ui;
        public void SetReference(Transform calibratedKeyboard, string calibrationId = null) { m_Reference = calibratedKeyboard; m_CalibrationId = calibrationId; }
        public void SetCamera(Camera camera) { m_Head = camera != null ? camera.transform : null; BindOriginEvents(); }
        public bool Restore()
        {
            if (!QuestUiPlacementStore.TryLoad(SavePath, out var data) ||
                !QuestUiPlacementStore.TryResolve(data, m_Reference, m_Head, m_MinimumDistance, m_MaximumDistance, out var pose, m_CalibrationId))
                return false;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            PersistenceStatus = "Restored: KeyboardLocal v1"; return true;
        }
        public void Save()
        {
            if (m_Reference == null) { PersistenceStatus = "Uncalibrated: safe front placement on restart"; return; }
            try { QuestUiPlacementStore.Save(SavePath, transform, m_Reference, m_CalibrationId); PersistenceStatus = "Saved: KeyboardLocal v1"; }
            catch (Exception e) { PersistenceStatus = "Placement save error: " + e.GetType().Name; PlaceInFront(false); }
        }
        public void PlaceInFront(bool save = true)
        {
            StopMoving(false);
            if (m_Head == null) return;
            var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            forward.Normalize();
            // Canvas text faces local -Z; +Z must point from the viewer towards the panel.
            var right = Vector3.Cross(Vector3.up, forward);
            transform.SetPositionAndRotation(m_Head.position + forward * 1.05f + right * .15f + Vector3.up * .05f,
                Quaternion.LookRotation(forward, Vector3.up));
            if (save) Save();
        }
        public void FaceUser()
        {
            if (m_Head == null) return;
            var away = Vector3.ProjectOnPlane(transform.position - m_Head.position, Vector3.up);
            if (away.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            Save();
        }
        public void ResetPlacement()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { PersistenceStatus = "Placement reset error: " + e.GetType().Name; }
            PlaceInFront(false);
        }
        public void AdjustDistance(float change)
        {
            if (m_Head == null || m_Moving) return;
            var delta = transform.position - m_Head.position;
            if (delta.sqrMagnitude < .0001f) return;
            transform.position = m_Head.position + delta.normalized * Mathf.Clamp(delta.magnitude + change, m_MinimumDistance, m_MaximumDistance);
            Save();
        }
        public void AdjustLimits(float change)
        {
            m_MinimumDistance = Mathf.Clamp(m_MinimumDistance + change, .25f, 1.25f);
            m_MaximumDistance = Mathf.Clamp(m_MaximumDistance + change, 1.5f, 5f);
        }
        public bool BeginMove(Ray ray, Vector3 hit, int pointerId)
        {
            if (m_Moving || m_Ui == null || !m_Ui.Safety.CanUseControls || m_Ui.IpKeyboardVisible || m_Head == null) return false;
            if (!QuestSpatialPlacement.IsFinite(ray.origin) || !QuestSpatialPlacement.IsFinite(hit)) return false;
            m_StartPanel = transform.position;
            m_RayDistance = Vector3.Dot(hit - ray.origin, ray.direction);
            if (m_RayDistance <= 0) return false;
            m_StartRayPoint = ray.GetPoint(m_RayDistance);
            m_Target = m_StartPanel;
            m_Pointer = pointerId; m_Moving = true;
            return true;
        }
        public void Move(Ray ray, int pointerId)
        {
            if (!m_Moving || pointerId != m_Pointer) return;
            if (!m_Ui.Safety.CanUseControls) { StopMoving(); return; }
            var target = m_StartPanel + ray.GetPoint(m_RayDistance) - m_StartRayPoint;
            if (!QuestSpatialPlacement.IsFinite(target)) { StopMoving(); return; }
            var delta = target - m_Head.position;
            if (delta.sqrMagnitude < .0001f) return;
            m_Target = m_Head.position + delta.normalized * Mathf.Clamp(delta.magnitude, m_MinimumDistance, m_MaximumDistance);
            if (m_Smoothing <= 0) transform.position = m_Target;
        }
        public void EndMove(int pointerId) { if (m_Moving && pointerId == m_Pointer) StopMoving(); }
        public void StopMoving(bool save = true)
        {
            if (!m_Moving) return;
            m_Moving = false;
            EventSystem.current?.SetSelectedGameObject(null);
            if (save) Save();
        }
        void Update()
        {
            if (!m_Moving) return;
            if (m_Head == null || !m_Ui.Safety.CanUseControls) { StopMoving(); return; }
            transform.position = Vector3.Lerp(transform.position, m_Target, 1f - Mathf.Exp(-m_Smoothing * Time.unscaledDeltaTime));
        }
        void OnDisable() => StopMoving();
        void OnApplicationPause(bool paused) { if (paused) StopMoving(); }
        // Quest system recenter changes the tracking space: use a visible safe placement.
        readonly System.Collections.Generic.List<XRInputSubsystem> m_Inputs = new System.Collections.Generic.List<XRInputSubsystem>();
        void OnEnable() => BindOriginEvents();
        void BindOriginEvents()
        {
            // XR subsystems may not exist yet when this runtime component is added.
            var available = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(available);
            foreach (var input in available)
                if (!m_Inputs.Contains(input))
                {
                    m_Inputs.Add(input);
                    input.trackingOriginUpdated += OriginUpdated;
                }
        }
        void OriginUpdated(XRInputSubsystem input) => PlaceInFront(false);
        void OnDestroy() { foreach (var input in m_Inputs) input.trackingOriginUpdated -= OriginUpdated; }
    }
    /// <summary>Only the handle receives drag events; buttons, scroll views and sliders remain standard XRI UI.</summary>
    public sealed class QuestPanelMoveHandle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public QuestFloatingPanel Panel { get; set; }
        bool m_Hover;
        PointerEventData m_SelectedEvent;
        static bool TryRay(PointerEventData e, out Ray ray)
        {
            ray = default;
            if (!(e is TrackedDeviceEventData tracked) || tracked.rayPoints == null || tracked.rayPoints.Count < 2) return false;
            var direction = tracked.rayPoints[1] - tracked.rayPoints[0];
            if (direction.sqrMagnitude < .000001f) return false;
            ray = new Ray(tracked.rayPoints[0], direction.normalized); return true;
        }
        public void OnPointerDown(PointerEventData e)
        {
            if (TryRay(e, out var ray) && Panel.BeginMove(ray, e.pointerCurrentRaycast.worldPosition, e.pointerId)) m_SelectedEvent = e;
            Paint();
        }
        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
        public void OnBeginDrag(PointerEventData e) { e.eligibleForClick = false; }
        public void OnDrag(PointerEventData e) { if (TryRay(e, out var ray)) Panel.Move(ray, e.pointerId); }
        public void OnPointerUp(PointerEventData e) { Panel.EndMove(e.pointerId); if (m_SelectedEvent != null && m_SelectedEvent.pointerId == e.pointerId) m_SelectedEvent = null; Paint(); }
        public void OnEndDrag(PointerEventData e) { Panel.EndMove(e.pointerId); if (m_SelectedEvent != null && m_SelectedEvent.pointerId == e.pointerId) m_SelectedEvent = null; Paint(); }
        public void OnPointerEnter(PointerEventData e) { m_Hover = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { m_Hover = false; Paint(); }
        void LateUpdate()
        {
            if (m_SelectedEvent == null || !Panel.Moving) return;
            // XRI 3.5 skips OnDrag when the ray hits no graphic (screen delta becomes zero).
            // Read the same XRI-owned event model while its pointer press is held.
            if (TryRay(m_SelectedEvent, out var ray)) Panel.Move(ray, m_SelectedEvent.pointerId);
            else { Panel.StopMoving(); m_SelectedEvent = null; }
            Paint();
        }
        void Paint() { GetComponent<Image>().color = Panel.Moving ? new Color(.2f,.7f,1f) : m_Hover ? new Color(.3f,.5f,.7f) : new Color(.18f,.25f,.34f); }
        void OnDisable() { m_SelectedEvent = null; if (Panel != null) Panel.StopMoving(); }
    }
}
