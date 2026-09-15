using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    public static class DistributedBuildInfo
    {
        const string ResourceName = "DistributedBuildInfo";
        static string s_Timestamp;

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
