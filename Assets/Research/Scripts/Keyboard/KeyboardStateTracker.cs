using System;

namespace QuestPianoMotion.Research
{
    public readonly struct KeyboardStateChange
    {
        public KeyboardStateChange(double absoluteTime, int note, bool pressed, int velocity, bool sustain)
        {
            AbsoluteTimeSeconds = absoluteTime;
            NoteNumber = note;
            Pressed = pressed;
            Velocity = velocity;
            SustainActive = sustain;
        }

        public double AbsoluteTimeSeconds { get; }
        public int NoteNumber { get; }
        public bool Pressed { get; }
        public int Velocity { get; }
        public bool SustainActive { get; }
    }

    /// <summary>Channel-aware physical-key state. Sustain never changes Pressed.</summary>
    public sealed class KeyboardStateTracker
    {
        readonly ushort[] m_ChannelMasks = new ushort[128];
        readonly int[] m_Velocities = new int[128];

        public event Action<KeyboardStateChange> StateChanged;
        public bool SustainActive { get; private set; }
        public bool IsPressed(int note) => IsValidNote(note) && m_ChannelMasks[note] != 0;
        public int Velocity(int note) => IsValidNote(note) ? m_Velocities[note] : 0;

        public void Apply(in MidiMessage message)
        {
            if (message.EventType == MidiEventType.ControlChange && message.ControlNumber == 64)
            {
                var next = message.ControlValue >= 64;
                if (next == SustainActive) return;
                SustainActive = next;
                StateChanged?.Invoke(new KeyboardStateChange(
                    message.AbsoluteTimeSeconds, -1, false, 0, SustainActive));
                return;
            }

            if (!IsValidNote(message.NoteNumber) || message.Channel < 1 || message.Channel > 16)
                return;
            var noteNumber = message.NoteNumber;
            var oldPressed = m_ChannelMasks[noteNumber] != 0;
            var bit = (ushort)(1 << (message.Channel - 1));
            if (message.EventType == MidiEventType.NoteOn && message.Velocity > 0)
            {
                m_ChannelMasks[noteNumber] |= bit; // duplicate Note On on one channel is idempotent
                m_Velocities[noteNumber] = message.Velocity;
            }
            else if (message.EventType == MidiEventType.NoteOff)
            {
                m_ChannelMasks[noteNumber] &= (ushort)~bit;
                if (m_ChannelMasks[noteNumber] == 0) m_Velocities[noteNumber] = 0;
            }
            else return;

            var newPressed = m_ChannelMasks[noteNumber] != 0;
            if (oldPressed != newPressed || newPressed)
                StateChanged?.Invoke(new KeyboardStateChange(message.AbsoluteTimeSeconds, noteNumber, newPressed,
                    m_Velocities[noteNumber], SustainActive));
        }

        static bool IsValidNote(int note) => note >= 0 && note < 128;
    }
}
