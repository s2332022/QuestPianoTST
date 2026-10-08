using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;
using QuestPianoMotion.Research.Distributed;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace QuestPianoMotion.Research
{
    [Preserve, DisallowMultipleComponent]
    public sealed class BleMidiInput : MonoBehaviour, IMidiInput
    {
        readonly List<MidiDeviceDescriptor> m_Devices = new List<MidiDeviceDescriptor>();
        readonly List<string> m_Addresses = new List<string>();
        BleMidiSession m_Session;
        AndroidJavaObject m_Bridge;
        StreamWriter m_Log;
        long m_Generation, m_JavaDropped, m_JavaStale, m_LastDropped;
        readonly BleMidiPacketParser m_Parser = new BleMidiPacketParser();
        string m_SelectedAddress = "", m_DeviceList = "", m_LastStatus = "";
        double m_NextPoll;
        public event Action<MidiMessage> MessageReceived;
        public event Action<MidiMessage, BleMidiSession.Sample, double, bool> ResearchEvent;
        public event Action DevicesChanged;
        public IReadOnlyList<MidiDeviceDescriptor> Devices => m_Devices;
        public bool IsConnected { get; private set; }
        public string ConnectedDeviceName { get; private set; } = "";
        public string LastEventText { get; private set; } = "None";
        public string Capability { get; private set; } = "Android device required";
        public string ConnectionState { get; private set; } = "idle";
        public string ScanState { get; private set; } = "idle";
        public string SelectedDevice => m_SelectedAddress;
        public string DeviceInfo { get; private set; } = "None";
        public string LogPath { get; private set; } = "";
        public string LogError { get; private set; } = "";
        public long Dropped => m_JavaDropped + (m_Session?.Rejected ?? 0);
        public long Stale => m_JavaStale + (m_Session?.Stale ?? 0);
        public long Generation => m_Generation;

        void OnEnable()
        {
            m_Session = new BleMidiSession(ResearchServices.Clock);
            m_Session.MessageReceived += OnMessage;
            m_Session.Diagnostic += LogMessage;
            m_Parser.Reset();
            m_LastDropped = m_JavaDropped = m_JavaStale = 0;
            m_LastStatus = m_DeviceList = "";
            try
            {
                var directory = Path.Combine(Application.persistentDataPath, "ble_diagnostics");
                Directory.CreateDirectory(directory);
                LogPath = Path.Combine(directory, "quest_ble_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + ".csv");
                m_Log = new StreamWriter(LogPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                m_Log.WriteLine("kind,generation,state,device_info,event_index,event_type,channel,note,velocity,control,value,android_timestamp_nanos,java_received_nanos,event_absolute_seconds,received_absolute_seconds,raw_clock,unity_clock,time_source,synthetic,dropped,stale,unity_observed_seconds,ble_timestamp_13bit_ms");
            }
            catch (Exception e) { LogError = e.Message; Debug.LogWarning("[BLE] Log unavailable: " + e.Message); }
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Recreate each enable; AndroidMidiInput's Awake-only lifecycle is not reused.
                m_Bridge = new AndroidJavaObject("com.questpianomotion.midi.QuestBleMidi");
                var before = ResearchServices.Clock.AbsoluteSeconds;
                var nanos = m_Bridge.Call<long>("nowNanos");
                var after = ResearchServices.Clock.AbsoluteSeconds;
                ResearchServices.Clock.SynchronizeAndroidClock(nanos, before, after);
                Capability = m_Bridge.Call<string>("capability");
                LogState(m_Bridge.Call<string>("diagnostics"));
                LogState("clock_sync before=" + Number(before) + " after=" + Number(after) + " java=" + nanos);
            }
            catch (Exception e) { Capability = "bridge_unavailable: " + e.Message; LogState(Capability); }
#endif
        }
        void Update()
        {
            if (m_Bridge == null) return;
            try
            {
                var now = ResearchServices.Clock.AbsoluteSeconds;
                if (now >= m_NextPoll)
                {
                    m_NextPoll = now + .25;
                    Capability = m_Bridge.Call<string>("capability");
                    if (Capability != "ready" && (IsConnected || ConnectionState == "connecting")) Disconnect();
                    RefreshDeviceList();
                }
                var status = m_Bridge.Call<string>("status").Split('|');
                if (status.Length == 6)
                {
                    ScanState = status[5];
                    if (long.TryParse(status[0], out var generation) && generation == m_Generation)
                    {
                        var wasConnected = IsConnected;
                        ConnectionState = status[1]; IsConnected = ConnectionState == "connected";
                        ConnectedDeviceName = IsConnected ? Decode(status[2]) : "";
                        DeviceInfo = Decode(status[2]) + " address=" + status[3] + " transport=" + status[4];
                        if (!IsConnected && wasConnected) { m_Session.End(m_Generation); m_Parser.Reset(); }
                    }
                }
                var fullStatus = Capability + " / " + ConnectionState + " / " + ScanState + " / " + DeviceInfo;
                if (fullStatus != m_LastStatus) { m_LastStatus = fullStatus; LogState(fullStatus); }
                m_JavaDropped = m_Bridge.Call<long>("dropped");
                m_JavaStale = m_Bridge.Call<long>("stale");
                var batch = IsConnected ? m_Bridge.Call<string>("poll", 256) : "";
                foreach (var line in batch.Split('\n'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var p = line.Split('|');
                    if (p.Length != 3 || !long.TryParse(p[0], out var epoch) || !long.TryParse(p[1], out var received))
                        throw new FormatException("Malformed BLE notification envelope");
                    // Gate BEFORE parsing so an obsolete callback cannot affect parser state.
                    if (epoch != m_Generation || !IsConnected) { ++m_JavaStale; continue; }
                    if (!m_Parser.Parse(Convert.FromBase64String(p[2]), epoch, received, s => m_Session.Enqueue(s)))
                        LogState("malformed_ble_packet");
                }
                m_Session.Flush();
                if (Dropped != m_LastDropped)
                {
                    m_LastDropped = Dropped;
                    LogState("queue_overflow: disconnect and release BLE state");
                    DisconnectTransport("queue_overflow");
                }

            }
            catch (Exception e)
            {
                Capability = "bridge_error: " + e.Message;
                DisconnectTransport("bridge_error");
                Call("close"); m_Bridge?.Dispose(); m_Bridge = null;
            }
        }
        public void RequestPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Permission.RequestUserPermissions(new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" });
#endif
            LogState("permission_requested");
        }
        public void StartScan() { RequestMissingPermissions(); Call("scan"); }
        void RequestMissingPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN") ||
                !Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT")) RequestPermissions();
#endif
        }
        public void StopScan() => Call("stopScan");
        public void RefreshDeviceList()
        {
            if (m_Bridge == null || Capability != "ready") return;
            var encoded = m_Bridge.Call<string>("listDevices");
            if (encoded == m_DeviceList) return;
            m_DeviceList = encoded; m_Devices.Clear(); m_Addresses.Clear();
            foreach (var line in encoded.Split('\n'))
            {
                var p = line.Split('|'); if (p.Length != 2) continue;
                m_Addresses.Add(p[0]);
                m_Devices.Add(new MidiDeviceDescriptor(m_Devices.Count, Decode(p[1]) + " [" + p[0] + "]"));
            }
            DevicesChanged?.Invoke();
        }
        public bool SelectDevice(int index)
        { if (index < 0 || index >= m_Addresses.Count) return false; m_SelectedAddress = m_Addresses[index]; return true; }
        public bool ConnectSelectedDevice()
        {
            if (m_Bridge == null || string.IsNullOrEmpty(m_SelectedAddress) || Capability != "ready") return false;
            if (ConnectionState == "connecting" || ConnectionState == "discovering" || ConnectionState == "subscribing") return false;
            m_Parser.Reset();
            m_Session.Begin(++m_Generation, "BLE " + m_SelectedAddress);
            IsConnected = false; ConnectionState = "connecting";
            m_Bridge.Call("connect", m_SelectedAddress, m_Generation);
            LogState("connect_requested " + m_SelectedAddress); return true;
        }
        public void Disconnect() => DisconnectTransport("disconnected");
        void DisconnectTransport(string reason)
        {
            m_Parser.Reset();
            m_Session?.End(++m_Generation);
            IsConnected = false; ConnectedDeviceName = ""; ConnectionState = reason;

            Call("disconnect"); LogState(reason);
        }
        void Call(string method)
        { try { m_Bridge?.Call(method); } catch (Exception e) { Capability = "bridge_error: " + e.Message; } }
        void OnDisable() => Close();
        void OnApplicationQuit() => Close();
        void Close()
        {
            if (m_Session == null && m_Bridge == null && m_Log == null) return;
            Disconnect(); Call("close"); m_Bridge?.Dispose(); m_Bridge = null;
            m_Session = null;
            m_Log?.Dispose(); m_Log = null;
        }
        void OnMessage(MidiMessage message)
        {
            LastEventText = message.EventType == MidiEventType.ControlChange
                ? $"CC{message.ControlNumber}={message.ControlValue} ch{message.Channel}"
                : $"{message.EventType} {message.NoteNumber} v{message.Velocity} ch{message.Channel}";
            MessageReceived?.Invoke(message);
        }
        void LogMessage(MidiMessage m, BleMidiSession.Sample s, double received, bool synthetic)
        {
            ResearchEvent?.Invoke(m, s, received, synthetic);
            Write(new object[] { "event", s.Generation, ConnectionState, DeviceInfo, m.EventIndex, m.EventType, m.Channel,
                m.NoteNumber, m.Velocity, m.ControlNumber, m.ControlValue, s.TimestampNanos, s.ReceivedNanos,
                Number(m.AbsoluteTimeSeconds), Number(received), synthetic ? "none" : "Android_System.nanoTime",
                "Quest_Stopwatch_absolute", synthetic ? "unity_release" : s.BleTimestampMilliseconds >= 0 ? "gatt_receive_ble_sender_timestamp_retained" : s.TimestampNanos > 0 ? "android_timestamp" : "java_receive_fallback",
                synthetic, Dropped, Stale, Number(ResearchServices.Clock.AbsoluteSeconds), s.BleTimestampMilliseconds });
        }
        void LogState(string text)
        {
            Debug.Log("[BLE] " + text);
            Write(new object[] { "state", m_Generation, text, DeviceInfo, "", "", "", "", "", "", "", "", "", "", "",
                "Android_System.nanoTime", "Quest_Stopwatch_absolute", "unity_observation", false, Dropped, Stale,
                Number(ResearchServices.Clock.AbsoluteSeconds), "" });
        }
        void Write(object[] values)
        {
            if (m_Log == null) return;
            try { m_Log.WriteLine(string.Join(",", Array.ConvertAll(values, v => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""))); }
            catch (Exception e) { LogError = e.Message; Debug.LogWarning("[BLE] Log write failed: " + e.Message); m_Log.Dispose(); m_Log = null; }
        }
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
