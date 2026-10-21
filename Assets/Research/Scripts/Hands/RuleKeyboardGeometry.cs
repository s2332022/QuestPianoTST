using UnityEngine;

namespace QuestPianoMotion.Research
{
    /// <summary>Cached rest geometry. KeyboardRootLocal metres; animation is never contact ground truth.</summary>
    public sealed class RuleKeyboardGeometry
    {
        readonly Bounds[] bounds=new Bounds[88];
        readonly PianoKeySurface[] surfaces=new PianoKeySurface[128];
        int count;
        Matrix4x4 geometryMatrix;
        public Matrix4x4 TrackingToKeyboard { get; private set; }=Matrix4x4.identity;
        public Matrix4x4 KeyboardToTracking { get; private set; }=Matrix4x4.identity;
        public bool Ready { get; private set; }
        public bool Refresh(VirtualPianoKeyboard keyboard,Transform origin)
        {
            Ready=false;
            if(keyboard==null || origin==null || keyboard.KeyboardRoot==null || keyboard.KeyboardGeometry==null ||
                !MidiTipCorrectionProcessor.Rigid(origin.localToWorldMatrix) || !MidiTipCorrectionProcessor.Rigid(keyboard.KeyboardRoot.localToWorldMatrix))return false;
            TrackingToKeyboard=keyboard.KeyboardRoot.worldToLocalMatrix*origin.localToWorldMatrix;
            KeyboardToTracking=TrackingToKeyboard.inverse;
            var matrix=keyboard.KeyboardRoot.worldToLocalMatrix*keyboard.KeyboardGeometry.localToWorldMatrix;
            if(count!=keyboard.KeyCount || matrix!=geometryMatrix)
            {
                count=0;for(var i=0;i<128;i++)surfaces[i]=null;
                for(var n=keyboard.MinNote;n<=keyboard.MaxNote;n++)
                    if(keyboard.TryGetRestKeyBounds(n,out var b) && keyboard.TryGetKeySurface(n,out var s))
                    {bounds[count++]=b;surfaces[n]=s;}
                geometryMatrix=matrix;
            }
            Ready=count>0;return Ready;
        }
        public PianoKeySurface Surface(int note)=>note>=0 && note<128?surfaces[note]:null;
        public float Penetration(Vector3 trackingPoint,float radius,float permittedDepth)
        {
            if(!Ready)return 0;
            var p=TrackingToKeyboard.MultiplyPoint3x4(trackingPoint);var depth=0f;
            for(var i=0;i<count;i++)
            {
                var b=bounds[i];
                var x=Mathf.Max(Mathf.Max(b.min.x-p.x,0),p.x-b.max.x);var z=Mathf.Max(Mathf.Max(b.min.z-p.z,0),p.z-b.max.z);
                var square=x*x+z*z;if(square>radius*radius)continue;
                var vertical=Mathf.Sqrt(Mathf.Max(0,radius*radius-square));
                depth=Mathf.Max(depth,b.max.y+vertical-permittedDepth-p.y);
            }
            return depth;
        }
    }
}
