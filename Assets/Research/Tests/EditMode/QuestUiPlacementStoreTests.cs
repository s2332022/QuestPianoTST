using System;
using System.IO;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;
namespace QuestPianoMotion.Research.Tests
{
    public sealed class QuestUiPlacementStoreTests
    {
        [TestCase("")][TestCase("{}")] [TestCase("broken")]
        [TestCase("{\"version\":9,\"coordinateSpace\":\"World\"}")]
        [TestCase("{\"version\":1,\"coordinateSpace\":\"KeyboardLocal\",\"reference\":\"CalibratedKeyboardRoot\",\"position\":{\"x\":1e99},\"rotation\":{\"w\":1}}")]
        public void InvalidJsonIsRejected(string json) => Assert.That(QuestUiPlacementStore.TryDecode(json,out _),Is.False);
        [TestCase(float.NaN)][TestCase(float.PositiveInfinity)][TestCase(99f)]
        public void InvalidCoordinatesAreRejected(float x)
        {
            var data=new QuestUiPlacementData {position=new Vector3(x,0,0),rotation=Quaternion.identity};
            Assert.That(QuestUiPlacementStore.TryDecode(JsonUtility.ToJson(data),out _),Is.False);
        }
        [Test]
        public void SaveAndRestoreRelativeToChangedOrigin()
        {
            var reference=new GameObject();var panel=new GameObject();var head=new GameObject();
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
            try
            {
                reference.transform.position=new Vector3(3,0,2);head.transform.position=reference.transform.position;
                panel.transform.position=head.transform.position+Vector3.forward;
                QuestUiPlacementStore.Save(path,panel.transform,reference.transform);
                Assert.That(QuestUiPlacementStore.TryLoad(path,out var data),Is.True);
                reference.transform.position+=Vector3.right*10;head.transform.position+=Vector3.right*10;
                Assert.That(QuestUiPlacementStore.TryResolve(data,reference.transform,head.transform,.45f,2.5f,out var pose),Is.True);
                Assert.That(Vector3.Distance(pose.position,head.transform.position+Vector3.forward),Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(pose.rotation,Quaternion.identity),Is.LessThan(.001f));
                reference.transform.rotation=Quaternion.Euler(0,70,0);head.transform.rotation=reference.transform.rotation;
                Assert.That(QuestUiPlacementStore.TryResolve(data,reference.transform,head.transform,.45f,2.5f,out pose),Is.True);
                Assert.That(Vector3.Distance(pose.position,head.transform.position+reference.transform.forward),Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(pose.rotation,reference.transform.rotation),Is.LessThan(.001f));
                Assert.That(QuestUiPlacementStore.TryResolve(data,reference.transform,head.transform,.45f,2.5f,out _,"different-calibration"),Is.False);
                head.transform.position+=Vector3.forward*20;
                Assert.That(QuestUiPlacementStore.TryResolve(data,reference.transform,head.transform,.45f,2.5f,out _),Is.False);
                Assert.That(QuestUiPlacementStore.TryResolve(data,null,head.transform,.45f,2.5f,out _),Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(reference);UnityEngine.Object.DestroyImmediate(panel);UnityEngine.Object.DestroyImmediate(head);if(File.Exists(path))File.Delete(path); }
        }
        [TestCase(0,0,-1,0)] [TestCase(0,0,1,180)] [TestCase(0,0,1,90)]
        public void InvisibleOrInvertedPoseIsRejected(float x,float y,float z,float pitch)
        {
            var root=new GameObject();var head=new GameObject();
            try{var data=new QuestUiPlacementData{position=new Vector3(x,y,z),rotation=Quaternion.Euler(pitch,0,0)};
                Assert.That(QuestUiPlacementStore.TryResolve(data,root.transform,head.transform,.45f,2.5f,out _),Is.False);}
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head);}
        }
    }
}
