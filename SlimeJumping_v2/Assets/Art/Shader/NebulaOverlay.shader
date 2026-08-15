Shader "SlimeJumping/Nebula Overlay"
{
    // Farland Skies "Nebula One" composition ported off its skybox shader onto a
    // camera-facing quad, because swapping RenderSettings.skybox collides with
    // SkyGradientTransition (tried and dropped in an earlier session). Sampling is by view
    // ray, so the mapping matches a skybox without being one. Two authored cubemaps carry
    // the shape: density says where the cloud is, diffusion's channels act as three
    // independent detail fields, and four tints layer over them at different density
    // thresholds. Additive rather than Farland's lerp, since this draws on the StarField
    // layer and reaches the scene through StarFieldBloom's additive composite — which also
    // means tints pushed above 1 are the only part that blooms. The two rotation matrices
    // are driven by camera height at different rates, so climbing travels through the cloud
    // and the layers shear against each other instead of moving as one sheet.
    Properties
    {
        [NoScaleOffset] _DensityCube ("Density Cubemap", Cube) = "grey" {}
        [NoScaleOffset] _DiffusionCube ("Diffusion Cubemap", Cube) = "grey" {}

        _DensityThresholdLow ("Density Threshold Low", Range(0, 1)) = 0.4
        _DensityThresholdHigh ("Density Threshold High", Range(0, 1)) = 0.8
        _RipplesDistortion ("Ripples Distortion", Vector) = (0.25, 0, 0, 0)

        [HDR] _AmbientTint ("Ambient Tint", Color) = (0, 0.627, 1, 0.027)
        [HDR] _BasementTint ("Basement Tint", Color) = (0, 0.627, 1, 0.498)
        [HDR] _RipplesTint1 ("Ripples 1 Tint", Color) = (0, 0.627, 1, 0.259)
        [HDR] _RipplesTint2 ("Ripples 2 Tint", Color) = (0.318, 0.149, 0.337, 0.318)

        _Exposure ("Exposure", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "Queue"="Transparent-100" "IgnoreProjector"="True"
        }

        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            ColorMask RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _DensityCube;
            samplerCUBE _DiffusionCube;
            float _DensityThresholdLow, _DensityThresholdHigh, _Exposure;
            float3 _RipplesDistortion;
            float4 _AmbientTint, _BasementTint, _RipplesTint1, _RipplesTint2;
            float4x4 _DensityRotation;
            float4x4 _DiffusionRotation;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = world - _WorldSpaceCameraPos;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.viewDir);
                float3 ddir = mul((float3x3)_DensityRotation, dir);
                float3 fdir = mul((float3x3)_DiffusionRotation, dir);

                float4 diffusion = texCUBE(_DiffusionCube, fdir);
                float density = texCUBE(_DensityCube, ddir).r;

                float lo = _DensityThresholdLow;
                // Independent sliders, so order them: an inverted or zero-width pair makes every gate undefined.
                float hi = max(_DensityThresholdHigh, lo + 1e-4);
                float3 col = 0;

                // Thin edges of the cloud. Inverted as 1-smoothstep rather than reversed edges, which HLSL leaves undefined.
                float a = diffusion.g * _AmbientTint.a * (1.0 - smoothstep(lo, hi, density));
                col += _AmbientTint.rgb * a;

                // Dense body.
                a = 0.5 * (diffusion.g + diffusion.b) * _BasementTint.a * smoothstep(lo, hi, density);
                col += _BasementTint.rgb * a;

                // Two ripple passes read the same fields through a self-distorted offset, which is what breaks up the banding.
                float3 off = _RipplesDistortion.xyz * diffusion.b * float3(0.75, -0.75, 0.75);
                a = texCUBE(_DiffusionCube, fdir + off).r * _RipplesTint1.a
                  * smoothstep(lo, hi, texCUBE(_DensityCube, ddir + off).r);
                col += _RipplesTint1.rgb * a;

                off = _RipplesDistortion.zyx * diffusion.g;
                a = texCUBE(_DiffusionCube, fdir + off).r * _RipplesTint2.a
                  * smoothstep(lo, hi, texCUBE(_DensityCube, ddir + off).r);
                col += _RipplesTint2.rgb * a;

                return float4(col * _Exposure, 1);
            }
            ENDCG
        }
    }
}
