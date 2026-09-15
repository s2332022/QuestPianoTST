Shader "QuestPianoMotion/Research Unlit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial) float4 _BaseMap_ST; half4 _BaseColor; CBUFFER_END
            Varyings vert(Attributes IN) { UNITY_SETUP_INSTANCE_ID(IN); Varyings OUT; OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz); OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap); return OUT; }
            half4 frag(Varyings IN) : SV_Target { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor; }
            ENDHLSL
        }
    }
}