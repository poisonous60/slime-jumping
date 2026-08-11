// Cloth_Unlit — URP unlit cloth for ClothField (#16 passable cloth).
//
// Two-sided (Cull Off) so rotated curtains read from either side without
// duplicating geometry. Alpha-blended with a vertex-color mask for the pin
// row: vertex color R = 0 at pinned nodes, 1 elsewhere. Pinned row fades
// slightly more transparent so the pin line reads visually.
//
// Ambient shimmer: a time-scrolled gradient noise adds a low-amplitude
// brightness variation across UVs, giving the static cloth a subtle sense
// of liveliness even when the sim is idle.

Shader "SlimeJumping/Cloth_Unlit"
{
    Properties
    {
        [HDR] _BaseColor     ("Base Color",                Color) = (0.75, 0.6, 0.7, 0.9)
        _PinAlphaFloor       ("Pin Row Alpha Floor (R=0)", Range(0, 1)) = 0.85
        _ShimmerAmplitude    ("Shimmer Amplitude",         Range(0, 0.3)) = 0.05
        _ShimmerScale        ("Shimmer UV Scale",          Range(0.1, 20)) = 3.0
        _ShimmerSpeed        ("Shimmer Speed",             Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ClothForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _PinAlphaFloor;
                float  _ShimmerAmplitude;
                float  _ShimmerScale;
                float  _ShimmerSpeed;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            // Cheap 2D gradient noise — hashed dot, good enough for ambient shimmer.
            float Hash12(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float GradientNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash12(i);
                float b = Hash12(i + float2(1, 0));
                float c = Hash12(i + float2(0, 1));
                float d = Hash12(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float2 uv = IN.uv;
                float t = _Time.y * _ShimmerSpeed;
                float n = GradientNoise(uv * _ShimmerScale + float2(t, t * 0.7));

                half4 col = _BaseColor;
                col.rgb *= 1.0 + (n - 0.5) * _ShimmerAmplitude;

                // Vertex color R channel — 0 on pinned row, 1 elsewhere.
                half mask = saturate(IN.color.r);
                half alphaScale = lerp(_PinAlphaFloor, 1.0, mask);
                col.a *= alphaScale;

                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
