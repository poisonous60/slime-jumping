// SDF glyph skin for the menu letter jelly: samples the TMP font atlas directly on a
// deformable world-space mesh, so the letter look (crisp SDF edge + TitleGlow-style
// outer glow) survives vertex deformation without TMP's packed vertex channels.
// Edge threshold must match the bake iso so the visual silhouette equals the collider.
// Single pass, premultiplied alpha (URP draws only the first pass).
Shader "SlimeJumping/UI/MenuGlyphJelly"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1, 1, 1, 1)
        _EdgeThreshold ("Edge Threshold", Range(0.05, 0.95)) = 0.5
        _GlowColor ("Glow Color", Color) = (0.45, 0.9, 0.65, 0.85)
        _GlowOuter ("Glow Outer", Range(0, 1)) = 0.35
        _GlowInner ("Glow Inner", Range(0, 1)) = 0.05
        _GlowPower ("Glow Power", Range(0.1, 4)) = 0.75
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _FaceColor;
            float _EdgeThreshold;
            fixed4 _GlowColor;
            float _GlowOuter;
            float _GlowInner;
            float _GlowPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float sd = tex2D(_MainTex, i.uv).a;
                float aa = fwidth(sd) * 0.75 + 1e-4;
                float face = smoothstep(_EdgeThreshold - aa, _EdgeThreshold + aa, sd);

                // TMP 식 글로우: 엣지에서 피크, 안팎 비대칭 폭으로 양측 감쇠 — 평탄 밴드 금지.
                float d = sd - _EdgeThreshold;
                float width = lerp(_GlowOuter, _GlowInner, step(0.0, d)) * 0.5;
                float glow = pow(1.0 - saturate(abs(d) / max(width, 1e-4)), _GlowPower) * _GlowColor.a;

                float faceA = face * _FaceColor.a;
                float glowA = glow * (1.0 - face);
                float3 rgb = _FaceColor.rgb * faceA + _GlowColor.rgb * glowA;
                return fixed4(rgb, saturate(faceA + glowA));
            }
            ENDCG
        }
    }
}
