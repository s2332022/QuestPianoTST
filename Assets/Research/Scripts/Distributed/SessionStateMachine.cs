namespace QuestPianoMotion.Research.Distributed
{
    public sealed class SessionStateMachine
    {
        public DistributedSessionState State{get;private set;}=DistributedSessionState.Idle;
        public bool RequestStart(){if(State!=DistributedSessionState.Idle&&State!=DistributedSessionState.Completed)return false;State=DistributedSessionState.Starting;return true;}
        public bool AcknowledgeStart(bool accepted){if(State!=DistributedSessionState.Starting)return false;State=accepted?DistributedSessionState.Recording:DistributedSessionState.Idle;return accepted;}
        public bool RequestStop(){if(State!=DistributedSessionState.Recording)return false;State=DistributedSessionState.Finalizing;return true;}
        public bool CompleteStop(){if(State!=DistributedSessionState.Finalizing&&State!=DistributedSessionState.Stopping)return false;State=DistributedSessionState.Completed;return true;}
        public bool Abort(){if(State!=DistributedSessionState.Recording&&State!=DistributedSessionState.Finalizing&&State!=DistributedSessionState.Stopping)return false;State=DistributedSessionState.Aborted;return true;}
        public void Fault()=>State=DistributedSessionState.Faulted;
    }
}
