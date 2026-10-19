using System.Collections.Generic;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    /// <summary>Rest key top in KeyboardRoot-local metres. White tops exclude black-key footprints.</summary>
    public sealed class PianoKeySurface
    {
        public readonly int Note;
        public readonly bool IsBlack;
        public readonly float Height;
        public readonly List<Rect> Regions = new List<Rect>();
        public PianoKeySurface(int note, bool black, Bounds bounds)
        {
            Note = note; IsBlack = black; Height = bounds.max.y;
            Regions.Add(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
        }
        public void Exclude(Rect cut)
        {
            for (var i = Regions.Count - 1; i >= 0; --i)
            {
                var r = Regions[i];
                var x0 = Mathf.Max(r.xMin, cut.xMin); var x1 = Mathf.Min(r.xMax, cut.xMax);
                var z0 = Mathf.Max(r.yMin, cut.yMin); var z1 = Mathf.Min(r.yMax, cut.yMax);
                if (x0 >= x1 || z0 >= z1) continue;
                Regions.RemoveAt(i);
                Add(r.xMin, r.yMin, x0, r.yMax); Add(x1, r.yMin, r.xMax, r.yMax);
                Add(x0, r.yMin, x1, z0); Add(x0, z1, x1, r.yMax);
            }
        }
        void Add(float x0, float z0, float x1, float z1)
        { if (x1 > x0 && z1 > z0) Regions.Add(Rect.MinMaxRect(x0, z0, x1, z1)); }
        public Vector2 Closest(Vector2 p)
        {
            var best = p; var distance = float.PositiveInfinity;
            foreach (var r in Regions)
            {
                var q = new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
                var d = (q - p).sqrMagnitude;
                if (d < distance) { distance = d; best = q; }
            }
            return best;
        }
        public float PlanarDistance(Vector3 contact) => Vector2.Distance(new Vector2(contact.x, contact.z), Closest(new Vector2(contact.x, contact.z)));
        public Vector3 Target(Vector3 tip, Vector3 tipToContact, float planarTolerance, float below, float above)
        {
            var contact = tip + tipToContact;
            var q = Closest(new Vector2(contact.x, contact.z));
            if (Vector2.Distance(q, new Vector2(contact.x, contact.z)) > planarTolerance)
            { contact.x = q.x; contact.z = q.y; }
            contact.y = Mathf.Clamp(contact.y, Height - below, Height + above);
            return contact - tipToContact;
        }
    }
}
