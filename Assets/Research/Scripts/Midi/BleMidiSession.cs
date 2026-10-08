using System;
using QuestPianoMotion.Research.Distributed;

namespace QuestPianoMotion.Research
{
    /// <summary>Main-thread consumer. Android timestamps use System.nanoTime, never PC time.</summary>
    public sealed class BleMidiSession
    {
        public readonly struct Sample
        {
            public Sample(long generation, long timestamp, long received, int status, int first, int second, int bleTimestampMilliseconds = -1)
            { Generation = generation; TimestampNanos = timestamp; ReceivedNanos = received; Status = status; First = first; Second = second; BleTimestampMilliseconds = bleTimestampMilliseconds; }
            public readonly long Generation, TimestampNanos, ReceivedNanos;
            public readonly int Status, First, Second, BleTimestampMilliseconds;
        }
        readonly RejectingBoundedQueue<Sample> m_Queue;
        readonly bool[,] m_Notes = new bool[16, 128];
        readonly bool[] m_Sustain = new bool[16];
        readonly MonotonicSessionClock m_Clock;
        long m_Index;
        public BleMidiSession(MonotonicSessionClock clock, int capacity = 2048)
        { m_Clock = clock; m_Queue = new RejectingBoundedQueue<Sample>(capacity); }
        public long Generation { get; private set; }
        public long Rejected => m_Queue.Rejected;
        public long Stale { get; private set; }
        public string DeviceName { get; private set; } = "BLE";
        public event Action<MidiMessage> MessageReceived;
        public event Action<MidiMessage, Sample, double, bool> Diagnostic;
        public void Begin(long generation, string name)
        { ReleaseAll(); ClearPending(); Generation = generation; DeviceName = name; }
        public bool Enqueue(Sample sample) => m_Queue.TryEnqueue(sample);
        public void Flush()
        {
            while (m_Queue.TryDequeue(out var sample))
            {
                if (sample.Generation != Generation) { ++Stale; continue; }
                var time = m_Clock.AndroidNanosToAbsoluteSeconds(sample.TimestampNanos > 0 ? sample.TimestampNanos : sample.ReceivedNanos);
                var message = AndroidMidiInput.NormalizeMessage(time, ++m_Index, DeviceName, sample.Status, sample.First, sample.Second);
                Track(message);
                Diagnostic?.Invoke(message, sample, m_Clock.AndroidNanosToAbsoluteSeconds(sample.ReceivedNanos), false);
                MessageReceived?.Invoke(message);
            }
        }
        void Track(MidiMessage message)
        {
            var c = message.Channel - 1;
            if (message.EventType == MidiEventType.NoteOn) m_Notes[c, message.NoteNumber] = true;
            if (message.EventType == MidiEventType.NoteOff) m_Notes[c, message.NoteNumber] = false;
            if (message.EventType != MidiEventType.ControlChange) return;
            if (message.ControlNumber == 64) m_Sustain[c] = message.ControlValue >= 64;
            // Retain note ownership under sustain: disconnect still sends explicit NoteOff.
            if (message.ControlNumber == 123 && !m_Sustain[c])
                for (var n = 0; n < 128; ++n) m_Notes[c, n] = false;
        }
        public void End(long generation)
        { ReleaseAll(); Generation = generation; ClearPending(); }
        void ClearPending() { while (m_Queue.TryDequeue(out _)) ++Stale; }
        public void ReleaseAll()
        {
            var now = m_Clock.AbsoluteSeconds;
            for (var c = 0; c < 16; ++c)
            {
                for (var n = 0; n < 128; ++n)
                {
                    if (!m_Notes[c, n]) continue;
                    m_Notes[c, n] = false;
                    Emit(new MidiMessage(now, ++m_Index, DeviceName, MidiEventType.NoteOff, c + 1, n, 0, -1, -1));
                }
                if (m_Sustain[c])
                {
                    m_Sustain[c] = false;
                    Emit(new MidiMessage(now, ++m_Index, DeviceName, MidiEventType.ControlChange, c + 1, -1, -1, 64, 0));
                }
            }
        }
        void Emit(MidiMessage message)
        { Diagnostic?.Invoke(message, new Sample(Generation, 0, 0, 0, 0, 0), message.AbsoluteTimeSeconds, true); MessageReceived?.Invoke(message); }
    }
}
