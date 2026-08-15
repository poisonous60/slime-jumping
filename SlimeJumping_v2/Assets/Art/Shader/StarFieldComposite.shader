Shader "SlimeJumping/Star Field Composite"
{
    // Draws StarFieldBloom's offscreen target back into the scene. That target already
    // holds the stars with URP Bloom applied, rendered by a camera on a renderer without
    // TranslucentImageBlurSource, so its HDR survived long enough for the bloom threshold
    // to see the cores. Additive over a black target, so only the stars contribute.
    // Sampled in screen space: the star camera shares the scene camera's pose and FOV.
    Properties
    {
        _MainTex ("Star Target", 2D) = "black" {}
        _Intensity ("Intensity", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True"
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

            sampler2D _MainTex;
            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                return float4(tex2D(_MainTex, uv).rgb * _Intensity, 1);
            }
            ENDCG
        }
    }
}
