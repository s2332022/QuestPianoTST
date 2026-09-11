using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    public interface IMidiInput
    {
        event Action<MidiMessage> MessageReceived; event Action DevicesChanged;
        IReadOnlyList<MidiDeviceDescriptor> Devices { get; } bool IsConnected { get; }
        string ConnectedDeviceName { get; } string LastEventText { get; }
        void RefreshDeviceList(); bool SelectDevice(int index); bool ConnectSelectedDevice(); void Disconnect();
    }

    [DisallowMultipleComponent]
    public sealed class NetworkMidiInput : MonoBehaviour, IMidiInput
    {
        readonly RejectingBoundedQueue<MidiPacket> m_Queue=new RejectingBoundedQueue<MidiPacket>(2048); readonly ushort[] m_ActiveChannels=new ushort[128];
        readonly List<MidiDeviceDescriptor> m_Devices=new List<MidiDeviceDescriptor>{new MidiDeviceDescriptor(0,"PC network MIDI")};long m_EventIndex;
        public event Action<MidiMessage> MessageReceived;public event Action DevicesChanged;
        public IReadOnlyList<MidiDeviceDescriptor> Devices=>m_Devices;public bool IsConnected{get;private set;}public string ConnectedDeviceName=>IsConnected?"PC network MIDI":string.Empty;
        public string LastEventText{get;private set;}="None";public long RejectedMessages=>m_Queue.Rejected;
        public bool Enqueue(in MidiPacket packet){var ok=m_Queue.TryEnqueue(packet);if(ok)IsConnected=true;return ok;}
        void Update(){while(m_Queue.TryDequeue(out var p))Dispatch(p);}
        void Dispatch(MidiPacket p)
        {
            var note=p.Note==255?-1:p.Note;var control=p.Control==255?-1:p.Control;var type=p.EventType;
            if(type==MidiEventType.NoteOn&&p.Velocity==0)type=MidiEventType.NoteOff;
            if(note>=0&&note<128&&p.Channel>=1&&p.Channel<=16){var bit=(ushort)(1<<(p.Channel-1));if(type==MidiEventType.NoteOn)m_ActiveChannels[note]|=bit;else if(type==MidiEventType.NoteOff)m_ActiveChannels[note]&=(ushort)~bit;}
            var message=new MidiMessage(p.Header.SenderTimestamp,p.EventIndex!=0?p.EventIndex:++m_EventIndex,"PC network MIDI",type,p.Channel,note,p.Velocity,control,p.Value);
            LastEventText=type==MidiEventType.ControlChange?$"CC{control}={p.Value} ch{p.Channel}":$"{type} {note} v{p.Velocity} ch{p.Channel}";MessageReceived?.Invoke(message);
        }
        public void ReleaseAll(double now)
        {
            for(var note=0;note<128;++note)for(var channel=1;channel<=16;++channel){var bit=(ushort)(1<<(channel-1));if((m_ActiveChannels[note]&bit)==0)continue;m_ActiveChannels[note]&=(ushort)~bit;var message=new MidiMessage(now,++m_EventIndex,"Network disconnect",MidiEventType.NoteOff,channel,note,0,-1,-1);MessageReceived?.Invoke(message);}
        }
        public void RefreshDeviceList()=>DevicesChanged?.Invoke();public bool SelectDevice(int index)=>index==0;public bool ConnectSelectedDevice(){IsConnected=true;return true;}public void Disconnect(){if(IsConnected)ReleaseAll(ResearchServices.Clock.AbsoluteSeconds);IsConnected=false;}
    }

    [DisallowMultipleComponent]
    public sealed class PcMidiInput : MonoBehaviour, IMidiInput
    {
        delegate void MidiInProc(IntPtr hMidiIn,uint wMsg,UIntPtr dwInstance,UIntPtr dwParam1,UIntPtr dwParam2);
        const uint CallbackFunction=0x00030000;const uint MimData=0x3C3;const uint MimOpen=0x3C1, MimClose=0x3C2;
        static readonly MidiInProc s_Callback=Callback;static readonly object s_MapLock=new object();static readonly Dictionary<IntPtr,PcMidiInput> s_Instances=new Dictionary<IntPtr,PcMidiInput>();
        readonly ConcurrentQueue<uint> m_RawMessages=new ConcurrentQueue<uint>();readonly ConcurrentQueue<uint> m_ConnectionEvents=new ConcurrentQueue<uint>();readonly List<MidiDeviceDescriptor> m_Devices=new List<MidiDeviceDescriptor>();
        IntPtr m_Handle;int m_Selected=-1;long m_EventIndex;int m_Queued;const int MaxQueue=4096;
        public event Action<MidiMessage> MessageReceived;public event Action DevicesChanged;public IReadOnlyList<MidiDeviceDescriptor> Devices=>m_Devices;
        public bool IsConnected=>m_Handle!=IntPtr.Zero;public string ConnectedDeviceName{get;private set;}=string.Empty;public string LastEventText{get;private set;}="None";public long QueueOverflows{get;private set;}
        void OnEnable()=>RefreshDeviceList();void OnDisable()=>Disconnect();void OnApplicationQuit()=>Disconnect();
        void Update(){while(m_ConnectionEvents.TryDequeue(out var state)){if(state==MimClose){lock(s_MapLock)s_Instances.Remove(m_Handle);m_Handle=IntPtr.Zero;ConnectedDeviceName=string.Empty;LastEventText="MIDI device disconnected";DevicesChanged?.Invoke();}}while(m_RawMessages.TryDequeue(out var packed)){System.Threading.Interlocked.Decrement(ref m_Queued);Dispatch(packed);}}
        public void RefreshDeviceList()
        {
            m_Devices.Clear();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var count=midiInGetNumDevs();for(uint i=0;i<count;++i){if(midiInGetDevCaps(new UIntPtr(i),out var caps,(uint)Marshal.SizeOf<MidiInCaps>())==0)m_Devices.Add(new MidiDeviceDescriptor((int)i,caps.Name));}
#endif
            if(m_Devices.Count==0)m_Selected=-1;else if(m_Selected<0||m_Selected>=m_Devices.Count)m_Selected=0;DevicesChanged?.Invoke();
        }
        public bool SelectDevice(int index){if(index<0||index>=m_Devices.Count)return false;m_Selected=index;return true;}
        public bool ConnectSelectedDevice()
        {
            Disconnect();if(m_Selected<0||m_Selected>=m_Devices.Count)return false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if(midiInOpen(out m_Handle,(uint)m_Devices[m_Selected].DeviceId,s_Callback,UIntPtr.Zero,CallbackFunction)!=0){m_Handle=IntPtr.Zero;return false;}
            lock(s_MapLock)s_Instances[m_Handle]=this;if(midiInStart(m_Handle)!=0){Disconnect();return false;}ConnectedDeviceName=m_Devices[m_Selected].Name;return true;
#else
            return false;
#endif
        }
        public void Disconnect()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var h=m_Handle;if(h==IntPtr.Zero)return;m_Handle=IntPtr.Zero;lock(s_MapLock)s_Instances.Remove(h);midiInStop(h);midiInReset(h);midiInClose(h);
#endif
            ConnectedDeviceName=string.Empty;
        }
        static void Callback(IntPtr handle,uint message,UIntPtr instance,UIntPtr param1,UIntPtr param2)
        {PcMidiInput target;lock(s_MapLock)if(!s_Instances.TryGetValue(handle,out target))return;if(message==MimData){if(target.m_Queued>=MaxQueue){target.QueueOverflows++;return;}target.m_RawMessages.Enqueue(unchecked((uint)param1.ToUInt64()));System.Threading.Interlocked.Increment(ref target.m_Queued);}else if(message==MimOpen||message==MimClose)target.m_ConnectionEvents.Enqueue(message);}
        void Dispatch(uint packed)
        {
            var status=(int)(packed&0xFF);var data1=(int)((packed>>8)&0xFF);var data2=(int)((packed>>16)&0xFF);var command=status&0xF0;var channel=(status&0x0F)+1;MidiEventType type;var note=-1;var velocity=-1;var control=-1;var value=-1;
            if(command==0x90){type=data2==0?MidiEventType.NoteOff:MidiEventType.NoteOn;note=data1;velocity=data2;}else if(command==0x80){type=MidiEventType.NoteOff;note=data1;velocity=data2;}else if(command==0xB0){type=MidiEventType.ControlChange;control=data1;value=data2;}else type=MidiEventType.Other;
            var now=ResearchServices.Clock.AbsoluteSeconds;var midi=new MidiMessage(now,++m_EventIndex,ConnectedDeviceName,type,channel,note,velocity,control,value);LastEventText=type==MidiEventType.ControlChange?$"CC{control}={value} ch{channel}":$"{type} {note} v{velocity} ch{channel}";MessageReceived?.Invoke(midi);
        }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct MidiInCaps{public ushort ManufacturerId,ProductId;public uint DriverVersion;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public uint Support;}
        [DllImport("winmm.dll")]static extern uint midiInGetNumDevs();[DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern uint midiInGetDevCaps(UIntPtr id,out MidiInCaps caps,uint size);
        [DllImport("winmm.dll")]static extern uint midiInOpen(out IntPtr handle,uint deviceId,MidiInProc callback,UIntPtr instance,uint flags);[DllImport("winmm.dll")]static extern uint midiInStart(IntPtr handle);[DllImport("winmm.dll")]static extern uint midiInStop(IntPtr handle);[DllImport("winmm.dll")]static extern uint midiInReset(IntPtr handle);[DllImport("winmm.dll")]static extern uint midiInClose(IntPtr handle);
#endif
    }
}
