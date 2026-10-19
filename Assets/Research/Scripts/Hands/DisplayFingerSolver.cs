using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    public enum CorrectionFinger { Thumb, Index, Middle, Ring, Little }
    public enum StrikingFingerMode { Manual, AutomaticReserved }

    /// <summary>Small FABRIK adapter. Anchors MCP, preserves segment lengths and all other fingers.</summary>
    public sealed class DisplayFingerSolver
    {
        readonly Vector3[] m_Positions = new Vector3[4];
        readonly Vector3[] m_Original = new Vector3[4];
        readonly float[] m_Lengths = new float[3];
        readonly int[] m_Indices = new int[4];
        public static XRHandJointID Tip(CorrectionFinger finger) => finger == CorrectionFinger.Thumb ? XRHandJointID.ThumbTip :
            finger == CorrectionFinger.Index ? XRHandJointID.IndexTip : finger == CorrectionFinger.Middle ? XRHandJointID.MiddleTip :
            finger == CorrectionFinger.Ring ? XRHandJointID.RingTip : XRHandJointID.LittleTip;
        public bool IsValid(HandJointPose[] joints, CorrectionFinger finger)
        {
            var count = finger == CorrectionFinger.Thumb ? 3 : 4;
            var first = (int)Tip(finger) - count + 1;
            for (var i = 0; i < count; ++i)
            {
                m_Indices[i] = -1;
                for (var j = 0; j < joints.Length; ++j)
                    if ((int)joints[j].JointId == first + i && joints[j].PoseValid && Finite(joints[j].Pose.position))
                    { m_Indices[i] = j; break; }
                if (m_Indices[i] < 0) return false;
            }
            return true;
        }
        public bool Solve(HandJointPose[] joints, CorrectionFinger finger, Vector3 target, float maxDisplacement, out bool reachLimited)
        {
            reachLimited = false;
            if (!IsValid(joints, finger) || !Finite(target)) return false;
            var count = finger == CorrectionFinger.Thumb ? 3 : 4; var total = 0f;
            for (var i = 0; i < count; ++i) m_Original[i] = m_Positions[i] = joints[m_Indices[i]].Pose.position;
            for (var i = 0; i < count - 1; ++i)
            {
                m_Lengths[i] = Vector3.Distance(m_Positions[i], m_Positions[i + 1]);
                if (m_Lengths[i] < 0.00001f) return false;
                total += m_Lengths[i];
            }
            var anchor = m_Positions[0];
            if (Vector3.Distance(anchor, target) >= total)
            {
                var direction = (target - anchor).normalized;
                for (var i = 1; i < count; ++i) m_Positions[i] = m_Positions[i - 1] + direction * m_Lengths[i - 1];
            }
            else
            {
                // Seed a bend when the raw chain is straight and the target lies on its axis.
                var axis = (m_Positions[count - 1] - anchor).normalized;
                var bend = Vector3.Cross(axis, Vector3.up);
                if (bend.sqrMagnitude < 0.001f) bend = Vector3.Cross(axis, Vector3.right);
                if (Vector3.Cross(target - anchor, axis).sqrMagnitude < 1e-10f)
                    m_Positions[1] += bend.normalized * 0.0005f;
                for (var iteration = 0; iteration < 40; ++iteration)
                {
                    m_Positions[count - 1] = target;
                    for (var i = count - 2; i >= 0; --i)
                        m_Positions[i] = m_Positions[i + 1] + (m_Positions[i] - m_Positions[i + 1]).normalized * m_Lengths[i];
                    m_Positions[0] = anchor;
                    for (var i = 1; i < count; ++i)
                        m_Positions[i] = m_Positions[i - 1] + (m_Positions[i] - m_Positions[i - 1]).normalized * m_Lengths[i - 1];
                    if (Vector3.Distance(m_Positions[count - 1], target) < 0.00001f) break;
                }
            }
            reachLimited = Vector3.Distance(m_Positions[count - 1], target) > 0.0001f;
            if (Vector3.Distance(m_Positions[count - 1], m_Original[count - 1]) > maxDisplacement + 0.000001f) return false;
            for (var i = 0; i < count; ++i)
            {
                var sample = joints[m_Indices[i]];
                sample.Pose.position = m_Positions[i];
                var segment = Mathf.Min(i, count - 2);
                sample.Pose.rotation = Quaternion.FromToRotation(m_Original[segment + 1] - m_Original[segment],
                    m_Positions[segment + 1] - m_Positions[segment]) * sample.Pose.rotation;
                joints[m_Indices[i]] = sample;
            }
            return true;
        }
        public static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
