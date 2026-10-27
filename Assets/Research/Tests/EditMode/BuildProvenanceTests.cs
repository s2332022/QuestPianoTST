using System;
using System.IO;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using System.Reflection;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class BuildProvenanceTests
    {
        [Test]
        public void SidecarUsesCapturedBuildStateAndActualArtifactHash()
        {
            var artifact=Path.Combine(Path.GetTempPath(),"QuestPianoProvenance-"+Guid.NewGuid().ToString("N")+".apk");
            try
            {
                File.WriteAllText(artifact,"abc");
                var builder=Type.GetType("QuestPianoMotion.Research.Editor.PianoDistributedSceneBuilder, QuestPianoMotion.Research.Editor",true);
                builder.GetMethod("WriteBuildProvenance",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{artifact,"quest-scene","Android",
                    new DistributedBuildInfo.BuildMetadata{git_commit_hash="captured-commit",git_dirty="true"},
                    " M captured.cs","captured-build-time"});
                var json=File.ReadAllText(artifact+".provenance.json");
                StringAssert.Contains("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",json);
                StringAssert.Contains("captured-commit",json);
                StringAssert.Contains("captured-build-time",json);
                StringAssert.Contains("captured.cs",json);
                StringAssert.Contains("\"git_dirty\": \"true\"",json);
                Assert.AreEqual("abc",File.ReadAllText(artifact));
            }
            finally
            {
                if(File.Exists(artifact))File.Delete(artifact);
                if(File.Exists(artifact+".provenance.json"))File.Delete(artifact+".provenance.json");
            }
        }
    }
}
