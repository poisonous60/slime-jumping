Shader "SlimeJumping/Ice Lake"
{
    Properties
    {
        _NearColor("Near Color", Color) = (0.431, 0.447, 0.706, 1)
        _FarColor("Far Color", Color) = (0.353, 0.369, 0.612, 1)
        _NearDistance("Near Distance", Float) = 20
        _FarDistance("Far Distance", Float) = 160
        _StencilRef("Stencil Ref (reflection mask)", Range(0, 255)) = 40
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            // Marks the pixels where the ice surface is actually visible. The reflection
            // shader only paints where this mark survives, so it can never spill past
            // the lake or onto whatever occludes it.
            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _NearColor;
                float4 _FarColor;
                float _NearDistance;
                float _FarDistance;
                float _StencilRef;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float depth = distance(IN.positionWS, _WorldSpaceCameraPos);
                float t = saturate((depth - _NearDistance) / max(0.001, _FarDistance - _NearDistance));
                return half4(lerp(_NearColor.rgb, _FarColor.rgb, t), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
