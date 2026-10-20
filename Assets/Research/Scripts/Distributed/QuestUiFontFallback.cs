using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace QuestPianoMotion.Research.Distributed
{
    // Runtime fallback membership only. Never clones/destroys the imported Latin atlas or material.
    static class QuestUiFontFallback
    {
        static TMP_FontAsset s_Japanese;
        static TMP_FontAsset s_Primary;
        static List<TMP_FontAsset> s_Previous;
        static int s_Users;
        public static TMP_FontAsset Acquire(TMP_FontAsset primary)
        {
            if(s_Users==0)
            {
                var source=Resources.Load<Font>("QuestUi/NotoSansJP");
                if(source==null)return null;
                s_Japanese=TMP_FontAsset.CreateFontAsset(source);
                s_Primary=primary;s_Previous=primary.fallbackFontAssetTable;
                var fallbacks=s_Previous!=null?new List<TMP_FontAsset>(s_Previous):new List<TMP_FontAsset>();
                fallbacks.Add(s_Japanese);primary.fallbackFontAssetTable=fallbacks;
            }
            ++s_Users;return s_Japanese;
        }
        public static void Release()
        {
            if(s_Users==0||--s_Users!=0)return;
            if(s_Primary!=null)s_Primary.fallbackFontAssetTable=s_Previous;
            if(s_Japanese!=null)Object.Destroy(s_Japanese);
            s_Primary=null;s_Previous=null;s_Japanese=null;
        }
    }
}
