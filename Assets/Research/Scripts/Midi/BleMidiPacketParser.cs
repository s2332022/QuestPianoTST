using System;
using System.Collections.Generic;

namespace QuestPianoMotion.Research
{
    /// <summary>BLE-MIDI 1.0 notification framing. Sender milliseconds are not Android nanoTime.
    /// Channel messages and running status cannot cross notification boundaries.
    /// Unsupported SysEx is discarded; Note, CC and channel framing remain independent.</summary>
    public sealed class BleMidiPacketParser
    {
        readonly List<BleMidiSession.Sample> m_Packet = new List<BleMidiSession.Sample>();
        public void Reset() => m_Packet.Clear();

        public bool Parse(byte[] packet, long generation, long receivedNanos, Action<BleMidiSession.Sample> emit)
        {
            m_Packet.Clear();
            if (packet == null || packet.Length < 3 || packet.Length > 512 || (packet[0] & 0xC0) != 0x80)
                return false;
            int high = packet[0] & 0x3F, low = -1, timestamp = -1, running = 0, i = 1;
            bool needsTimestamp = true;
            while (i < packet.Length)
            {
                if (needsTimestamp || packet[i] >= 0x80)
                {
                    if (packet[i] < 0x80) return false;
                    int next = packet[i++] & 0x7F;
                    if (low >= 0 && next < low) high = (high + 1) & 0x3F;
                    low = next; timestamp = (high << 7) | low;
                    if (i == packet.Length) return false;
                }
                needsTimestamp = false;
                int status = packet[i] >= 0x80 ? packet[i++] : running;
                if (status == 0) return false;
                if (status >= 0xF8) continue; // real-time never cancels running status
                if (status >= 0xF0)
                {
                    running = 0;
                    // SysEx is outside this note/CC receiver's supported subset.
                    if (status == 0xF0 || status == 0xF7) return false;
                    int commonLength = status == 0xF2 ? 2 : status == 0xF1 || status == 0xF3 ? 1 : 0;
                    if (status == 0xF4 || status == 0xF5) return false;
                    for (int n = 0; n < commonLength; ++n)
                        if (i >= packet.Length || packet[i++] >= 0x80) return false;
                    needsTimestamp = true;
                    continue;
                }
                running = status;
                int length = (status & 0xF0) == 0xC0 || (status & 0xF0) == 0xD0 ? 1 : 2;
                int first = -1, second = 0, count = 0;
                while (count < length)
                {
                    if (i >= packet.Length) return false; // no channel-message fragment carried into next packet
                    if (packet[i] >= 0x80)
                    {
                        // A timestamp + real-time status may interrupt data bytes.
                        int next = packet[i++] & 0x7F;
                        if (i >= packet.Length || packet[i] < 0xF8) return false;
                        if (next < low) high = (high + 1) & 0x3F;
                        low = next;
                        ++i;
                        continue;
                    }
                    if (count++ == 0) first = packet[i++]; else second = packet[i++];
                }
                int command = status & 0xF0;
                if (command == 0x80 || command == 0x90 || command == 0xB0)
                    m_Packet.Add(new BleMidiSession.Sample(generation, 0, receivedNanos, status, first, second, timestamp));
            }
            // Atomic validation: a malformed tail cannot publish a partial packet.
            foreach (var sample in m_Packet) emit(sample);
            return true;
        }
    }
}
