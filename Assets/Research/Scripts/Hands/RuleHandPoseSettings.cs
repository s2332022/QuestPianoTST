using System;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    public enum HandCorrectionMethod { Legacy, ContactOnly, JointConstraints, FullRules, Raw }

    [Serializable]
    public sealed class FingerPoseRule
    {
        public Vector3 contactOffsetKeyboardMeters;
        [Min(0)] public float maxDisplacementMeters = .02f;
        // Unsigned segment bend envelopes, NOT calibrated clinical joint angles.
        public Vector3 maximumBendDegrees = new Vector3(90, 110, 90);
        [Range(0, 90)] public float maximumChangeFromRawDegrees = 25;
    }

    [Serializable]
    public sealed class RuleHandPoseSettings
    {
        public HandCorrectionMethod method = HandCorrectionMethod.Legacy;
        public bool contact = true, joints = true, nonpenetration = true, interference = true;
        public FingerPoseRule[] fingers = { new FingerPoseRule { maximumBendDegrees = new Vector3(90,90,90) },
            new FingerPoseRule(), new FingerPoseRule(), new FingerPoseRule(), new FingerPoseRule() };
        [Range(1, 16)] public int iterations = 8;
        [Min(0)] public float wholeHandRmsLimitMeters = .015f;
        [Min(0)] public float permittedPenetrationMeters = .003f;
        [Min(0)] public float segmentRadiusMeters = .003f;
        [Min(0)] public float interferenceRadiusMeters = .005f;
        [Min(0)] public float smoothingSeconds = .025f;
        [Min(0)] public float maximumOffsetSpeedMetersPerSecond = .25f;
        public bool manualNoteMap;
        // -1 = unmapped. A note has at most one manual finger; collisions are rejected.
        public int[] noteToFinger = EmptyMap();
        static int[] EmptyMap() { var a=new int[128]; for(var i=0;i<a.Length;i++)a[i]=-1; return a; }
        public bool UseJoints => method != HandCorrectionMethod.ContactOnly && joints;
        public bool UseEnvironment => method == HandCorrectionMethod.FullRules;
        public bool IsValid
        {
            get
            {
                if(!Enum.IsDefined(typeof(HandCorrectionMethod),method) || iterations<1 || iterations>16 ||
                    !Nonnegative(wholeHandRmsLimitMeters) || !Nonnegative(permittedPenetrationMeters) ||
                    !Nonnegative(segmentRadiusMeters) || !Nonnegative(interferenceRadiusMeters) ||
                    !Nonnegative(smoothingSeconds) || !Nonnegative(maximumOffsetSpeedMetersPerSecond) ||
                    fingers==null || fingers.Length!=5 || noteToFinger==null || noteToFinger.Length!=128) return false;
                foreach(var f in fingers)
                    if(f==null || !DisplayFingerSolver.Finite(f.contactOffsetKeyboardMeters) || !Nonnegative(f.maxDisplacementMeters) ||
                        !DisplayFingerSolver.Finite(f.maximumBendDegrees) || f.maximumBendDegrees.x<0 || f.maximumBendDegrees.x>180 ||
                        f.maximumBendDegrees.y<0 || f.maximumBendDegrees.y>180 || f.maximumBendDegrees.z<0 || f.maximumBendDegrees.z>180 ||
                        !Nonnegative(f.maximumChangeFromRawDegrees) || f.maximumChangeFromRawDegrees>90) return false;
                foreach(var f in noteToFinger) if(f < -1 || f > 4) return false;
                return true;
            }
        }
        static bool Nonnegative(float v)=>float.IsFinite(v)&&v>=0;
    }
}
