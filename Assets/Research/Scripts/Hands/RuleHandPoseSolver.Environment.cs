using UnityEngine;

namespace QuestPianoMotion.Research
{
    public sealed partial class RuleHandPoseSolver
    {
        public RuleKeyboardGeometry Geometry { get; set; }
        public float ConstraintStrength { get; set; }=1;
        readonly Vector3[] environmentTarget=new Vector3[5], push=new Vector3[5];
        readonly float[] contactWeight=new float[5];
        void PrepareEnvironment(RuleHandPoseSettings settings)
        {
            for(var f=0;f<5;f++)push[f]=Vector3.zero;
            if(!settings.UseEnvironment || ConstraintStrength<=0)return;
            if(settings.nonpenetration && Geometry!=null && Geometry.Ready)
                for(var f=0;f<5;f++)if(valid[f] && FrozenFactors[f]<0)
                {
                    var violation=Penetration(f,p,settings);
                    push[f]+=Geometry.KeyboardToTracking.MultiplyVector(Vector3.up)*Mathf.Min(.01f,violation);
                }
            if(settings.interference)
                for(var f=0;f<5;f++)for(var g=f+1;g<5;g++)if(valid[f] && valid[g])
                    for(var a=0;a<3;a++)for(var b=0;b<3;b++)
                    {
                        Closest(p[f,a],p[f,a+1],p[g,b],p[g,b+1],out var u,out var v);
                        var delta=u-v;var distance=delta.magnitude;var overlap=2*settings.interferenceRadiusMeters-distance;
                        if(overlap<=0)continue;
                        var direction=distance>1e-6f?delta/distance:(raw[f,0]-raw[g,0]).normalized;
                        if(direction.sqrMagnitude<.5f)direction=Vector3.right;
                        var share=FrozenFactors[f]>=0?0:FrozenFactors[g]>=0?1:contactWeight[f]>contactWeight[g]?.2f:contactWeight[g]>contactWeight[f]?.8f:.5f;
                        push[f]+=direction*overlap*share*.35f;push[g]-=direction*overlap*(1-share)*.35f;
                    }
            for(var f=0;f<5;f++)environmentTarget[f]=Vector3.ClampMagnitude(environmentTarget[f]+push[f]*Mathf.Clamp01(ConstraintStrength),settings.fingers[f].maxDisplacementMeters);
        }
        Vector3 EnvironmentPush(int f,RuleHandPoseSettings settings)=>settings.UseEnvironment?environmentTarget[f]:Vector3.zero;
        float Penetration(int f,Vector3[,] points,RuleHandPoseSettings settings)
        {
            if(Geometry==null)return 0;
            var worst=0f;
            // Three sphere samples per bone, including its endpoints; approximation explicitly logged.
            for(var j=0;j<3;j++)for(var k=0;k<=2;k++)
                worst=Mathf.Max(worst,Geometry.Penetration(Vector3.Lerp(points[f,j],points[f,j+1],k*.5f),settings.segmentRadiusMeters,settings.permittedPenetrationMeters));
            return worst;
        }
        void FinishEnvironment(RuleHandPoseSettings settings)
        {
            for(var f=0;f<5;f++)if(valid[f])
            {
                var m=Metrics[f];m.rawPenetration=Penetration(f,raw,settings);m.penetration=Penetration(f,p,settings);
                for(var g=0;g<5;g++)if(g!=f && valid[g])
                    for(var a=0;a<3;a++)for(var b=0;b<3;b++)
                    {
                        Closest(p[f,a],p[f,a+1],p[g,b],p[g,b+1],out var u,out var v);
                        m.interference=Mathf.Max(m.interference,2*settings.interferenceRadiusMeters-Vector3.Distance(u,v));
                        Closest(raw[f,a],raw[f,a+1],raw[g,b],raw[g,b+1],out u,out v);
                        m.rawInterference=Mathf.Max(m.rawInterference,2*settings.interferenceRadiusMeters-Vector3.Distance(u,v));
                    }
                Metrics[f]=m;
                if((settings.UseEnvironment && ((settings.nonpenetration && m.penetration>.0001f) || (settings.interference && m.interference>.0001f))) ||
                    (settings.UseJoints && m.angularViolationDegrees>.1f))Reason="constraints_remaining";
            }
        }
        // Closest points on two finite segments, including parallel and zero-length cases.
        public static void Closest(Vector3 a,Vector3 b,Vector3 c,Vector3 d,out Vector3 u,out Vector3 v)
        {
            var x=b-a;var y=d-c;var r=a-c;
            var xx=Vector3.Dot(x,x);var yy=Vector3.Dot(y,y);var xy=Vector3.Dot(x,y);var xr=Vector3.Dot(x,r);var yr=Vector3.Dot(y,r);
            float s,t;
            if(xx<=1e-12f && yy<=1e-12f){u=a;v=c;return;}
            if(xx<=1e-12f){s=0;t=Mathf.Clamp01(yr/yy);}
            else if(yy<=1e-12f){t=0;s=Mathf.Clamp01(-xr/xx);}
            else
            {
                var denominator=xx*yy-xy*xy;s=denominator>1e-12f?Mathf.Clamp01((xy*yr-xr*yy)/denominator):0;
                t=(xy*s+yr)/yy;
                if(t<0){t=0;s=Mathf.Clamp01(-xr/xx);}else if(t>1){t=1;s=Mathf.Clamp01((xy-xr)/xx);}
            }
            u=a+x*s;v=c+y*t;
        }
    }
}
