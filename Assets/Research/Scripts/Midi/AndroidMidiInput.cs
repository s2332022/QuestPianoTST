using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace QuestPianoMotion.Research
{
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    [Preserve]
    public sealed class AndroidMidiInput : MonoBehaviour, Distributed.IMidiInput
    {
        readonly ConcurrentQueue<string> m_MessageQueue = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> m_ConnectionQueue = new ConcurrentQueue<string>();
        readonly List<MidiDeviceDescriptor> m_Devices = new List<MidiDeviceDescriptor>();
        AndroidJavaObject m_Bridge = null;
        double m_NextReconnectTime;
        long m_EventIndex;
        bool m_DeviceListDirty;
        string m_PreferredDeviceName = string.Empty;

        public event Action<MidiMessage> MessageReceived;
        public event Action DevicesChanged;
        public IReadOnlyList<MidiDeviceDescriptor> Devices => m_Devices;
        public int SelectedDeviceIndex { get; private set; } = -1;
        public bool IsConnected { get; private set; }
        public string ConnectedDeviceName { get; private set; } = string.Empty;
        public string LastEventText { get; private set; } = "None";

        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                m_Bridge = new AndroidJavaObject("com.questpianomotion.midi.QuestMidiBridge", gameObject.name);
                var before = ResearchServices.Clock.AbsoluteSeconds;
                var androidNow = m_Bridge.Call<long>("nowNanos");
                var after = ResearchServices.Clock.AbsoluteSeconds;
                ResearchServices.Clock.SynchronizeAndroidClock(androidNow, before, after);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MIDI] Android bridge unavailable: {exception.Message}", this);
            }
#endif
            RefreshDeviceList();
        }

        void OnEnable()
        {
            m_NextReconnectTime = 0d;
        }

        void Update()
        {
            while (m_ConnectionQueue.TryDequeue(out var connection))
                ApplyConnectionMessage(connection);
            while (m_MessageQueue.TryDequeue(out var encoded))
                ApplyMidiMessage(encoded);

            if (m_DeviceListDirty)
            {
                m_DeviceListDirty = false;
                RefreshDeviceList();
            }

            if (!IsConnected && !string.IsNullOrEmpty(m_PreferredDeviceName) &&
                ResearchServices.Clock.AbsoluteSeconds >= m_NextReconnectTime)
            {
                m_NextReconnectTime = ResearchServices.Clock.AbsoluteSeconds + 2d;
                TryReconnectPreferredDevice();
            }
        }

        void OnDisable()
        {
            SafeClose();
        }

        void OnApplicationQuit()
        {
            SafeClose();
        }

        public void RefreshDeviceList()
        {
            m_Devices.Clear();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_Bridge != null)
            {
                try
                {
                    ParseDeviceList(m_Bridge.Call<string>("listDevices"), m_Devices);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[MIDI] Device enumeration failed: {exception.Message}", this);
                }
            }
