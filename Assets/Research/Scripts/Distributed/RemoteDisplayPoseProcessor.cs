using UnityEngine.XR.Hands;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed class RemoteDisplayPoseProcessor : IHandPoseProcessor
    {
        readonly IdentityHandPoseProcessor m_Identity=new IdentityHandPoseProcessor();readonly HandPoseFrame m_Remote;
        bool[] m_Chunks;long m_Callback=long.MinValue;int m_Received;bool m_Ready;
        public bool UseRemote;public bool Connected;
        public void Reset(){m_Chunks=null;m_Callback=long.MinValue;m_Received=0;m_Ready=false;}
        public RemoteDisplayPoseProcessor(int jointCount){m_Remote=new HandPoseFrame(jointCount);}
        public void Accept(PosePacket p)
        {
            if(p.CallbackIndex!=m_Callback){m_Callback=p.CallbackIndex;m_Received=0;m_Ready=false;m_Chunks=new bool[p.ChunkCount];m_Remote.AbsoluteTimeSeconds=p.Header.SenderTimestamp;m_Remote.UnityFrame=p.UnityFrame;m_Remote.CallbackIndex=p.CallbackIndex;m_Remote.UpdateType=(XRHandSubsystem.UpdateType)p.UpdateType;m_Remote.SuccessFlags=(XRHandSubsystem.UpdateSuccessFlags)p.SuccessFlags;m_Remote.LeftTracked=p.LeftTracked;m_Remote.RightTracked=p.RightTracked;m_Remote.LeftRootPose=p.LeftRoot;m_Remote.RightRootPose=p.RightRoot;}
            if(p.ChunkIndex>=m_Chunks.Length||m_Chunks[p.ChunkIndex])return;m_Chunks[p.ChunkIndex]=true;++m_Received;
            for(var i=0;i<p.Joints.Count;++i){var j=p.Joints[i];var target=j.Hand==0?m_Remote.LeftJoints:m_Remote.RightJoints;var index=(int)j.JointId-(int)XRHandJointID.BeginMarker;if(index<0||index>=target.Length)continue;target[index]=new HandJointPose{JointId=(XRHandJointID)j.JointId,PoseValid=j.PoseValid,TrackingState=(XRHandJointTrackingState)j.TrackingState,Pose=new UnityEngine.Pose(j.Position,j.Rotation)};}
            m_Ready=m_Received==m_Chunks.Length;
        }
        public void Process(HandPoseFrame raw,HandPoseFrame display){if(UseRemote&&Connected&&m_Ready)m_Identity.Process(m_Remote,display);else m_Identity.Process(raw,display);}
    }
}
