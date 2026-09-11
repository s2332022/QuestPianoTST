using System.Collections.Generic;
namespace QuestPianoMotion.Research.Distributed
{
    public readonly struct ClockSyncSample
    {
        public readonly double PcT0,QuestQ1,QuestQ2,PcT3,Rtt,Offset; public readonly int Index;
        public ClockSyncSample(double t0,double q1,double q2,double t3,int index){PcT0=t0;QuestQ1=q1;QuestQ2=q2;PcT3=t3;Index=index;Rtt=(t3-t0)-(q2-q1);Offset=((q1-t0)+(q2-t3))*0.5d;}
    }
    public sealed class ClockSynchronizer
    {
        readonly List<ClockSyncSample> m_History=new List<ClockSyncSample>(256); ClockSyncSample m_Selected;
        public IReadOnlyList<ClockSyncSample> History=>m_History; public int SampleCount=>m_History.Count; public bool HasEstimate=>m_History.Count>0;
        public double OffsetSeconds=>HasEstimate?m_Selected.Offset:0d; public double RttSeconds=>HasEstimate?m_Selected.Rtt:0d; public ClockSyncSample Selected=>m_Selected;
        public ClockSyncSample Add(double t0,double q1,double q2,double t3){var sample=new ClockSyncSample(t0,q1,q2,t3,m_History.Count);m_History.Add(sample);if(m_History.Count==1||sample.Rtt<m_Selected.Rtt)m_Selected=sample;return sample;}
        public double QuestToPc(double questTimestamp)=>questTimestamp-OffsetSeconds;
    }
}
