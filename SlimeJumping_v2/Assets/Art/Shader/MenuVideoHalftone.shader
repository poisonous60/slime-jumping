// Print-style halftone over the menu background video: screen-space rotated dot grid,
// dot radius follows the luminance of the video sampled at each cell center, gaps fall
// to a dark ink color. Strength 0 = raw video. RawImage vertex color (config tint)
// multiplies the result, so the existing dim/vignette channels keep working.
Shader "SlimeJumping/UI/MenuVideoHalftone"
{
    Properties
    {
        [PerRendererData] _MainTex ("Video", 2D) = "white" {}
        _CellSize ("Cell Size (px)", Range(2, 64)) = 10
        _PatternAngle ("Pattern Angle (deg)", Range(0, 90)) = 45
        _DotGain ("Dot Gain", Range(0.25, 2)) = 1
        _DotSoftness ("Dot Softness", Range(0.01, 0.5)) = 0.12
        _GapColor ("Gap Color", Color) = (0.01, 0.03, 0.02, 1)
        _Strength ("Effect Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _CellSize;
            float _PatternAngle;
            float _DotGain;
            float _DotSoftness;
            fixed4 _GapColor;
            float _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.screen = ComputeScreenPos(o.pos);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 px = (i.screen.xy / i.screen.w) * _ScreenParams.xy;
                float a = radians(_PatternAngle);
                float s = sin(a);
                float c = cos(a);
                float2 g = float2(c * px.x - s * px.y, s * px.x + c * px.y) / _CellSize;
                float2 cellCenter = floor(g) + 0.5;
                float2 centerPx = float2(c * cellCenter.x + s * cellCenter.y,
                                         -s * cellCenter.x + c * cellCenter.y) * _CellSize;

                // The quad is screen-aligned, so uv is affine in px; derivatives give the
                // exact px->uv mapping without wiring the rect transform in.
                float2 centerUv = i.uv + ddx(i.uv) * (centerPx.x - px.x)
                                       + ddy(i.uv) * (centerPx.y - px.y);

                fixed4 cellCol = tex2D(_MainTex, centerUv);
                float lum = saturate(dot(cellCol.rgb, float3(0.299, 0.587, 0.114)));
                // sqrt keeps dot AREA linear in luminance; 0.71 lets bright cells merge.
                float radius = 0.71 * sqrt(lum) * _DotGain;
                float d = length(frac(g) - 0.5);
                float dotMask = 1.0 - smoothstep(radius - _DotSoftness, radius + _DotSoftness, d);

                fixed3 halftone = lerp(_GapColor.rgb, cellCol.rgb, dotMask);
                fixed3 raw = tex2D(_MainTex, i.uv).rgb;
                fixed4 outCol = fixed4(lerp(raw, halftone, _Strength), 1.0);
                return outCol * i.color;
            }
            ENDCG
        }
    }
}
