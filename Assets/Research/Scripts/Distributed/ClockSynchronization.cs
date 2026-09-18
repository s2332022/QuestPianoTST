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
        public void Reset(){m_History.Clear();m_Selected=default;}
        public ClockSyncSample Add(double t0,double q1,double q2,double t3){TryAdd(t0,q1,q2,t3,out var sample);return sample;}
        public bool TryAdd(double t0,double q1,double q2,double t3,out ClockSyncSample sample)
        {
            sample=default;
            if(!IsFinite(t0)||!IsFinite(q1)||!IsFinite(q2)||!IsFinite(t3)||t3<t0||q2<q1)return false;
            var candidate=new ClockSyncSample(t0,q1,q2,t3,m_History.Count);
            if(candidate.Rtt<0d||!IsFinite(candidate.Rtt)||!IsFinite(candidate.Offset))return false;
            sample=candidate;m_History.Add(candidate);if(m_History.Count==1||candidate.Rtt<m_Selected.Rtt)m_Selected=candidate;return true;
        }
        public double QuestToPc(double questTimestamp)=>questTimestamp-OffsetSeconds;
        static bool IsFinite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}
