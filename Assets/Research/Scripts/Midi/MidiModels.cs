using System;

namespace QuestPianoMotion.Research
{
    public enum MidiEventType
    {
        NoteOn,
        NoteOff,
        ControlChange,
        Other
    }

    [Serializable]
    public struct MidiDeviceDescriptor
    {
        public int DeviceId;
        public string Name;

        public MidiDeviceDescriptor(int deviceId, string name)
        {
            DeviceId = deviceId;
            Name = name ?? string.Empty;
        }
    }

    public readonly struct MidiMessage
    {
        public MidiMessage(double absoluteTimeSeconds, long eventIndex, string deviceName,
            MidiEventType eventType, int channel, int note, int velocity, int control, int value)
        {
            AbsoluteTimeSeconds = absoluteTimeSeconds;
            EventIndex = eventIndex;
            DeviceName = deviceName ?? string.Empty;
            EventType = eventType;
            Channel = channel;
            NoteNumber = note;
            Velocity = velocity;
            ControlNumber = control;
            ControlValue = value;
        }

        public double AbsoluteTimeSeconds { get; }
        public long EventIndex { get; }
        public string DeviceName { get; }
        public MidiEventType EventType { get; }
        public int Channel { get; }
        public int NoteNumber { get; }
        public int Velocity { get; }
        public int ControlNumber { get; }
        public int ControlValue { get; }
    }
}
