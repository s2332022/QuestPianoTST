using System;
using System.Collections.Generic;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed class PoseFrameAssembler
    {
        PosePacket[] m_Chunks; bool[] m_Received; long m_Callback=long.MinValue; int m_Count; double m_FirstReceive;
        public long IncompleteFramesDropped{get;private set;}
        public void Reset(){if(m_Chunks!=null&&m_Count<m_Chunks.Length)++IncompleteFramesDropped;m_Chunks=null;m_Received=null;m_Callback=long.MinValue;m_Count=0;m_FirstReceive=0d;}
        public bool Accept(PosePacket packet,double receiveTime,out IReadOnlyList<PosePacket> complete)
        {
            complete=null;if(packet.ChunkCount==0||packet.ChunkIndex>=packet.ChunkCount)return false;
            if(m_Callback!=long.MinValue&&packet.CallbackIndex<m_Callback)return false;
            if(packet.CallbackIndex!=m_Callback){if(m_Chunks!=null&&m_Count<m_Chunks.Length)++IncompleteFramesDropped;m_Callback=packet.CallbackIndex;m_Chunks=new PosePacket[packet.ChunkCount];m_Received=new bool[packet.ChunkCount];m_Count=0;m_FirstReceive=receiveTime;}
            if(packet.ChunkCount!=m_Chunks.Length||m_Received[packet.ChunkIndex])return false;m_Received[packet.ChunkIndex]=true;m_Chunks[packet.ChunkIndex]=packet;++m_Count;
            if(m_Count!=m_Chunks.Length)return false;complete=m_Chunks;m_Chunks=null;m_Received=null;m_Count=0;return true;
        }
        public void Expire(double now,double timeoutSeconds){if(m_Chunks==null||now-m_FirstReceive<timeoutSeconds)return;++IncompleteFramesDropped;m_Chunks=null;m_Received=null;m_Count=0;}
    }
}
