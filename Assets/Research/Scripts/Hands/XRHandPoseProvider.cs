using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace QuestPianoMotion.Research
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class XRHandPoseProvider : MonoBehaviour
    {
        const double RetrySeconds = 1d;
        readonly MonotonicSessionClock m_Clock = ResearchServices.Clock;
        XRHandSubsystem m_Subsystem;
        HandPoseFrame m_Raw;
        HandPoseFrame m_Display;
        IHandPoseProcessor m_Processor;
        double m_NextRetry;
        long m_CallbackIndex;
        bool m_CaptureDynamic = true;
        bool m_CaptureBeforeRender;

        public event Action<HandPoseFrame> RawFrameUpdated;
        public event Action<HandPoseFrame> DisplayFrameUpdated;

        public bool IsReady => m_Subsystem != null && m_Subsystem.running;
        public bool LeftTracked => m_Display != null && m_Display.LeftTracked;
        public bool RightTracked => m_Display != null && m_Display.RightTracked;
        public HandPoseFrame LatestDisplayFrame => m_Display;

        void Awake()
        {
            var jointCount = (int)XRHandJointID.EndMarker - (int)XRHandJointID.BeginMarker;
            m_Raw = new HandPoseFrame(jointCount);
            m_Display = new HandPoseFrame(jointCount);
            m_Processor = new IdentityHandPoseProcessor();
        }

        void OnEnable()
        {
            m_NextRetry = 0d;
            TryBind();
        }

        void OnDisable()
        {
            Bind(null);
        }

        void Update()
        {
            if (IsReady)
                return;
            var now = m_Clock.AbsoluteSeconds;
            if (now < m_NextRetry)
                return;
            m_NextRetry = now + RetrySeconds;
            TryBind();
        }

        public void SetProcessor(IHandPoseProcessor processor)
        {
            m_Processor = processor ?? new IdentityHandPoseProcessor();
        }

        public void ConfigureUpdateTypes(bool captureDynamic, bool captureBeforeRender)
        {
            m_CaptureDynamic = captureDynamic;
            m_CaptureBeforeRender = captureBeforeRender;
        }

        public bool TryGetWorldJoint(bool left, XRHandJointID id, out Pose worldPose)
        {
            worldPose = Pose.identity;
            if (m_Display == null)
                return false;
            var sample = m_Display.FindJoint(left, id);
            if (!sample.PoseValid)
                return false;
            var origin = ResearchServices.TrackingOrigin;
            if (origin == null)
            {
                worldPose = sample.Pose;
                return true;
            }
            worldPose.position = origin.TransformPoint(sample.Pose.position);
            worldPose.rotation = origin.rotation * sample.Pose.rotation;
            return true;
        }

        void TryBind()
        {
            var settings = XRGeneralSettings.Instance;
            var manager = settings != null ? settings.Manager : null;
            var loader = manager != null ? manager.activeLoader : null;
            Bind(loader != null ? loader.GetLoadedSubsystem<XRHandSubsystem>() : null);
        }

        void Bind(XRHandSubsystem subsystem)
        {
            if (ReferenceEquals(m_Subsystem, subsystem))
                return;
            if (m_Subsystem != null)
                m_Subsystem.updatedHands -= OnUpdatedHands;
            m_Subsystem = subsystem;
            if (m_Subsystem != null)
                m_Subsystem.updatedHands += OnUpdatedHands;
        }

        void OnUpdatedHands(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags flags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (!subsystem.running ||
                (updateType == XRHandSubsystem.UpdateType.Dynamic && !m_CaptureDynamic) ||
                (updateType == XRHandSubsystem.UpdateType.BeforeRender && !m_CaptureBeforeRender))
                return;
            m_Raw.AbsoluteTimeSeconds = m_Clock.AbsoluteSeconds;
            m_Raw.UnityFrame = Time.frameCount;
            m_Raw.CallbackIndex = ++m_CallbackIndex;
            m_Raw.UpdateType = updateType;
            m_Raw.SuccessFlags = flags;
            CaptureHand(subsystem.leftHand, m_Raw.LeftJoints, out m_Raw.LeftTracked, out m_Raw.LeftRootPose);
            CaptureHand(subsystem.rightHand, m_Raw.RightJoints, out m_Raw.RightTracked, out m_Raw.RightRootPose);
            RawFrameUpdated?.Invoke(m_Raw);
            m_Processor.Process(m_Raw, m_Display);
            DisplayFrameUpdated?.Invoke(m_Display);
        }

        static void CaptureHand(XRHand hand, HandJointPose[] destination, out bool tracked, out Pose rootPose)
        {
            tracked = hand.isTracked;
            rootPose = tracked ? hand.rootPose : Pose.identity;
            var first = (int)XRHandJointID.BeginMarker;
            for (var i = 0; i < destination.Length; ++i)
            {
                var id = (XRHandJointID)(first + i);
                var joint = hand.GetJoint(id);
                var valid = joint.TryGetPose(out var pose);
                destination[i] = new HandJointPose
                {
                    JointId = id,
                    PoseValid = valid,
                    TrackingState = joint.trackingState,
                    Pose = valid ? pose : Pose.identity
                };
            }
        }
    }
}
