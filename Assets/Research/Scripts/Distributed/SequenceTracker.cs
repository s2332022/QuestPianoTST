namespace QuestPianoMotion.Research.Distributed
{
    public readonly struct SequenceObservation
    {
        public readonly bool Duplicate, OutOfOrder; public readonly uint Missing;
        public SequenceObservation(bool duplicate, bool outOfOrder, uint missing) { Duplicate=duplicate; OutOfOrder=outOfOrder; Missing=missing; }
    }
    public sealed class SequenceTracker
    {
        bool m_HasValue; uint m_Highest; ulong m_Received, m_Missing, m_Duplicates, m_OutOfOrder;
        public ulong Received=>m_Received; public ulong Missing=>m_Missing; public ulong Duplicates=>m_Duplicates; public ulong OutOfOrder=>m_OutOfOrder;
        public double LossRatio=>m_Received+m_Missing==0?0d:(double)m_Missing/(m_Received+m_Missing);
        public void Reset(){m_HasValue=false;m_Highest=0;m_Received=0;m_Missing=0;m_Duplicates=0;m_OutOfOrder=0;}
        public SequenceObservation Observe(uint sequence)
        {
            ++m_Received; if(!m_HasValue){m_HasValue=true;m_Highest=sequence;return default;}
            if(sequence==m_Highest){++m_Duplicates;return new SequenceObservation(true,false,0);}
            if(IsNewer(sequence,m_Highest)){var distance=sequence-m_Highest;var missing=distance>1?distance-1:0;m_Missing+=missing;m_Highest=sequence;return new SequenceObservation(false,false,missing);}
            ++m_OutOfOrder;return new SequenceObservation(false,true,0);
        }
        public static bool IsNewer(uint candidate,uint current)=>unchecked((int)(candidate-current))>0;
    }
}
