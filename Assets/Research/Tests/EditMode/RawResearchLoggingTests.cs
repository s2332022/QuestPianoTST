using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;
using QuestPianoMotion.Research.Distributed;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class RawResearchLoggingTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void TrackingWorldKeyboardTransformCases(int kind)
        {
            var t=kind==1?new Vector3(2,3,4):Vector3.zero;
            var r=kind==2?Quaternion.Euler(0,90,0):Quaternion.identity;
            var s=kind==3?new Vector3(2,3,4):Vector3.one;
            var m=Matrix4x4.TRS(t,r,s);var p=new Vector3(1,2,3);
            var world=QuestRawSessionLog.ToWorld(m,p);
            var expected=kind==1?new Vector3(3,5,7):kind==2?new Vector3(3,2,-1):kind==3?new Vector3(2,6,12):p;
            Assert.That(Vector3.Distance(world,expected),Is.LessThan(1e-5));
            Assert.That(Vector3.Distance(QuestRawSessionLog.ToKeyboard(m,world),p),Is.LessThan(1e-5));
        }
        [Test] public void SharedClockNeverMovesBackwards()
        {
            var clock=ResearchServices.Clock;var previous=clock.AbsoluteSeconds;
            for(var i=0;i<1000;i++){var now=clock.AbsoluteSeconds;Assert.That(now,Is.GreaterThanOrEqualTo(previous));previous=now;}
        }
        [Test] public void SessionPreservesRawAndMidiAndReferencesChangingSnapshots()
        {
            var go=new GameObject("Research log test");var origin=new GameObject("Origin");
            var previous=ResearchServices.TrackingOrigin;
            // Internal setter remains protected outside production assembly.
            typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{origin.transform});
            var keyboard=go.AddComponent<VirtualPianoKeyboard>();
            typeof(VirtualPianoKeyboard).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(keyboard,null);
            var log=new QuestRawSessionLog();
            var id=Guid.NewGuid();string path=null;
            try
            {
                origin.transform.position=new Vector3(2,3,4);
                Assert.That(log.Begin(id,keyboard,null),Is.True);path=log.DirectoryPath;
                var f=new HandPoseFrame(1){AbsoluteTimeSeconds=10,UnityFrame=7,CallbackIndex=9,UpdateType=XRHandSubsystem.UpdateType.Dynamic,LeftTracked=true};
                f.LeftJoints[0]=new HandJointPose{JointId=XRHandJointID.IndexTip,PoseValid=true,Pose=new Pose(new Vector3(.1f,.2f,.3f),Quaternion.identity)};
                var original=f.LeftJoints[0].Pose;
                log.RecordFrame(f,keyboard);
                origin.transform.position+=Vector3.right;f.AbsoluteTimeSeconds=11;f.CallbackIndex++;log.RecordFrame(f,keyboard);
                Assert.That(f.LeftJoints[0].Pose.position,Is.EqualTo(original.position));
                var midi=new MidiMessage(22.5,7,"test",MidiEventType.NoteOn,1,60,0,-1,-1);
                log.RecordMidi("BLE",midi,23.5,8191,4,false,keyboard);
                log.RecordMidi("UDP",midi,24.5,-1,0,false,keyboard);
                log.End("test_complete");Assert.That(log.LastError,Is.Empty);
                var metadata=File.ReadAllText(Path.Combine(path,"session_metadata.json"));
                foreach(var field in new[]{"protocol_version","calibration_snapshot","tracking_origin_snapshot","keyboard_root_snapshot","local_to_world","runtime_versions","timing","git_branch"})Assert.That(metadata,Does.Contain("\""+field+"\""));
                var lines=File.ReadAllLines(Path.Combine(path,"transform_snapshots.jsonl"));Assert.That(lines.Length,Is.EqualTo(2));
                var first=JsonUtility.FromJson<QuestRawSessionLog.Snapshot>(lines[0]);var second=JsonUtility.FromJson<QuestRawSessionLog.Snapshot>(lines[1]);
                Assert.That(first.tracking_origin.position,Is.EqualTo(new Vector3(2,3,4)));
                Assert.That(second.snapshot_id,Is.EqualTo(1));Assert.That(first.keys.Length,Is.EqualTo(88));
                var raw=File.ReadAllLines(Path.Combine(path,"quest_hand_raw.csv"));Assert.That(raw.Length,Is.EqualTo(5));
                var left=raw[1].Split(',');var right=raw[2].Split(',');Assert.That(left[1],Is.EqualTo(right[1]));Assert.That(float.Parse(left[11],System.Globalization.CultureInfo.InvariantCulture),Is.EqualTo(original.position.x));
                var events=File.ReadAllLines(Path.Combine(path,"quest_midi_research.csv"));
                Assert.That(events[1],Does.Contain(",BLE,23.5,22.5,8191,4,7,NoteOff,"));
                Assert.That(events[2],Does.Contain(",UDP,24.5,22.5,,0,7,NoteOff,"));
                Assert.That(File.ReadAllText(Path.Combine(path,"research_recording_summary.json")),Does.Contain("\"writer_closed\":true"));
            }
            finally
            {
                log.Dispose();UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(origin);
                typeof(ResearchServices).GetProperty("TrackingOrigin").GetSetMethod(true).Invoke(null,new object[]{previous});
                if(path!=null&&Directory.Exists(path))Directory.Delete(path,true);
            }
        }
    }
}
