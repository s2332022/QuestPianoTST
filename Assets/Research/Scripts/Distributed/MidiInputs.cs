using System;
using System.Collections.Generic;
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

}
