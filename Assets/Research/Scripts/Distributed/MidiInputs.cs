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
        readonly RejectingBoundedQueue<MidiPacket> m_Queue=new RejectingBoundedQueue<MidiPacket>(2048); readonly ushort[] m_ActiveChannels=new ushort[128]; ushort m_SustainChannels;
        readonly List<MidiDeviceDescriptor> m_Devices=new List<MidiDeviceDescriptor>{new MidiDeviceDescriptor(0,"PC network MIDI")};long m_EventIndex;
        public event Action<MidiMessage> MessageReceived;public event Action DevicesChanged;
        public IReadOnlyList<MidiDeviceDescriptor> Devices=>m_Devices;public bool IsConnected{get;private set;}public string ConnectedDeviceName=>IsConnected?"PC network MIDI":string.Empty;
        public string LastEventText{get;private set;}="None";public long RejectedMessages=>m_Queue.Rejected;
        public bool Enqueue(in MidiPacket packet){var ok=m_Queue.TryEnqueue(packet);if(ok)IsConnected=true;return ok;}
        void Update()=>FlushPending();
        public void FlushPending(){while(m_Queue.TryDequeue(out var p))Dispatch(p);}
        void Dispatch(MidiPacket p)
        {
            var note=p.Note==255?-1:p.Note;var control=p.Control==255?-1:p.Control;var type=p.EventType;
            if(type==MidiEventType.NoteOn&&p.Velocity==0)type=MidiEventType.NoteOff;
            if(p.Channel>=1&&p.Channel<=16&&type==MidiEventType.ControlChange&&control==64)
            {var bit=(ushort)(1<<(p.Channel-1));if(p.Value>=64)m_SustainChannels|=bit;else m_SustainChannels&=(ushort)~bit;}
            if(p.Channel>=1&&p.Channel<=16&&type==MidiEventType.ControlChange&&control==123)
            {var bit=(ushort)(1<<(p.Channel-1));for(var n=0;n<128;++n)m_ActiveChannels[n]&=(ushort)~bit;}
            if(note>=0&&note<128&&p.Channel>=1&&p.Channel<=16){var bit=(ushort)(1<<(p.Channel-1));if(type==MidiEventType.NoteOn)m_ActiveChannels[note]|=bit;else if(type==MidiEventType.NoteOff)m_ActiveChannels[note]&=(ushort)~bit;}
            var message=new MidiMessage(p.Header.SenderTimestamp,p.EventIndex!=0?p.EventIndex:++m_EventIndex,"PC network MIDI",type,p.Channel,note,p.Velocity,control,p.Value);
            LastEventText=type==MidiEventType.ControlChange?$"CC{control}={p.Value} ch{p.Channel}":$"{type} {note} v{p.Velocity} ch{p.Channel}";MessageReceived?.Invoke(message);
        }
        public void ReleaseAll(double now)
        {
            for(var note=0;note<128;++note)for(var channel=1;channel<=16;++channel){var bit=(ushort)(1<<(channel-1));if((m_ActiveChannels[note]&bit)==0)continue;m_ActiveChannels[note]&=(ushort)~bit;var message=new MidiMessage(now,++m_EventIndex,"Network disconnect",MidiEventType.NoteOff,channel,note,0,-1,-1);MessageReceived?.Invoke(message);}
            for(var channel=1;channel<=16;++channel){var bit=(ushort)(1<<(channel-1));if((m_SustainChannels&bit)==0)continue;m_SustainChannels&=(ushort)~bit;MessageReceived?.Invoke(new MidiMessage(now,++m_EventIndex,"Network disconnect",MidiEventType.ControlChange,channel,-1,0,64,0));}
        }
        public void SynchronizeState(byte[] noteBits,byte[] sustainChannels)
        {
            if(noteBits==null||noteBits.Length!=256||sustainChannels==null||sustainChannels.Length!=16)return;
            Array.Clear(m_ActiveChannels,0,m_ActiveChannels.Length);m_SustainChannels=0;
            for(var channel=0;channel<16;++channel)
            {
                var bit=(ushort)(1<<channel);
                for(var note=0;note<128;++note)if((noteBits[channel*16+(note>>3)]&(1<<(note&7)))!=0)m_ActiveChannels[note]|=bit;
                if(sustainChannels[channel]!=0)m_SustainChannels|=bit;
            }
            IsConnected=true;
        }
        public void RefreshDeviceList()=>DevicesChanged?.Invoke();public bool SelectDevice(int index)=>index==0;public bool ConnectSelectedDevice(){IsConnected=true;return true;}public void Disconnect(){m_Queue.Clear();if(IsConnected)ReleaseAll(ResearchServices.Clock.AbsoluteSeconds);IsConnected=false;}
    }

}
