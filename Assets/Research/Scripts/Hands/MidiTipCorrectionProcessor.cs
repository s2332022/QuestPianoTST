using System;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research
{
    public enum TipCorrectionState { Inactive, Engaging, Active, Releasing }

    [Serializable]
    public sealed class MidiTipCorrectionSettings
    {
        public bool enabled;
        public bool leftHand;
        public CorrectionFinger finger = CorrectionFinger.Index;
        public StrikingFingerMode selectionMode = StrikingFingerMode.Manual;
        [Range(0, 1)] public float strength = 0.5f;
        [Min(0)] public float maxCorrectionMeters = 0.015f;
        [Min(0)] public float planarToleranceMeters = 0.002f;
        public Vector3 tipToContactKeyboardMeters = Vector3.zero;
        [Min(0)] public float belowSurfaceToleranceMeters = 0.003f;
        [Min(0)] public float aboveSurfaceToleranceMeters = 0.008f;
        [Min(0.001f)] public float engageSeconds = 0.08f;
        [Min(0.001f)] public float releaseSeconds = 0.12f;
        [Min(0.1f)] public float maxHoldSeconds = 3f;
        public bool IsValid => float.IsFinite(strength) && strength >= 0 && strength <= 1 &&
            Nonnegative(maxCorrectionMeters) && Nonnegative(planarToleranceMeters) &&
            Nonnegative(belowSurfaceToleranceMeters) && Nonnegative(aboveSurfaceToleranceMeters) &&
            DisplayFingerSolver.Finite(tipToContactKeyboardMeters) && Nonnegative(engageSeconds) &&
            Nonnegative(releaseSeconds) && float.IsFinite(maxHoldSeconds) && maxHoldSeconds >= .1f &&
            Enum.IsDefined(typeof(CorrectionFinger), finger);
        static bool Nonnegative(float v) => float.IsFinite(v) && v >= 0;
    }

    public struct TipCorrectionSample
    {
        public double Time, ReceivedTime, ObservedTime;
        public long EventIndex, Generation;
        public int Note, Channel;
        public string Source, EventType, Reason;
        public bool Left, Tracked, GeometryValid, CoordinatesValid, Capped, ReachLimited;
        public CorrectionFinger Finger;
        public TipCorrectionState State;
        public float Gain, EffectiveStrength, BeforeDistance, AfterDistance, BeforeNormal, AfterNormal;
        public Vector3 RawKeyboard, CorrectedKeyboard, DeltaKeyboard;
    }

    /// <summary>Consumes real transport observations on Unity thread; only edits the copied display frame.</summary>
    public sealed class MidiTipCorrectionProcessor : IHandPoseProcessor
    {
        readonly IdentityHandPoseProcessor m_Identity = new IdentityHandPoseProcessor();
        readonly DisplayFingerSolver m_Solver = new DisplayFingerSolver();
        readonly bool[,,] m_Held = new bool[2, 16, 128];
        readonly VirtualPianoKeyboard m_Keyboard;
        readonly Func<bool> m_Calibrated;
        readonly IHandPoseProcessor m_Fallback;
        readonly Func<bool> m_RemoteSelected;
        readonly Func<Transform> m_Origin;
        int m_HeldCount, m_Source = -1, m_Note = -1, m_Channel;
        bool m_Ambiguous, m_LastLeft;
        CorrectionFinger m_LastFinger;
        StrikingFingerMode m_LastMode;
        double m_Onset, m_PhaseStart, m_LastPoseTime = double.NegativeInfinity;
        float m_ReleaseGain;
        Vector3 m_LastDeltaTracking;
        TipCorrectionSample m_Event;
        string m_Reason = "no_note";
        PianoKeySurface m_Surface;
        Matrix4x4 m_GeometryMatrix;
        int m_KeyCount;
        public MidiTipCorrectionSettings Settings { get; }
        public TipCorrectionState State { get; private set; }
        public float Gain { get; private set; }
        public TipCorrectionSample Latest { get; private set; }
        public event Action<HandPoseFrame, TipCorrectionSample> Evaluated;

        public MidiTipCorrectionProcessor(MidiTipCorrectionSettings settings, VirtualPianoKeyboard keyboard,
            Func<bool> calibrated, IHandPoseProcessor fallback = null, Func<bool> remoteSelected = null,
            Func<Transform> origin = null)
        {
            Settings = settings; m_Keyboard = keyboard; m_Calibrated = calibrated;
            m_Fallback = fallback ?? m_Identity; m_RemoteSelected = remoteSelected;
            m_Origin = origin ?? (() => ResearchServices.TrackingOrigin);
            SaveSelection();
        }
        void SaveSelection() { m_LastLeft = Settings.leftHand; m_LastFinger = Settings.finger; m_LastMode = Settings.selectionMode; }
        public void ObserveMidi(string source, MidiMessage message, double received, double observed, long generation = 0, bool synthetic = false)
        {
            var s = source == "BLE" ? 0 : source == "UDP" ? 1 : -1;
            if (s < 0 || !double.IsFinite(received) || !double.IsFinite(observed)) return;
            if (synthetic) { DisconnectSource(source, observed, "midi_disconnect"); return; }
            var channel = message.Channel - 1;
            if (channel < 0 || channel > 15) return;
            var type = message.EventType == MidiEventType.NoteOn && message.Velocity == 0 ? MidiEventType.NoteOff : message.EventType;
            if (type == MidiEventType.ControlChange && (message.ControlNumber == 120 || message.ControlNumber == 123))
            {
                for (var n = 0; n < 128; ++n)
                    if (m_Held[s, channel, n]) { m_Held[s, channel, n] = false; --m_HeldCount; }
                if (m_Source == s && m_Channel == channel) Release(observed, "all_notes_off");
                if (m_HeldCount == 0) m_Ambiguous = false;
                return;
            }
            if (type != MidiEventType.NoteOn && type != MidiEventType.NoteOff) return;
            var note = message.NoteNumber;
            if (note < 0 || note > 127) return;
            var wasHeld = m_Held[s, channel, note];
            var held = type == MidiEventType.NoteOn;
            if (held != wasHeld) { m_Held[s, channel, note] = held; m_HeldCount += held ? 1 : -1; }
            var observation = new TipCorrectionSample { Source = source, EventIndex = message.EventIndex,
                Generation = generation, Note = note, Channel = message.Channel, EventType = type.ToString(),
                ReceivedTime = received, ObservedTime = observed };
            if (held)
            {
                if (wasHeld || m_HeldCount != 1 || m_Ambiguous)
                { m_Ambiguous = true; HardReset("ambiguous_notes"); return; }
                // An onset replaces any release tail. No attribution to earlier poses is made.
                HardReset("note_on"); m_Source = s; m_Channel = channel; m_Note = note;
                m_Event = observation; m_Onset = m_PhaseStart = observed; m_Surface = null;
                if (note < 60 || note > 72) { m_Reason = "outside_experiment_range"; return; }
                SaveSelection(); State = TipCorrectionState.Engaging;
            }
            else
            {
                if (s == m_Source && note == m_Note && channel == m_Channel)
                { m_Event = observation; Release(observed, "note_off"); }
                if (m_HeldCount == 0) m_Ambiguous = false;
            }
        }
        public void DisconnectSource(string source, double now, string reason = "midi_disconnect")
        {
            var s = source == "BLE" ? 0 : 1;
            for (var c = 0; c < 16; ++c) for (var n = 0; n < 128; ++n)
                if (m_Held[s, c, n]) { m_Held[s, c, n] = false; --m_HeldCount; }
            if (s == m_Source) Release(now, reason);
            if (m_HeldCount == 0) m_Ambiguous = false;
        }
        public void SynchronizeUdpOwnership(byte[] noteBits, double now)
        {
            if (noteBits == null || noteBits.Length != 256) return;
            DisconnectSource("UDP", now, "udp_snapshot_repair");
            for (var c = 0; c < 16; ++c) for (var n = 0; n < 128; ++n)
                if ((noteBits[c * 16 + (n >> 3)] & (1 << (n & 7))) != 0)
                { m_Held[1, c, n] = true; ++m_HeldCount; }
            // Imported ownership has no newly observed onset or known finger. Require all keys up.
            if (m_HeldCount > 0) { m_Ambiguous = true; HardReset("udp_snapshot_repair"); }
        }
        public void EndSession(double now)
        { m_Ambiguous = m_HeldCount > 0; Release(now, "session_end"); }
        public void HardReset(string reason)
        { State = TipCorrectionState.Inactive; Gain = 0; m_LastDeltaTracking = Vector3.zero; m_Reason = reason; }
        void Release(double now, string reason)
        {
            if (State == TipCorrectionState.Releasing || State == TipCorrectionState.Inactive) return;
            Tick(now); m_ReleaseGain = Gain; m_PhaseStart = now;
            State = TipCorrectionState.Releasing; m_Reason = reason;
        }
        static float Smooth(double elapsed, float duration)
        { var t = duration <= 0 ? 1f : Mathf.Clamp01((float)(elapsed / duration)); return t * t * (3 - 2 * t); }
        void Tick(double now)
        {
            if (State == TipCorrectionState.Engaging)
            { Gain = Smooth(now - m_PhaseStart, Settings.engageSeconds); if (Gain >= 1) State = TipCorrectionState.Active; }
            if (State == TipCorrectionState.Releasing)
            {
                Gain = m_ReleaseGain * (1 - Smooth(now - m_PhaseStart, Settings.releaseSeconds));
                if (Gain <= 0) HardReset(m_Reason);
            }
        }
        public void Watchdog(double now)
        {
            if (State != TipCorrectionState.Inactive && now - m_LastPoseTime > .25 && double.IsFinite(m_LastPoseTime))
                HardReset("pose_timeout");
            if ((State == TipCorrectionState.Active || State == TipCorrectionState.Engaging) && now - m_Onset > Settings.maxHoldSeconds)
            { EndSession(now); m_Reason = "note_timeout"; }
        }
        public void Process(HandPoseFrame raw, HandPoseFrame display)
        {
            // Preserve the pre-existing PC display mode. Correction never combines frames/clocks from two devices.
            if (m_RemoteSelected != null && m_RemoteSelected())
            {
                HardReset("remote_display_selected"); m_Fallback.Process(raw, display);
                var skipped = m_Event; skipped.Time = raw.AbsoluteTimeSeconds; skipped.Note = m_Note;
                skipped.Left = Settings.leftHand; skipped.Finger = Settings.finger;
                skipped.Reason = "remote_display_selected"; skipped.State = State;
                skipped.Tracked = Settings.leftHand ? raw.LeftTracked : raw.RightTracked;
                Latest = skipped; Evaluated?.Invoke(raw, skipped); return;
            }
            m_Identity.Process(raw, display);
            var now = raw.AbsoluteTimeSeconds;
            var result = m_Event; result.Time = now; result.Note = m_Note;
            result.Left = Settings.leftHand; result.Finger = Settings.finger;
            result.Tracked = result.Left ? raw.LeftTracked : raw.RightTracked;
            var tipId = DisplayFingerSolver.Tip(Settings.finger);
            var tip = raw.FindJoint(result.Left, tipId);
            var joints = result.Left ? display.LeftJoints : display.RightJoints;
            var origin = m_Origin();
            var transformsValid = origin != null && m_Keyboard != null && m_Keyboard.KeyboardRoot != null &&
                Rigid(origin.localToWorldMatrix) && Rigid(m_Keyboard.KeyboardRoot.localToWorldMatrix);
            var sampleValid = result.Tracked && tip.PoseValid && DisplayFingerSolver.Finite(tip.Pose.position);
            var valid = sampleValid && transformsValid;
            if (valid)
            {
                result.RawKeyboard = m_Keyboard.KeyboardRoot.InverseTransformPoint(origin.TransformPoint(tip.Pose.position));
                result.CorrectedKeyboard = result.RawKeyboard; result.CoordinatesValid = true;
            }
            if (transformsValid && m_Note >= 0)
            {
                var matrix = m_Keyboard.KeyboardRoot.worldToLocalMatrix * m_Keyboard.KeyboardGeometry.localToWorldMatrix;
                if (m_Surface == null || matrix != m_GeometryMatrix || m_KeyCount != m_Keyboard.KeyCount)
                {
                    m_Keyboard.TryGetKeySurface(m_Note, out m_Surface); m_GeometryMatrix = matrix; m_KeyCount = m_Keyboard.KeyCount;
                }
                result.GeometryValid = m_Surface != null;
            }
            string blocked = null;
            if (!Settings.IsValid) blocked = "invalid_settings";
            else if (!Settings.enabled) blocked = "disabled";
            else if (Settings.selectionMode != StrikingFingerMode.Manual) blocked = "automatic_unavailable";
            else if (Settings.leftHand != m_LastLeft || Settings.finger != m_LastFinger || Settings.selectionMode != m_LastMode)
            { SaveSelection(); blocked = "selection_changed"; }
            else if (!sampleValid || !m_Solver.IsValid(joints, Settings.finger)) blocked = "tracking_invalid";
            else if (m_Calibrated == null || !m_Calibrated()) blocked = "calibration_invalid";
            else if (!transformsValid) blocked = "transform_invalid_or_scaled";
            else if (m_Note >= 0 && !result.GeometryValid) blocked = "key_unavailable";
            else if (now < m_Event.ObservedTime || now < m_Event.ReceivedTime) blocked = "before_midi_observation";
            else if (now < m_LastPoseTime) blocked = "nonmonotonic_pose";
            if (blocked != null) HardReset(blocked);
            else { Watchdog(now); Tick(now); }
            m_LastPoseTime = now;
            if (valid && result.GeometryValid && blocked == null && State != TipCorrectionState.Inactive)
            {
                var desiredKeyboard = m_Surface.Target(result.RawKeyboard, Settings.tipToContactKeyboardMeters,
                    Settings.planarToleranceMeters, Settings.belowSurfaceToleranceMeters, Settings.aboveSurfaceToleranceMeters);
                var deltaKeyboard = (desiredKeyboard - result.RawKeyboard) * Settings.strength;
                var deltaTracking = origin.InverseTransformVector(m_Keyboard.KeyboardRoot.TransformVector(deltaKeyboard));
                result.Capped = deltaTracking.magnitude > Settings.maxCorrectionMeters;
                deltaTracking = Vector3.ClampMagnitude(deltaTracking, Settings.maxCorrectionMeters) * Gain;
                if (deltaTracking.sqrMagnitude > 1e-14f)
                {
                    if (!m_Solver.Solve(joints, Settings.finger, tip.Pose.position + deltaTracking,
                        Settings.maxCorrectionMeters, out result.ReachLimited))
                    { if (State != TipCorrectionState.Releasing) m_Reason = "kinematic_limit"; }
                    else if (State != TipCorrectionState.Releasing) m_Reason = result.ReachLimited ? "reach_limited" : "applied";
                }
                else if (State != TipCorrectionState.Releasing)
                    m_Reason = Gain == 0 ? "engaging" : Settings.strength == 0 ? "strength_zero" :
                        Settings.maxCorrectionMeters == 0 ? "correction_limit_zero" : "within_tolerance";
                var corrected = display.FindJoint(result.Left, tipId).Pose.position;
                m_LastDeltaTracking = corrected - tip.Pose.position;
                result.CorrectedKeyboard = m_Keyboard.KeyboardRoot.InverseTransformPoint(origin.TransformPoint(corrected));
                result.DeltaKeyboard = result.CorrectedKeyboard - result.RawKeyboard;
            }
            if (valid && result.GeometryValid)
            {
                var before = result.RawKeyboard + Settings.tipToContactKeyboardMeters;
                var after = result.CorrectedKeyboard + Settings.tipToContactKeyboardMeters;
                result.BeforeDistance = m_Surface.PlanarDistance(before); result.AfterDistance = m_Surface.PlanarDistance(after);
                result.BeforeNormal = before.y - m_Surface.Height; result.AfterNormal = after.y - m_Surface.Height;
            }
            result.State = State; result.Gain = Gain; result.EffectiveStrength = Settings.strength * Gain;
            result.Reason = m_Reason; Latest = result;
            Evaluated?.Invoke(raw, result);
        }
        public static bool Rigid(Matrix4x4 m)
        {
            var x = (Vector3)m.GetColumn(0); var y = (Vector3)m.GetColumn(1); var z = (Vector3)m.GetColumn(2);
            return DisplayFingerSolver.Finite(m.GetColumn(3)) && Mathf.Abs(x.sqrMagnitude - 1) < .0001f &&
                Mathf.Abs(y.sqrMagnitude - 1) < .0001f && Mathf.Abs(z.sqrMagnitude - 1) < .0001f &&
                Mathf.Abs(Vector3.Dot(x,y)) < .0001f && Mathf.Abs(Vector3.Dot(x,z)) < .0001f &&
                Mathf.Abs(Vector3.Dot(y,z)) < .0001f && m.determinant > 0;
        }
    }
}
