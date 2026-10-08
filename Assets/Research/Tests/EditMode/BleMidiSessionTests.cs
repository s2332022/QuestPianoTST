using System.Collections.Generic;
using NUnit.Framework;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class BleMidiSessionTests
    {
        static BleMidiSession.Sample Sample(long generation, int status, int first, int second, long timestamp = 2000000000, long received = 3000000000)
            => new BleMidiSession.Sample(generation, timestamp, received, status, first, second);
        [Test]
        public void NormalizationRetainsChannelNoteVelocityAndControllers()
        {
            var input = new BleMidiSession(new MonotonicSessionClock()); var events = new List<MidiMessage>(); input.MessageReceived += events.Add;
            input.Begin(1, "piano");
            input.Enqueue(Sample(1, 0x92, 60, 99)); input.Enqueue(Sample(1, 0x92, 60, 0));
            input.Enqueue(Sample(1, 0x82, 61, 7)); input.Enqueue(Sample(1, 0xB2, 64, 127)); input.Enqueue(Sample(1, 0xB2, 123, 0)); input.Flush();
            Assert.That(events.Count, Is.EqualTo(5)); Assert.That(events[0].Channel, Is.EqualTo(3)); Assert.That(events[0].Velocity, Is.EqualTo(99));
            Assert.That(events[0].NoteNumber, Is.EqualTo(60)); Assert.That(events[1].EventType, Is.EqualTo(MidiEventType.NoteOff));
            Assert.That(events[2].Velocity, Is.EqualTo(7)); Assert.That(events[3].ControlNumber, Is.EqualTo(64)); Assert.That(events[3].ControlValue, Is.EqualTo(127));
            Assert.That(events[4].ControlNumber, Is.EqualTo(123));
        }
        [Test]
        public void TimestampAndArrivalRemainSeparateInExistingUnityClockDomain()
        {
            var clock = new MonotonicSessionClock(); clock.SynchronizeAndroidClock(1000000000, 10, 12);
            var input = new BleMidiSession(clock); input.Begin(1, "piano");
            double received = 0; MidiMessage message = default; BleMidiSession.Sample raw = default;
            input.Diagnostic += (m, s, r, synthetic) => { message = m; raw = s; received = r; };
            input.Enqueue(Sample(1, 0x90, 60, 1)); input.Flush();
            Assert.That(message.AbsoluteTimeSeconds, Is.EqualTo(12).Within(1e-9)); Assert.That(received, Is.EqualTo(13).Within(1e-9));
            Assert.That(raw.TimestampNanos, Is.EqualTo(2000000000)); Assert.That(raw.ReceivedNanos, Is.EqualTo(3000000000));
            input.Enqueue(Sample(1, 0x80, 60, 0, 0)); input.Flush();
            Assert.That(raw.TimestampNanos, Is.Zero); Assert.That(message.AbsoluteTimeSeconds, Is.EqualTo(received));
        }
        [Test]
        public void DisconnectReleasesEveryActiveChannelAndSustainExactlyOnce()
        {
            var input = new BleMidiSession(new MonotonicSessionClock()); input.Begin(1, "piano");
            input.Enqueue(Sample(1, 0x90, 60, 1)); input.Enqueue(Sample(1, 0x91, 60, 2)); input.Enqueue(Sample(1, 0xB1, 64, 127)); input.Flush();
            var events = new List<MidiMessage>(); input.MessageReceived += events.Add;
            input.End(2); input.End(3);
            Assert.That(events.Count, Is.EqualTo(3)); Assert.That(events.FindAll(x => x.EventType == MidiEventType.NoteOff).Count, Is.EqualTo(2));
            Assert.That(events[2].Channel, Is.EqualTo(2)); Assert.That(events[2].ControlNumber, Is.EqualTo(64)); Assert.That(events[2].ControlValue, Is.Zero);
        }
        [Test]
        public void SustainAndAllNotesOffKeepDisconnectOwnership()
        {
            var input = new BleMidiSession(new MonotonicSessionClock()); input.Begin(1, "piano");
            input.Enqueue(Sample(1, 0x90, 60, 1)); input.Enqueue(Sample(1, 0xB0, 64, 127)); input.Enqueue(Sample(1, 0xB0, 123, 0)); input.Flush();
            var events = new List<MidiMessage>(); input.MessageReceived += events.Add; input.End(2);
            Assert.That(events.Count, Is.EqualTo(2)); Assert.That(events[0].EventType, Is.EqualTo(MidiEventType.NoteOff));
            Assert.That(events[1].ControlValue, Is.Zero);
        }
        [Test]
        public void ReconnectRejectsOldCallbacksAndQueuedEvents()
        {
            var input = new BleMidiSession(new MonotonicSessionClock()); input.Begin(1, "old"); input.Enqueue(Sample(1, 0x90, 50, 1));
            input.Begin(2, "new"); var events = new List<MidiMessage>(); input.MessageReceived += events.Add;
            input.Enqueue(Sample(1, 0x90, 51, 1)); input.Enqueue(Sample(2, 0x90, 60, 1)); input.Flush();
            Assert.That(events.Count, Is.EqualTo(1)); Assert.That(events[0].NoteNumber, Is.EqualTo(60)); Assert.That(events[0].DeviceName, Is.EqualTo("new")); Assert.That(input.Stale, Is.EqualTo(2));
        }
        [Test]
        public void SustainOffAndCc123WithoutSustainDoNotLeaveOwnedNotes()
        {
            var input = new BleMidiSession(new MonotonicSessionClock()); input.Begin(1, "piano");
            input.Enqueue(Sample(1, 0x90, 60, 1)); input.Enqueue(Sample(1, 0xB0, 64, 127));
            input.Enqueue(Sample(1, 0xB0, 64, 0)); input.Enqueue(Sample(1, 0xB0, 123, 0)); input.Flush();
            var events = new List<MidiMessage>(); input.MessageReceived += events.Add; input.End(2);
            Assert.That(events, Is.Empty);
        }
        [Test]
        public void ManifestPatchIsAdditiveIdempotentAndDoesNotAdvertise()
        {
            const string android = "http://schemas.android.com/apk/res/android";
            var document = new System.Xml.XmlDocument();
            document.LoadXml("<manifest xmlns:android='" + android + "'><uses-permission android:name='android.permission.INTERNET'/><uses-permission android:name='android.permission.ACCESS_NETWORK_STATE'/><application/></manifest>");
            var type = System.Type.GetType("QuestPianoMotion.Research.Editor.AndroidBleMidiBuildGuard, QuestPianoMotion.Research.Editor", true);
            var patch = type.GetMethod("AddPermissions"); patch.Invoke(null, new object[] { document }); patch.Invoke(null, new object[] { document });
            Assert.That(document.SelectNodes("/manifest/uses-permission").Count, Is.EqualTo(4));
            var ns = new System.Xml.XmlNamespaceManager(document.NameTable); ns.AddNamespace("android", android);
            Assert.That(document.SelectSingleNode("/manifest/uses-permission[@android:name='android.permission.BLUETOOTH_SCAN']/@android:usesPermissionFlags", ns).Value, Is.EqualTo("neverForLocation"));
            Assert.That(document.SelectSingleNode("/manifest/uses-permission[@android:name='android.permission.BLUETOOTH_ADVERTISE']", ns), Is.Null);
            Assert.That(document.SelectNodes("/manifest/uses-feature[@android:required='false']", ns).Count, Is.EqualTo(2));
            Assert.That(document.SelectSingleNode("/manifest/application"), Is.Not.Null);
        }
        [Test]
        public void OverflowIsCountedAndCanReleaseState()
        {
            var input = new BleMidiSession(new MonotonicSessionClock(), 1); input.Begin(1, "piano");
            Assert.That(input.Enqueue(Sample(1, 0x90, 60, 100)), Is.True); Assert.That(input.Enqueue(Sample(1, 0x80, 60, 0)), Is.False);
            var events = new List<MidiMessage>(); input.MessageReceived += events.Add; input.Flush(); input.End(2);
            Assert.That(input.Rejected, Is.EqualTo(1)); Assert.That(events[1].EventType, Is.EqualTo(MidiEventType.NoteOff));
        }
    }
}
