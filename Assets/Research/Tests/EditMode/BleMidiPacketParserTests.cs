using System.Collections.Generic;
using NUnit.Framework;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class BleMidiPacketParserTests
    {
        static List<MidiMessage> Decode(byte[] packet, out List<BleMidiSession.Sample> raw)
        {
            raw = new List<BleMidiSession.Sample>();
            Assert.That(new BleMidiPacketParser().Parse(packet, 4, 123456789, raw.Add), Is.True);
            var input = new BleMidiSession(new MonotonicSessionClock()); input.Begin(4, "standard BLE-MIDI");
            var messages = new List<MidiMessage>(); input.MessageReceived += messages.Add;
            foreach (var sample in raw) input.Enqueue(sample);
            input.Flush(); return messages;
        }
        [TestCase(0x92, 99, MidiEventType.NoteOn)]
        [TestCase(0x82, 7, MidiEventType.NoteOff)]
        [TestCase(0x92, 0, MidiEventType.NoteOff)]
        public void NoteAndVelocityAreNormalized(int status, int velocity, MidiEventType type)
        {
            var messages = Decode(new byte[] { 0x81, 0x85, (byte)status, 60, (byte)velocity }, out var raw);
            Assert.That(messages.Count, Is.EqualTo(1)); Assert.That(messages[0].EventType, Is.EqualTo(type));
            Assert.That(messages[0].Channel, Is.EqualTo(3)); Assert.That(messages[0].NoteNumber, Is.EqualTo(60));
            Assert.That(messages[0].Velocity, Is.EqualTo(velocity));
            Assert.That(raw[0].BleTimestampMilliseconds, Is.EqualTo(133));
            Assert.That(raw[0].TimestampNanos, Is.Zero); Assert.That(raw[0].ReceivedNanos, Is.EqualTo(123456789));
        }
        [Test]
        public void RunningStatusWithSharedAndExplicitTimestamps()
        {
            var messages = Decode(new byte[] { 0x80, 0x81, 0x90, 60, 100, 61, 101, 0x82, 62, 0 }, out var raw);
            Assert.That(messages.Count, Is.EqualTo(3)); Assert.That(messages[1].NoteNumber, Is.EqualTo(61));
            Assert.That(messages[2].EventType, Is.EqualTo(MidiEventType.NoteOff));
            Assert.That(raw[1].BleTimestampMilliseconds, Is.EqualTo(1)); Assert.That(raw[2].BleTimestampMilliseconds, Is.EqualTo(2));
        }
        [Test]
        public void MultipleMessagesAndTimestampLowWrap()
        {
            var messages = Decode(new byte[] { 0xBF, 0xFF, 0x90, 60, 100, 0x80, 0x80, 60, 9, 0x81, 0xB0, 64, 127 }, out var raw);
            Assert.That(messages.Count, Is.EqualTo(3)); Assert.That(raw[0].BleTimestampMilliseconds, Is.EqualTo(8191));
            Assert.That(raw[1].BleTimestampMilliseconds, Is.Zero); Assert.That(messages[2].ControlNumber, Is.EqualTo(64));
        }
        [Test]
        public void NotificationBoundariesResetRunningStatusAndRejectSplitChannelMessages()
        {
            var parser = new BleMidiPacketParser(); var raw = new List<BleMidiSession.Sample>();
            Assert.That(parser.Parse(new byte[] { 0x80, 0x81, 0x90, 60 }, 1, 10, raw.Add), Is.False);
            Assert.That(parser.Parse(new byte[] { 0x80, 0x82, 100 }, 1, 11, raw.Add), Is.False);
            Assert.That(raw, Is.Empty);
            Assert.That(parser.Parse(new byte[] { 0x80, 0x83, 0x90, 61, 99 }, 1, 12, raw.Add), Is.True);
            Assert.That(parser.Parse(new byte[] { 0x80, 0x84, 62, 99 }, 1, 13, raw.Add), Is.False);
            Assert.That(parser.Parse(new byte[] { 0x80, 0x85, 0x80, 61, 0 }, 1, 14, raw.Add), Is.True);
            Assert.That(raw.Count, Is.EqualTo(2)); Assert.That(raw[1].Status, Is.EqualTo(0x80));
        }
        [Test]
        public void RealTimeDoesNotChangeRunningStatusOrInterruptNote()
        {
            var messages = Decode(new byte[] { 0x80, 0x81, 0x90, 60, 0x82, 0xF8, 100, 0x83, 0xF8, 61, 90 }, out var raw);
            Assert.That(messages.Count, Is.EqualTo(2)); Assert.That(messages[0].Velocity, Is.EqualTo(100));
            Assert.That(messages[1].NoteNumber, Is.EqualTo(61)); Assert.That(raw[0].BleTimestampMilliseconds, Is.EqualTo(1));
        }
        [Test]
        public void UnsupportedChannelLengthsDoNotBecomeNotes()
        {
            var messages = Decode(new byte[] { 0x80, 0x81, 0xC0, 4, 5, 0x82, 0xE0, 1, 2, 0x83, 0x90, 60, 100 }, out _);
            Assert.That(messages.Count, Is.EqualTo(1)); Assert.That(messages[0].NoteNumber, Is.EqualTo(60));
        }
        [Test]
        public void MalformedPacketsAreAtomicAndNextPacketRecovers()
        {
            var parser = new BleMidiPacketParser(); var raw = new List<BleMidiSession.Sample>();
            byte[][] invalid = { null, new byte[0], new byte[] { 0x80 }, new byte[] { 0, 0x81, 0x90, 60, 99 },
                new byte[] { 0xC0, 0x81, 0x90, 60, 99 }, new byte[] { 0x80, 0x81 },
                new byte[] { 0x80, 0x81, 0x90, 60, 99, 0x82, 0x80, 60 },
                new byte[] { 0x80, 0x81, 0x90, 0x80, 99 }, new byte[] { 0x80, 0x81, 0xF0, 1, 2 } };
            foreach (var packet in invalid) Assert.That(parser.Parse(packet, 1, 2, raw.Add), Is.False);
            Assert.That(raw, Is.Empty);
            Assert.That(parser.Parse(new byte[] { 0x80, 0x81, 0x90, 60, 99 }, 1, 2, raw.Add), Is.True);
            Assert.That(raw.Count, Is.EqualTo(1));
        }
        [Test]
        public void OldGenerationNotificationCannotPublishIntoNewSession()
        {
            var input = new BleMidiSession(new MonotonicSessionClock()); input.Begin(2, "new");
            var events = new List<MidiMessage>(); input.MessageReceived += events.Add;
            var parser = new BleMidiPacketParser();
            parser.Parse(new byte[] { 0x80, 0x81, 0x90, 60, 99 }, 1, 2, s => input.Enqueue(s));
            parser.Parse(new byte[] { 0x80, 0x81, 0x90, 61, 99 }, 2, 3, s => input.Enqueue(s));
            input.Flush(); Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].NoteNumber, Is.EqualTo(61)); Assert.That(input.Stale, Is.EqualTo(1));
        }
    }
}
