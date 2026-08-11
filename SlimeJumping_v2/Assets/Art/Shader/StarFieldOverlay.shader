Shader "SlimeJumping/Star Field Overlay"
{
    // Binary Lunar starfield (youtube wSM-l2T8wOk): two gradient-noise fields
    // counter-scrolled over time, saturate+power each, multiplied together,
    // tinted by a vertical HDR color gradient. Rendered additively on a
    // camera-facing quad. Every noise cell hashes to its own birth threshold, so
    // raising _Reveal lights up more stars instead of brightening the same ones —
    // _Reveal and _UvScale come per-frame from StarFieldOverlay via
    // MaterialPropertyBlock, the rest are asset-side look knobs.
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
        _UvScale ("Uv Aspect Scale", Float) = 1
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
            float _MasterAlpha, _Reveal, _RevealFade, _UvScale;

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

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = float2(i.uv.x * _UvScale, i.uv.y);
                float2 t = _Time.y * _BlinkSpeed;
                float f1 = pow(saturate(gradient_noise((uv + t) * _StarsScale)), _StarsPower);
                float f2 = pow(saturate(gradient_noise((uv - t) * _StarsScale)), _StarsPower);
                float birth = hash21(floor(uv * _StarsScale));
                float gate = smoothstep(birth, birth + _RevealFade, _Reveal * (1 + _RevealFade));
                float3 tint = lerp(_ColorBottom.rgb, _ColorTop.rgb, i.uv.y);
                return fixed4(f1 * f2 * gate * tint * _MasterAlpha, 1);
            }
            ENDCG
        }
    }
}
