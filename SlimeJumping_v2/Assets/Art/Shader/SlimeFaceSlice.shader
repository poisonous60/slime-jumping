// Unlit URP sprite shader for the slime FACE (eyes + mouth), with the same wound clip
// as the body — but in WORLD space, because the face sprites do not ride the body mesh's
// in-shader deform. SlimeSplitSequence maps each wound's rest-OS plane through the body's
// current transform every frame and pushes the resulting world planes here via MPB.
//
// A fragment is discarded if it is on the removed side of ANY active wound plane, so an eye
// on the cut-away part of the slime vanishes and returns as the wound heals. Default
// _SliceCountWS = 0 → clip is a no-op and the face renders as a plain alpha sprite.
Shader "SlimeJumping/SlimeFaceSlice"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color   ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define SLICE_MAX 3
            float  _SliceCountWS;                 // active wound count (0 = no clip)
            float4 _SlicePlaneWS[SLICE_MAX];      // xyz = world normal (removed side), w = plane distance d
            float4 _SliceBottomGuardWS;           // xyz = world up normal, w = d; keep (never clip) where dot(posWS,n) < d

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            half4 _Color;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            void SliceClipWS(float3 posWS)
            {
                int n = (int)(_SliceCountWS + 0.5);
                if (n <= 0) return;
                // Ground-contact band never clipped (matches the body's bottom guard).
                if (dot(posWS, _SliceBottomGuardWS.xyz) < _SliceBottomGuardWS.w) return;
                [unroll]
                for (int i = 0; i < SLICE_MAX; i++)
                {
                    if (i >= n) break;
                    float3 nrm = _SlicePlaneWS[i].xyz;
                    float  d   = _SlicePlaneWS[i].w;
                    clip(-(dot(posWS, nrm) - d));   // removed side (> d) → discard
                }
            }

            half4 frag (Varyings IN) : SV_Target
            {
                SliceClipWS(IN.positionWS);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
