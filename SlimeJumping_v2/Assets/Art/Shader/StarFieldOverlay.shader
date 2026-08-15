Shader "SlimeJumping/Star Field Overlay"
{
    // Binary Lunar starfield (youtube wSM-l2T8wOk): two gradient-noise fields
    // counter-scrolled over time, saturate+power each, multiplied together,
    // tinted by a vertical HDR color gradient. Rendered additively on a
    // camera-facing quad. The glow around each star is URP's own Bloom, run by
    // StarFieldBloom on a private camera: light spill is a spatial effect, so no
    // per-pixel reshaping of this noise can stand in for it, and the main camera cannot
    // supply it because TranslucentImageBlurSource drops its color buffer to 8-bit.
    // Keep the HDR colors well above 1 or nothing clears the bloom threshold.
    // The two fields sit at a fixed domain offset: same domain
    // means they coincide while the scroll is still near zero, and the whole field
    // lights up for the first ~100s of play. Every noise cell hashes to its own birth threshold, so
    // raising _Reveal lights up more stars instead of brightening the same ones.
    // Sampling is in screen space, not the quad's mesh UV: the quad is sized from
    // the camera FOV, which Cinemachine blends per vcam, so mesh UV made the whole
    // field re-roll and change count whenever the FOV moved. _Reveal comes per-frame
    // from StarFieldOverlay via MaterialPropertyBlock, the rest are look knobs.
    Properties
    {
        _StarsScale ("Stars Scale", Float) = 100
        _BlinkSpeed ("Blink Speed", Float) = 0.002
        _StarsPower ("Stars Power", Range(1, 500)) = 50
        [HDR] _ColorTop ("Color Top", Color) = (1.75, 2.75, 5, 1)
        [HDR] _ColorBottom ("Color Bottom", Color) = (5, 3.75, 1.75, 1)
        _MasterAlpha ("Master Alpha", Range(0, 1)) = 1
        _Reveal ("Reveal", Range(0, 1)) = 1
        _RevealFade ("Reveal Per-Star Fade", Range(0.01, 1)) = 0.15
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

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _StarsScale, _BlinkSpeed, _StarsPower;
            float4 _ColorTop, _ColorBottom;
            float _MasterAlpha, _Reveal, _RevealFade;

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

            // Unity Shader Graph Gradient Noise node, verbatim port.
            float2 gradient_dir(float2 p)
            {
                p = p % 289;
                float x = (34 * p.x + 1) * p.x % 289 + p.y;
                x = (34 * x + 1) * x % 289;
                x = frac(x / 41) * 2 - 1;
                return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
            }

            float gradient_noise(float2 p)
            {
                float2 ip = floor(p);
                float2 fp = frac(p);
                float d00 = dot(gradient_dir(ip), fp);
                float d01 = dot(gradient_dir(ip + float2(0, 1)), fp - float2(0, 1));
                float d10 = dot(gradient_dir(ip + float2(1, 0)), fp - float2(1, 0));
                float d11 = dot(gradient_dir(ip + float2(1, 1)), fp - float2(1, 1));
                fp = fp * fp * fp * (fp * (fp * 6 - 15) + 10);
                return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x) + 0.5;
            }

            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 s = i.screen.xy / i.screen.w;
                float2 uv = float2(s.x * (_ScreenParams.x / _ScreenParams.y), s.y);
                float2 t = _Time.y * _BlinkSpeed;
                float n1 = saturate(gradient_noise((uv + t) * _StarsScale));
                float n2 = saturate(gradient_noise((uv - t) * _StarsScale + float2(37.3, 91.7)));
                float core = pow(n1, _StarsPower) * pow(n2, _StarsPower);
                float birth = hash21(floor(uv * _StarsScale));
                float gate = smoothstep(birth, birth + _RevealFade, _Reveal * (1 + _RevealFade));
                float3 tint = lerp(_ColorBottom.rgb, _ColorTop.rgb, s.y);
                return float4(core * gate * tint * _MasterAlpha, 1);
            }
            ENDCG
        }
    }
}
