using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class DistributedBuildMetadataRestoreTests
    {
        string path;

        [SetUp]
        public void SetUp()
        {
            path="Assets/Research/Resources/BuildMetadataRestoreTest-"+Guid.NewGuid().ToString("N")+".json";
        }

        [TearDown]
        public void TearDown()
        {
            if(File.Exists(path))AssetDatabase.DeleteAsset(path);
        }

        [Test]
        public void ExistingMetadata_RestoresOriginalBytesAndAsset()
        {
            var original=Encoding.UTF8.GetBytes("{\"original\":true}");
            File.WriteAllBytes(path,original);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);

            var previous=ReadExistingMetadata();
            File.WriteAllText(path,"{\"build\":true}");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            RestoreMetadata(previous);

            Assert.That(File.ReadAllBytes(path),Is.EqualTo(original));
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text,Is.EqualTo(Encoding.UTF8.GetString(original)));
        }

        [Test]
        public void MissingMetadata_CanEnterBuildAndLeavesNoAsset()
        {
            var previous=ReadExistingMetadata();
            Assert.That(previous,Is.Null);
            File.WriteAllText(path,"{\"build\":true}");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            RestoreMetadata(previous);

            Assert.That(File.Exists(path),Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(path),Is.Null);
        }

        byte[] ReadExistingMetadata()=>(byte[])BuilderMethod("ReadExistingBuildMetadata").Invoke(null,new object[]{path});
        void RestoreMetadata(byte[] previous)=>BuilderMethod("RestoreBuildMetadata").Invoke(null,new object[]{path,previous});
        static MethodInfo BuilderMethod(string name)
        {
            var builder=Type.GetType("QuestPianoMotion.Research.Editor.PianoDistributedSceneBuilder, QuestPianoMotion.Research.Editor",true);
            return builder.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
        }
    }
}
