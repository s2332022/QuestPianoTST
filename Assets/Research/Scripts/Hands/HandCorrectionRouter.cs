using System;

namespace QuestPianoMotion.Research
{
    /// <summary>Old algorithm remains untouched and is the default. Only one path writes the display.</summary>
    public sealed class HandCorrectionRouter : IHandPoseProcessor
    {
        readonly MidiTipCorrectionProcessor legacy;
        readonly RuleHandPoseSettings settings;
        readonly Func<bool> remote;
        HandCorrectionMethod previous;
        public RuleHandPoseProcessor New { get; }
        public string ExecutedMethod { get; private set; }="Legacy";
        public event Action<HandPoseFrame,HandPoseFrame> Evaluated;
        public HandCorrectionRouter(MidiTipCorrectionProcessor legacy,RuleHandPoseProcessor processor,RuleHandPoseSettings settings,Func<bool> remote=null)
        {this.legacy=legacy;New=processor;this.settings=settings;this.remote=remote;previous=settings.method;}
        void SynchronizeMethod()
        {if(previous!=settings.method){New.Reset("method_changed");legacy.HardReset("method_changed");previous=settings.method;}}
        public void Process(HandPoseFrame raw,HandPoseFrame display)
        {
            SynchronizeMethod();
            if(remote!=null && remote())
            {New.Reset("remote_display_selected");legacy.Process(raw,display);ExecutedMethod="PcDisplayPose";}
            else if(settings.method==HandCorrectionMethod.Legacy)
            {New.Reset("legacy_selected");legacy.Process(raw,display);ExecutedMethod="Legacy";}
            else {New.Process(raw,display);ExecutedMethod=settings.method.ToString();}
            Evaluated?.Invoke(raw,display);
        }
        public void ObserveMidi(string source,MidiMessage m,double received,double observed,long generation=0,bool synthetic=false)
        {SynchronizeMethod();legacy.ObserveMidi(source,m,received,observed,generation,synthetic);New.ObserveMidi(source,m,received,observed,generation,synthetic);}
        public void DisconnectSource(string source,double now){legacy.DisconnectSource(source,now);New.DisconnectSource(source,now);}
        public void SynchronizeUdpOwnership(byte[] bits,double now){legacy.SynchronizeUdpOwnership(bits,now);New.SynchronizeUdpOwnership(bits,now);}
        public void EndSession(double now){legacy.EndSession(now);New.EndSession(now);}
        public void Watchdog(double now){legacy.Watchdog(now);if(settings.method!=HandCorrectionMethod.Legacy)New.Watchdog(now);}
    }
}
