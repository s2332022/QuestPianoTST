using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    /// <summary>Copies selected XR Hands data into recorder-friendly value types.</summary>
    public static class XRHandSampleReader
    {
        /// <summary>Captures both hands from a running subsystem without retaining native-backed hand data.</summary>
        public static HandTrackingSample Capture(
            XRHandSubsystem subsystem,
            double monotonicTimeSeconds,
            int frameCount,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags)
        {
            if (subsystem == null || !subsystem.running)
            {
                return new HandTrackingSample(
                    monotonicTimeSeconds,
                    frameCount,
                    XRHandSubsystem.UpdateSuccessFlags.None,
                    CreateUnavailableHand(Handedness.Left),
                    CreateUnavailableHand(Handedness.Right));
            }

            return new HandTrackingSample(
                monotonicTimeSeconds,
                frameCount,
                updateSuccessFlags,
                CaptureHand(subsystem.leftHand),
                CaptureHand(subsystem.rightHand));
        }

        static HandSideSample CaptureHand(XRHand hand)
        {
            return new HandSideSample(
                hand.handedness,
                hand.isTracked,
                CaptureJoint(hand, XRHandJointID.Wrist),
                CaptureJoint(hand, XRHandJointID.Palm),
                CaptureJoint(hand, XRHandJointID.ThumbTip),
                CaptureJoint(hand, XRHandJointID.IndexTip),
                CaptureJoint(hand, XRHandJointID.MiddleTip),
                CaptureJoint(hand, XRHandJointID.RingTip),
                CaptureJoint(hand, XRHandJointID.LittleTip));
        }

        static HandJointSample CaptureJoint(XRHand hand, XRHandJointID jointId)
        {
            var joint = hand.GetJoint(jointId);
            var poseValid = joint.TryGetPose(out var pose);
            return new HandJointSample(jointId, poseValid, joint.trackingState, pose);
        }

        static HandSideSample CreateUnavailableHand(Handedness handedness)
        {
            return new HandSideSample(
                handedness,
                false,
                CreateUnavailableJoint(XRHandJointID.Wrist),
                CreateUnavailableJoint(XRHandJointID.Palm),
                CreateUnavailableJoint(XRHandJointID.ThumbTip),
                CreateUnavailableJoint(XRHandJointID.IndexTip),
                CreateUnavailableJoint(XRHandJointID.MiddleTip),
                CreateUnavailableJoint(XRHandJointID.RingTip),
                CreateUnavailableJoint(XRHandJointID.LittleTip));
        }

        static HandJointSample CreateUnavailableJoint(XRHandJointID jointId)
        {
            return new HandJointSample(jointId, false, XRHandJointTrackingState.None, Pose.identity);
        }
    }
}
