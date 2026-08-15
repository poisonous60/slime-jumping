Shader "SlimeJumping/Star Field Overlay"
{
    // A jittered grid where a cell is occupied only with probability _StarDensity. Occupancy is
    // what makes the field read as sky: one star per cell forces every block of cells to hold
    // exactly that many stars, so the count has zero variance and a hard minimum spacing, while a
    // real sky is a Poisson field with clumps and voids. The grid is divided by sqrt(density)
    // internally, so _StarsScale stays a pure count knob and density only trades clumpiness
    // against cell size. How many stars come out at each size is the inverse-CDF lookup
    // _SizeCurve, baked by StarFieldOverlay from a curve the artist draws, so one field carries
    // everything from sub-pixel dust to the few giants without splitting into layers.
    // 5x5 neighbour cells are sampled and a star is faded out by two cells, so it may sit anywhere
    // in its cell and still be whole; the cost is that a star wider than 1.5 cells gets clipped, and
    // a cell is 1080 * sqrt(density) / _StarsScale pixels tall — asking for bigger stars means
    // raising density, which is the same knob that buys clumping.
    // The earlier Binary Lunar build (youtube wSM-l2T8wOk) raised a gradient-noise product
    // to a power instead, where a star is a smooth hump whose radius falls out of the cell size —
    // there n ~ 1-k*r² with k ~ scale², so radius ~ 1/(scale*sqrt(power)) and asking for more stars
    // could only make them smaller and softer. Colour comes from the vertical HDR gradient and
    // reveal gates on a per-cell hash. Rendered additively on a camera-facing quad. The glow around
    // each star is URP's own Bloom, run by OverlayBloomCamera on a private camera: light spill is a
    // spatial effect, so no per-pixel reshaping can stand in for it, and the main camera cannot
    // supply it because TranslucentImageBlurSource drops its color buffer to 8-bit. Keep the HDR
    // colors well above 1 or nothing clears the bloom threshold — they also set how wide a star
    // reads, since the core saturates wherever tint*core passes 1.
    // The core is a regular polygon rather than a disc, with side count and spin hashed per
    // star, because a disc reads as a soft dot at every size while the reference footage shows
    // hard-edged aperture shapes; only stars big enough to cover several pixels can show it.
    // Sampling is in screen space, not the quad's mesh UV: the quad is sized from
    // the camera FOV, which Cinemachine blends per vcam, so mesh UV made the whole
    // field re-roll and change count whenever the FOV moved. _Reveal and _SizeCurve come from
    // StarFieldOverlay via MaterialPropertyBlock, the rest are look knobs.
    Properties
    {
        _StarsScale ("Stars Scale (count)", Float) = 100
        _StarDensity ("Cell Occupancy", Range(0.02, 1)) = 0.25
        _StarSize ("Star Size Max (uv)", Range(0.0002, 0.01)) = 0.003
        _SizeCurve ("Size Distribution LUT", 2D) = "white" {}
        _StarBrightVariance ("Star Brightness Variance", Range(0.02, 1)) = 0.12
        _StarBrightSkew ("Star Brightness Skew (rarity of bright)", Range(1, 200)) = 1
        _StarSidesMin ("Star Sides Min", Range(3, 16)) = 4
        _StarSidesMax ("Star Sides Max", Range(3, 16)) = 7
        _StarFalloff ("Star Falloff", Range(1, 8)) = 4
        _StarCoreCap ("Star Core Cap (halo reach)", Range(1, 64)) = 64
        _BlinkSpeed ("Blink Speed", Float) = 0.002
        _BlinkAmount ("Blink Amount", Range(0, 1)) = 0.35
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
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _SizeCurve;
            float _StarsScale, _StarDensity, _StarSize;
            float _StarBrightVariance, _StarBrightSkew, _StarFalloff, _BlinkSpeed, _BlinkAmount;
            float _StarSidesMin, _StarSidesMax, _StarCoreCap;
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

            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Dave Hoskins hash: three decorrelated values per cell, no trig.
            float3 hash32(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yzz) * p3.zyx);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 s = i.screen.xy / i.screen.w;
                float2 uv = float2(s.x * (_ScreenParams.x / _ScreenParams.y), s.y);

                // Finer grid for the same count, so a lower occupancy buys clumping without losing stars.
                float eff = max(_StarsScale, 1e-4) / sqrt(max(_StarDensity, 1e-4));
                float2 g = uv * eff;
                float2 base = floor(g);
                float acc = 0.0;

                for (int oy = -2; oy <= 2; oy++)
                {
                    for (int ox = -2; ox <= 2; ox++)
                    {
                        float2 off = float2(ox, oy);
                        float2 cell = base + off;
                        float3 h = hash32(cell);
                        float2 d2 = g - cell - h.xy;
                        // Polygon distance is never below the euclidean one, so this rejects nothing visible.
                        if (dot(d2, d2) > 4.0) continue;
                        if (hash21(cell + 3.7) > _StarDensity) continue;

                        float3 h2 = hash32(cell + 91.3);
                        float sides = max(floor(lerp(_StarSidesMin, _StarSidesMax + 1.0, h2.x)), 3.0);
                        float seg = 6.2831853 / sides;
                        float ang = atan2(d2.y, d2.x) + h2.y * 6.2831853;
                        ang -= seg * floor(ang / seg);
                        // Regular polygon inscribed in the circle it replaces, hashed sides and spin so no two point alike.
                        float dCell = length(d2) * cos(ang - seg * 0.5) / cos(seg * 0.5);

                        // Inverse CDF, so a flat hash lands on sizes in exactly the proportions drawn on the curve.
                        float sizeMul = tex2Dlod(_SizeCurve, float4(h.z, 0.5, 0, 0)).r;
                        float dUV = dCell / eff;
                        // Also the halo knob: how far the cap sits above the bloom threshold is what sets the glow's reach,
                        // and uncapped 1/d would diverge at the exact centre and push HDR tint past half range into Inf.
                        float core = min(pow((_StarSize * sizeMul) / max(dUV, 1e-6), _StarFalloff), _StarCoreCap);
                        // Gone before the 5x5 tap window ends, so a star never pops in at the sampling boundary.
                        core *= 1.0 - smoothstep(1.5, 2.0, dCell);
                        // Own hash, so a star can be small and bright or large and faint instead of size deciding both.
                        core *= lerp(_StarBrightVariance, 1.0, pow(hash21(cell + 17.3), _StarBrightSkew));
                        core *= 1.0 - _BlinkAmount * (0.5 + 0.5 * sin(_Time.y * _BlinkSpeed + h2.z * 6.2831853));

                        float birth = hash21(cell);
                        core *= smoothstep(birth, birth + _RevealFade, _Reveal * (1 + _RevealFade));
                        acc += core;
                    }
                }

                float3 tint = lerp(_ColorBottom.rgb, _ColorTop.rgb, s.y);
                return float4(acc * tint * _MasterAlpha, 1);
            }
            ENDCG
        }
    }
}