#endif
            if (m_Devices.Count == 0)
                SelectedDeviceIndex = -1;
            else if (SelectedDeviceIndex < 0 || SelectedDeviceIndex >= m_Devices.Count)
                SelectedDeviceIndex = 0;
            DevicesChanged?.Invoke();
        }

        public void SelectNextDevice()
        {
            if (m_Devices.Count == 0)
            {
                RefreshDeviceList();
                return;
            }
            SelectedDeviceIndex = (SelectedDeviceIndex + 1) % m_Devices.Count;
            ConnectSelectedDevice();
        }

        public void ConnectSelectedDevice()
        {
            if (SelectedDeviceIndex < 0 || SelectedDeviceIndex >= m_Devices.Count || m_Bridge == null)
                return;
            var device = m_Devices[SelectedDeviceIndex];
            m_PreferredDeviceName = device.Name;
            try
            {
                IsConnected = false;
                m_Bridge.Call("openDevice", device.DeviceId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MIDI] Open failed: {exception.Message}", this);
            }
        }

        public string SelectedDeviceName => SelectedDeviceIndex >= 0 && SelectedDeviceIndex < m_Devices.Count
            ? m_Devices[SelectedDeviceIndex].Name : "None";

        bool Distributed.IMidiInput.SelectDevice(int index)
        {
            if (index < 0 || index >= m_Devices.Count) return false;
            SelectedDeviceIndex = index;
            return true;
        }

        bool Distributed.IMidiInput.ConnectSelectedDevice()
        {
            if (SelectedDeviceIndex < 0 || SelectedDeviceIndex >= m_Devices.Count) return false;
            ConnectSelectedDevice();
            return true;
        }

        void Distributed.IMidiInput.Disconnect() => SafeClose();

        // Called by UnitySendMessage. Queue only; GameObjects are never mutated from the Java MIDI callback.
        [Preserve]
        public void OnAndroidMidiMessage(string encoded)
        {
            if (!string.IsNullOrEmpty(encoded))
                m_MessageQueue.Enqueue(encoded);
        }

        [Preserve]
        public void OnAndroidMidiConnection(string encoded)
        {
            m_ConnectionQueue.Enqueue(encoded ?? string.Empty);
        }

        [Preserve]
        public void OnAndroidMidiDevicesChanged(string unused)
        {
            m_DeviceListDirty = true;
        }

        public void InjectForTesting(int status, int data1, int data2)
        {
            Dispatch(ResearchServices.Clock.AbsoluteSeconds, status, data1, data2, "Simulated MIDI");
        }

        void ApplyMidiMessage(string encoded)
        {
            var parts = encoded.Split('|');
            if (parts.Length != 4 || !long.TryParse(parts[0], out var nanos) ||
                !int.TryParse(parts[1], out var status) || !int.TryParse(parts[2], out var data1) ||
                !int.TryParse(parts[3], out var data2))
                return;
            Dispatch(ResearchServices.Clock.AndroidNanosToAbsoluteSeconds(nanos), status, data1, data2,
                ConnectedDeviceName);
        }

        public static MidiMessage NormalizeMessage(double absoluteTime, long eventIndex, string deviceName, int status, int data1, int data2)
        {
            var command = status & 0xF0;
            var channel = (status & 0x0F) + 1;
            MidiEventType type;
            var note = -1;
            var velocity = -1;
            var control = -1;
            var value = -1;
            if (command == 0x90)
            {
                type = data2 == 0 ? MidiEventType.NoteOff : MidiEventType.NoteOn;
                note = data1;
                velocity = data2;
            }
            else if (command == 0x80)
            {
                type = MidiEventType.NoteOff;
                note = data1;
                velocity = data2;
            }
            else if (command == 0xB0)
            {
                type = MidiEventType.ControlChange;
                control = data1;
                value = data2;
            }
            else
            {
                type = MidiEventType.Other;
            }
            return new MidiMessage(absoluteTime, eventIndex, deviceName, type, channel,
                note, velocity, control, value);
        }

        void Dispatch(double absoluteTime, int status, int data1, int data2, string deviceName)
        {
            var message = NormalizeMessage(absoluteTime, ++m_EventIndex, deviceName, status, data1, data2);
            var type = message.EventType;
            var control = message.ControlNumber;
            var value = message.ControlValue;
            var channel = message.Channel;
            var note = message.NoteNumber;
            var velocity = message.Velocity;
            LastEventText = type == MidiEventType.ControlChange
                ? $"CC{control}={value} ch{channel}"
                : $"{type} {note} v{velocity} ch{channel}";
            MessageReceived?.Invoke(message);
        }

        void ApplyConnectionMessage(string encoded)
        {
            var split = encoded.IndexOf('|');
            var state = split >= 0 ? encoded.Substring(0, split) : encoded;
            var name = split >= 0 ? DecodeBase64(encoded.Substring(split + 1)) : string.Empty;
            IsConnected = state == "1";
            ConnectedDeviceName = IsConnected ? name : string.Empty;
            if (!string.IsNullOrEmpty(name))
                m_PreferredDeviceName = name;
        }

        void TryReconnectPreferredDevice()
        {
            RefreshDeviceList();
            for (var i = 0; i < m_Devices.Count; ++i)
            {
                if (!string.Equals(m_Devices[i].Name, m_PreferredDeviceName, StringComparison.Ordinal))
                    continue;
                SelectedDeviceIndex = i;
                ConnectSelectedDevice();
                return;
            }
        }

        void SafeClose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_Bridge != null)
            {
                try { m_Bridge.Call("close"); }
                catch (Exception) { }
                m_Bridge.Dispose();
                m_Bridge = null;
            }
#endif
            IsConnected = false;
        }

        static void ParseDeviceList(string encoded, List<MidiDeviceDescriptor> output)
        {
            if (string.IsNullOrEmpty(encoded))
                return;
            var lines = encoded.Split('\n');
            for (var i = 0; i < lines.Length; ++i)
            {
                var split = lines[i].IndexOf('|');
                if (split <= 0 || !int.TryParse(lines[i].Substring(0, split), out var id))
                    continue;
                output.Add(new MidiDeviceDescriptor(id, DecodeBase64(lines[i].Substring(split + 1))));
            }
        }

        static string DecodeBase64(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch (FormatException) { return value ?? string.Empty; }
        }
    }
}
