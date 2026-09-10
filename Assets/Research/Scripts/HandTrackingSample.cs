using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    /// <summary>One joint observation captured from XR Hands.</summary>
    public readonly struct HandJointSample
    {
        /// <summary>Creates an immutable joint observation.</summary>
        public HandJointSample(
            XRHandJointID jointId,
            bool poseValid,
            XRHandJointTrackingState trackingState,
            Pose pose)
        {
            JointId = jointId;
            PoseValid = poseValid;
            TrackingState = trackingState;
            Pose = pose;
        }

        /// <summary>Gets the XR Hands joint identifier.</summary>
        public XRHandJointID JointId { get; }

        /// <summary>Gets whether <see cref="XRHandJoint.TryGetPose"/> succeeded.</summary>
        public bool PoseValid { get; }

        /// <summary>Gets the joint tracking flags reported by XR Hands.</summary>
        public XRHandJointTrackingState TrackingState { get; }

        /// <summary>Gets the tracking-space pose, or <see cref="Pose.identity"/> when unavailable.</summary>
        public Pose Pose { get; }
    }

    /// <summary>Tracking state and selected joint observations for one hand.</summary>
    public readonly struct HandSideSample
    {
        /// <summary>Creates an immutable hand observation.</summary>
        public HandSideSample(
            Handedness handedness,
            bool isTracked,
            HandJointSample wrist,
            HandJointSample palm,
            HandJointSample thumbTip,
            HandJointSample indexTip,
            HandJointSample middleTip,
            HandJointSample ringTip,
            HandJointSample littleTip)
        {
            Handedness = handedness;
            IsTracked = isTracked;
            Wrist = wrist;
            Palm = palm;
            ThumbTip = thumbTip;
            IndexTip = indexTip;
            MiddleTip = middleTip;
            RingTip = ringTip;
            LittleTip = littleTip;
        }

        /// <summary>Gets the side represented by this observation.</summary>
        public Handedness Handedness { get; }

        /// <summary>Gets the value of <see cref="XRHand.isTracked"/> at capture time.</summary>
        public bool IsTracked { get; }

        /// <summary>Gets the wrist observation.</summary>
        public HandJointSample Wrist { get; }

        /// <summary>Gets the palm observation.</summary>
        public HandJointSample Palm { get; }

        /// <summary>Gets the thumb-tip observation.</summary>
        public HandJointSample ThumbTip { get; }

        /// <summary>Gets the index-tip observation.</summary>
        public HandJointSample IndexTip { get; }

        /// <summary>Gets the middle-tip observation.</summary>
        public HandJointSample MiddleTip { get; }

        /// <summary>Gets the ring-tip observation.</summary>
        public HandJointSample RingTip { get; }

        /// <summary>Gets the little-finger-tip observation.</summary>
        public HandJointSample LittleTip { get; }
    }

    /// <summary>A timestamped, allocation-free value snapshot of both hands.</summary>
    public readonly struct HandTrackingSample
    {
        /// <summary>Creates an immutable two-hand sample.</summary>
        public HandTrackingSample(
            double monotonicTimeSeconds,
            int frameCount,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            HandSideSample left,
            HandSideSample right)
        {
            MonotonicTimeSeconds = monotonicTimeSeconds;
            FrameCount = frameCount;
            UpdateSuccessFlags = updateSuccessFlags;
            Left = left;
            Right = right;
        }

        /// <summary>Gets monotonic seconds since the Unity player started.</summary>
        public double MonotonicTimeSeconds { get; }

        /// <summary>Gets the Unity frame number at capture time.</summary>
        public int FrameCount { get; }

        /// <summary>Gets the subsystem update result associated with this sample.</summary>
        public XRHandSubsystem.UpdateSuccessFlags UpdateSuccessFlags { get; }

        /// <summary>Gets the left-hand observation.</summary>
        public HandSideSample Left { get; }

        /// <summary>Gets the right-hand observation.</summary>
        public HandSideSample Right { get; }
    }
}
