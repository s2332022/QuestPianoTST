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
        ushort m_SustainChannels;

        public event Action<KeyboardStateChange> StateChanged;
        public bool SustainActive => m_SustainChannels != 0;
        public bool IsPressed(int note) => IsValidNote(note) && m_ChannelMasks[note] != 0;
        public bool IsChannelNotePressed(int channel, int note) =>
            channel >= 1 && channel <= 16 && IsValidNote(note) &&
            (m_ChannelMasks[note] & (1 << (channel - 1))) != 0;
        public bool IsSustainActive(int channel) =>
            channel >= 1 && channel <= 16 && (m_SustainChannels & (1 << (channel - 1))) != 0;
        public int Velocity(int note) => IsValidNote(note) ? m_Velocities[note] : 0;

        public void Reset(double absoluteTime)
        {
            for (var note = 0; note < 128; ++note)
            {
                var channels = m_ChannelMasks[note];
                if (channels == 0) continue;
                m_ChannelMasks[note] = 0;
                m_Velocities[note] = 0;
                StateChanged?.Invoke(new KeyboardStateChange(absoluteTime, note, false, 0, SustainActive));
            }

            if (m_SustainChannels == 0) return;
            m_SustainChannels = 0;
            StateChanged?.Invoke(new KeyboardStateChange(absoluteTime, -1, false, 0, false));
        }

        public void Apply(in MidiMessage message)
        {
            if (message.EventType == MidiEventType.ControlChange)
            {
                if (message.Channel < 1 || message.Channel > 16) return;
                if (message.ControlNumber == 64)
                {
                    var oldSustain = SustainActive;
                    var sustainBit = (ushort)(1 << (message.Channel - 1));
                    if (message.ControlValue >= 64) m_SustainChannels |= sustainBit;
                    else m_SustainChannels &= (ushort)~sustainBit;
                    if (oldSustain != SustainActive)
                        StateChanged?.Invoke(new KeyboardStateChange(
                            message.AbsoluteTimeSeconds, -1, false, 0, SustainActive));
                    return;
                }

                if (message.ControlNumber == 123)
                    ClearChannelNotes(message.Channel, message.AbsoluteTimeSeconds);
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

        public bool ApplySnapshot(byte[] noteBits, byte[] sustainChannels, double absoluteTime,
            out int noteRepairs, out int sustainRepairs)
        {
            noteRepairs = 0;
            sustainRepairs = 0;
            if (noteBits == null || noteBits.Length != 256 || sustainChannels == null ||
                sustainChannels.Length != 16 || double.IsNaN(absoluteTime) || double.IsInfinity(absoluteTime))
                return false;
            for (var i = 0; i < sustainChannels.Length; ++i)
                if (sustainChannels[i] > 1) return false;

            var oldSustainActive = SustainActive;
            var oldSustainChannels = m_SustainChannels;
            ushort nextSustainChannels = 0;
            for (var channelIndex = 0; channelIndex < 16; ++channelIndex)
            {
                var channelBit = (ushort)(1 << channelIndex);
                if (sustainChannels[channelIndex] != 0) nextSustainChannels |= channelBit;
                if (((oldSustainChannels ^ nextSustainChannels) & channelBit) != 0) ++sustainRepairs;
            }
            m_SustainChannels = nextSustainChannels;

            for (var channelIndex = 0; channelIndex < 16; ++channelIndex)
            {
                var channelBit = (ushort)(1 << channelIndex);
                for (var note = 0; note < 128; ++note)
                {
                    var byteIndex = channelIndex * 16 + (note >> 3);
                    var desired = (noteBits[byteIndex] & (1 << (note & 7))) != 0;
                    var oldChannelPressed = (m_ChannelMasks[note] & channelBit) != 0;
                    if (desired == oldChannelPressed) continue;

                    ++noteRepairs;
                    var oldPressed = m_ChannelMasks[note] != 0;
                    if (desired)
                    {
                        m_ChannelMasks[note] |= channelBit;
                        if (!oldPressed) m_Velocities[note] = 100;
                    }
                    else
                    {
                        m_ChannelMasks[note] &= (ushort)~channelBit;
                        if (m_ChannelMasks[note] == 0) m_Velocities[note] = 0;
                    }

                    var newPressed = m_ChannelMasks[note] != 0;
                    if (oldPressed != newPressed)
                        StateChanged?.Invoke(new KeyboardStateChange(absoluteTime, note, newPressed,
                            m_Velocities[note], SustainActive));
                }

            }

            if (oldSustainActive != SustainActive)
                StateChanged?.Invoke(new KeyboardStateChange(absoluteTime, -1, false, 0, SustainActive));
            return true;
        }

        void ClearChannelNotes(int channel, double absoluteTime)
        {
            var bit = (ushort)(1 << (channel - 1));
            for (var note = 0; note < 128; ++note)
            {
                var channels = m_ChannelMasks[note];
                if ((channels & bit) == 0) continue;
                var oldPressed = channels != 0;
                m_ChannelMasks[note] = (ushort)(channels & ~bit);
                var newPressed = m_ChannelMasks[note] != 0;
                if (!newPressed) m_Velocities[note] = 0;
                if (oldPressed != newPressed)
                    StateChanged?.Invoke(new KeyboardStateChange(absoluteTime, note, newPressed,
                        m_Velocities[note], SustainActive));
            }
        }

        static bool IsValidNote(int note) => note >= 0 && note < 128;
    }
}
