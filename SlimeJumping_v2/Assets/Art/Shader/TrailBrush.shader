// TrailBrush.shader
// Draws a soft radial falloff into a target RenderTexture via Graphics.Blit / GL.QUADS.
// Uses hardware additive blending so repeated stamps on the same pixel accumulate —
// R8 UNORM clamps at 1.0 by construction. A visual saturation cap below 1.0 is applied
// in the ground shader (_TrailMaxMask) rather than in the brush, so repeat-count
// information is preserved in the RT even if the visible tint is capped.
Shader "Hidden/SlimeJumping/TrailBrush"
{
    Properties
    {
        _Strength ("Strength", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "IgnoreProjector"="True" }

        Pass
        {
            Blend One One
            BlendOp Add
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Strength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local      : TEXCOORD0; // expected [-1, 1]
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                // GL.LoadOrtho sets 0..1 ortho; we pass UV-space vertex positions directly.
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.local = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d2 = dot(i.local, i.local);
                float falloff = saturate(1.0 - d2);
                falloff *= falloff;                     // smoother rolloff
                return half4(falloff * _Strength, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
