using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Tests
{
    public class HandTrackingDataTests
    {
        [Test]
        public void SampleIntervalGate_ThrottlesUsingMonotonicTime()
        {
            var gate = new SampleIntervalGate(0.5d);

            Assert.That(gate.ShouldSample(10d), Is.True);
            Assert.That(gate.ShouldSample(10.49d), Is.False);
            Assert.That(gate.ShouldSample(10.5d), Is.True);

            gate.Reset();
            Assert.That(gate.ShouldSample(10.51d), Is.True);
        }

        [Test]
        public void HandSideSample_PreservesRequiredJointMappingAndValidity()
        {
            var invalidWrist = new HandJointSample(
                XRHandJointID.Wrist,
                false,
                XRHandJointTrackingState.None,
                Pose.identity);
            var validPalm = new HandJointSample(
                XRHandJointID.Palm,
                true,
                XRHandJointTrackingState.Pose,
                new Pose(Vector3.one, Quaternion.identity));

            var hand = new HandSideSample(
                Handedness.Left,
                true,
                invalidWrist,
                validPalm,
                Joint(XRHandJointID.ThumbTip),
                Joint(XRHandJointID.IndexTip),
                Joint(XRHandJointID.MiddleTip),
                Joint(XRHandJointID.RingTip),
                Joint(XRHandJointID.LittleTip));

            Assert.That(hand.IsTracked, Is.True);
            Assert.That(hand.Wrist.JointId, Is.EqualTo(XRHandJointID.Wrist));
            Assert.That(hand.Wrist.PoseValid, Is.False);
            Assert.That(hand.Palm.JointId, Is.EqualTo(XRHandJointID.Palm));
            Assert.That(hand.Palm.PoseValid, Is.True);
            Assert.That(hand.ThumbTip.JointId, Is.EqualTo(XRHandJointID.ThumbTip));
            Assert.That(hand.IndexTip.JointId, Is.EqualTo(XRHandJointID.IndexTip));
            Assert.That(hand.MiddleTip.JointId, Is.EqualTo(XRHandJointID.MiddleTip));
            Assert.That(hand.RingTip.JointId, Is.EqualTo(XRHandJointID.RingTip));
            Assert.That(hand.LittleTip.JointId, Is.EqualTo(XRHandJointID.LittleTip));
        }

        static HandJointSample Joint(XRHandJointID jointId)
        {
            return new HandJointSample(jointId, true, XRHandJointTrackingState.Pose, Pose.identity);
        }
    }
}
