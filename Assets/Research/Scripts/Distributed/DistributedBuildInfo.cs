using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    public static class DistributedBuildInfo
    {
        const string ResourceName = "DistributedBuildInfo";
        const string MetadataResourceName = "DistributedBuildMetadata";
        static string s_Timestamp;
        static BuildMetadata s_Metadata;

        [System.Serializable]
        public sealed class BuildMetadata
        {
            public string git_commit_hash;
            public string git_dirty;
            public string xr_hands_version;
            public string xr_interaction_toolkit_version;
            public string openxr_version;
            public string meta_openxr_version;
            public string input_system_version;
        }

        public static BuildMetadata Metadata
        {
            get
            {
                if (s_Metadata != null) return s_Metadata;
                var asset = Resources.Load<TextAsset>(MetadataResourceName);
                try { s_Metadata = asset != null ? JsonUtility.FromJson<BuildMetadata>(asset.text) : null; }
                catch (System.Exception) { s_Metadata = null; }
                return s_Metadata ?? (s_Metadata = new BuildMetadata());
            }
        }

        public static string TimestampUtc
        {
            get
            {
                if (s_Timestamp != null)
                    return s_Timestamp;
                var asset = Resources.Load<TextAsset>(ResourceName);
                s_Timestamp = asset != null ? asset.text.Trim() : "Unavailable";
                return s_Timestamp;
            }
        }

        public static string Identifier => Application.version + "/" + Application.buildGUID + "/" + TimestampUtc;

        public static uint Fnv1a(string value)
        {
            uint hash = 2166136261;
            for (var i = 0; i < (value?.Length ?? 0); ++i)
            {
                hash ^= value[i];
                hash *= 16777619;
            }
            return hash;
        }
    }
}
