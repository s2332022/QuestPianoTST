using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Hands;
using QuestPianoMotion.Research.Distributed;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RawResearchRuntimeTests
    {
        [UnityTest]
        public IEnumerator LocalSessionRecordsBleAndRawIndependentlyOfUdpAndKeyboard()
        {
            var origin=new GameObject("Raw research origin");
            var previous=ResearchServices.TrackingOrigin;
            typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{origin.transform});
            var go=new GameObject("Raw research runtime",typeof(VirtualPianoKeyboard),typeof(XRHandPoseProvider),typeof(BleMidiInput));
            var settings=go.AddComponent<DistributedSettings>();settings.questReceivePort=0;
            var client=go.AddComponent<DistributedQuestClient>();var keyboard=go.GetComponent<VirtualPianoKeyboard>();
            var ble=go.GetComponent<BleMidiInput>();string path=null;
            try
            {
                yield return null;
                client.StopNetwork();
                Assert.That(client.BeginLocalResearchSession(),Is.True);path=client.ResearchSessionPath;
                Assert.That(client.BeginLocalResearchSession(),Is.False);
                var frame=new HandPoseFrame(1){AbsoluteTimeSeconds=ResearchServices.Clock.AbsoluteSeconds,CallbackIndex=1,UpdateType=XRHandSubsystem.UpdateType.Dynamic,LeftTracked=true};
                frame.LeftJoints[0]=new HandJointPose{JointId=XRHandJointID.IndexTip,PoseValid=true,Pose=new Pose(new Vector3(.1f,.2f,.3f),Quaternion.identity)};
                typeof(DistributedQuestClient).GetMethod("OnRawFrame",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(client,new object[]{frame});
                var session=(BleMidiSession)typeof(BleMidiInput).GetField("m_Session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ble);
                session.Begin(7,"BLE test");
                session.Enqueue(new BleMidiSession.Sample(7,0,123456789,0x90,60,100,8191));session.Flush();
                Assert.That(keyboard.State.IsPressed(60),Is.False,"Research observer must not alter UDP state");
                Assert.That(client.ResearchRecording,Is.True);
                client.StopNetwork();Assert.That(client.ResearchRecording,Is.True,"Local logging survives PC disconnect");
                client.EndLocalResearchSession();Assert.That(client.ResearchRecording,Is.False);
                Assert.That(client.ResearchLogError,Is.Empty);
                Assert.That(File.ReadAllLines(Path.Combine(path,"quest_hand_raw.csv")).Length,Is.EqualTo(3));
                Assert.That(File.ReadAllText(Path.Combine(path,"quest_midi_research.csv")),Does.Contain(",8191,7,1,NoteOn,1,60,100,"));
            }
            finally
            {
                client.EndLocalResearchSession();UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(origin);
                typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{previous});
                if(path!=null&&Directory.Exists(path))Directory.Delete(path,true);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator HostControlsAndUdpReceiveTimeAreRecordedWithoutChangingKeyboard()
        {
            using var peer=new UdpTransport();peer.Start(0);
            int port;using(var probe=new UdpTransport()){probe.Start(0);port=probe.LocalPort;}
            var origin=new GameObject("UDP research origin");var previous=ResearchServices.TrackingOrigin;
            typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{origin.transform});
            var go=new GameObject("UDP research",typeof(VirtualPianoKeyboard),typeof(XRHandPoseProvider));
            var settings=go.AddComponent<DistributedSettings>();settings.pcIpAddress="127.0.0.1";settings.questReceivePort=port;settings.pcReceivePort=peer.LocalPort;
            var client=go.AddComponent<DistributedQuestClient>();var keyboard=go.GetComponent<VirtualPianoKeyboard>();string path=null;
            try
            {
                yield return null;
                var instance=(uint)typeof(DistributedQuestClient).GetField("m_InstanceId",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(client);
                var id=Guid.NewGuid();var bytes=new byte[NetworkProtocolV1.MaximumDatagramBytes];var endpoint=new IPEndPoint(IPAddress.Loopback,port);
                var n=NetworkProtocolV1.WriteStartupAck(bytes,1,777,id,instance);Assert.That(peer.Send(bytes,n,endpoint),Is.True);
                var deadline=Time.realtimeSinceStartup+2f;while(!client.PcConnected&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(client.PcConnected,Is.True);
                n=NetworkProtocolV1.WriteSessionControl(bytes,2,778,id,SessionCommand.Start,1);peer.Send(bytes,n,endpoint);
                deadline=Time.realtimeSinceStartup+2f;while(!client.ResearchRecording&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(client.ResearchRecording,Is.True);path=client.ResearchSessionPath;
                var message=new MidiMessage(123.25,5,"PC",MidiEventType.NoteOn,1,60,100,-1,-1);
                n=NetworkProtocolV1.WriteMidi(bytes,3,123.25,id,in message,0);var before=ResearchServices.Clock.AbsoluteSeconds;peer.Send(bytes,n,endpoint);
                deadline=Time.realtimeSinceStartup+2f;while(!keyboard.State.IsPressed(60)&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(keyboard.State.IsPressed(60),Is.True);
                var after=ResearchServices.Clock.AbsoluteSeconds;
                n=NetworkProtocolV1.WriteSessionControl(bytes,4,779,id,SessionCommand.Stop,2);peer.Send(bytes,n,endpoint);
                deadline=Time.realtimeSinceStartup+2f;while(client.ResearchRecording&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(client.ResearchRecording,Is.False);Assert.That(client.ResearchLogError,Is.Empty);
                var rows=File.ReadAllLines(Path.Combine(path,"quest_midi_research.csv"));Assert.That(rows.Length,Is.EqualTo(2));
                var columns=rows[1].Split(',');Assert.That(columns[1],Is.EqualTo("UDP"));Assert.That(columns[3],Is.EqualTo("123.25"));
                var receive=double.Parse(columns[2],System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(receive,Is.GreaterThanOrEqualTo(before).And.LessThanOrEqualTo(after));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(origin);
                typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{previous});
                if(path!=null&&Directory.Exists(path))Directory.Delete(path,true);
            }
            yield return null;
        }

    }
}
