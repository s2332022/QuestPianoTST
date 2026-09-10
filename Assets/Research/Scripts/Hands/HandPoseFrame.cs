using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    public struct HandJointPose
    {
        public XRHandJointID JointId;
        public bool PoseValid;
        public XRHandJointTrackingState TrackingState;
        public Pose Pose;
    }

    /// <summary>Mutable, preallocated snapshot. Subscribers must consume it synchronously.</summary>
    public sealed class HandPoseFrame
    {
        public readonly HandJointPose[] LeftJoints;
        public readonly HandJointPose[] RightJoints;
        public double AbsoluteTimeSeconds;
        public int UnityFrame;
        public long CallbackIndex;
        public XRHandSubsystem.UpdateType UpdateType;
        public XRHandSubsystem.UpdateSuccessFlags SuccessFlags;
        public bool LeftTracked;
        public bool RightTracked;
        public Pose LeftRootPose;
        public Pose RightRootPose;

        public HandPoseFrame(int jointCount)
        {
            LeftJoints = new HandJointPose[jointCount];
            RightJoints = new HandJointPose[jointCount];
        }

        public HandJointPose FindJoint(bool left, XRHandJointID id)
        {
            var joints = left ? LeftJoints : RightJoints;
            for (var i = 0; i < joints.Length; ++i)
                if (joints[i].JointId == id)
                    return joints[i];
            return default;
        }
    }

    public interface IHandPoseProcessor
    {
        void Process(HandPoseFrame raw, HandPoseFrame display);
    }

    /// <summary>Current phase processor: copies raw XR data without correction.</summary>
    public sealed class IdentityHandPoseProcessor : IHandPoseProcessor
    {
        public void Process(HandPoseFrame raw, HandPoseFrame display)
        {
            display.AbsoluteTimeSeconds = raw.AbsoluteTimeSeconds;
            display.UnityFrame = raw.UnityFrame;
            display.CallbackIndex = raw.CallbackIndex;
            display.UpdateType = raw.UpdateType;
            display.SuccessFlags = raw.SuccessFlags;
            display.LeftTracked = raw.LeftTracked;
            display.RightTracked = raw.RightTracked;
            display.LeftRootPose = raw.LeftRootPose;
            display.RightRootPose = raw.RightRootPose;
            System.Array.Copy(raw.LeftJoints, display.LeftJoints, raw.LeftJoints.Length);
            System.Array.Copy(raw.RightJoints, display.RightJoints, raw.RightJoints.Length);
        }
    }
}
