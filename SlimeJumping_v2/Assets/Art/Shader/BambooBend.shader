// Smooth bend + stylized-lit (Quibli-style two-tone) shader for BambooPlatform.
// The mesh is a procedurally-generated cylinder with positionOS in world-equivalent
// units (base at y=0, tip at y=_BambooLength). The collider chain (N segments under
// the same Rigidbody2D) approximates the SAME continuous curve as a polyline, so the
// visual and collider stay sub-pixel coincident — slime never floats off or sinks in.
//
// Curve: linear shape function h(t)=t (cantilever-like — local tangent angle scales
// linearly with height). Closed-form circular arc:
//   bendRad = radians(_BendAngleDeg)
//   t = positionOS.y / _BambooLength   ∈ [0,1]
//   localTangentAngle(t) = bendRad * t
//   centerline.x(t) = (L/bendRad) * (cos(bendRad*t) - 1)   // negative for positive bendRad (CCW)
//   centerline.y(t) = (L/bendRad) * sin(bendRad*t)
// Sign: positive _BendAngleDeg = counter-clockwise around +Z (standard Unity Euler).
// Cross-section (vertex offset from centerline in object-X) rotates by localTangentAngle.
//
// Node geometry: real bamboo's defining feature is a slight outward bulge at each
// node (joint between internodes), with a thin sheath-scar groove dead center. This
// shader applies a UV.v-driven radius modulator and recomputes the normal to make
// the bulge catch toon highlights/shadows — visually distinct without sticking dark
// stripes on the texture.
//
// Surface: stylized two-tone toon lighting + rim light (mimics Quibli StylizedLit's
// banded ramp) for the cartoony platformer look.
Shader "SlimeJumping/BambooBend"
{
    Properties
    {
        [Header(Bend)]
        _BendAngleDeg("Bend Angle (Deg)", Float) = 0
        _BambooLength("Bamboo Length", Float) = 1

        [Header(Bamboo Color)]
        [MainColor] _BambooColorBase("Stalk Color (Base)", Color) = (0.30, 0.52, 0.20, 1)
        _BambooColorTip("Stalk Color (Tip)", Color) = (0.50, 0.68, 0.28, 1)
        _ShadowDarken("Shadow Darken (0=no shadow, 1=full lit)", Range(0, 1)) = 0.60
        _ScarColor("Sheath Scar Color", Color) = (0.22, 0.26, 0.14, 1)

        [Header(Node Geometry)]
        _NodeFrequency("Rings per Bamboo Length", Float) = 6
        _BulgeAmount("Bulge Radius Multiplier (0..0.5)", Range(0, 0.5)) = 0.10
        _BulgeWidth("Bulge Half-Width (UV-V cycle)", Range(0.01, 0.4)) = 0.12
        _ScarHalfWidth("Scar Half-Width (UV-V cycle)", Range(0.001, 0.1)) = 0.004
        _ScarSoftness("Scar Edge Softness (UV-V cycle)", Range(0.0, 0.2)) = 0.02
        _ScarColorCurve("Scar Color Falloff Curve (1=linear)", Range(0.1, 6.0)) = 1.0
        _ScarDepth("Scar Pinch Depth (0..0.2)", Range(0, 0.2)) = 0.03

        [Header(Stylized Lighting)]
        _LightDirWS("Light Dir (WS)", Vector) = (0.3, 0.85, -0.45, 0)
        _ToonThreshold("Lit/Shadow Threshold", Range(-1, 1)) = 0.15
        _ToonSoftness("Threshold Softness", Range(0.001, 0.5)) = 0.08
        _Rim("Rim Strength", Range(0, 1)) = 0.25
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        [MainTexture] _BaseMap("Base Map (optional bark texture)", 2D) = "white" {}
        _BaseMapStrength("Base Map Strength", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100
        Cull Back

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _BendAngleDeg;
                float  _BambooLength;
                float4 _BambooColorBase;
                float4 _BambooColorTip;
                float  _ShadowDarken;
                float4 _ScarColor;
                float  _NodeFrequency;
                float  _BulgeAmount;
                float  _BulgeWidth;
                float  _ScarHalfWidth;
                float  _ScarSoftness;
                float  _ScarColorCurve;
                float  _ScarDepth;
                float4 _LightDirWS;
                float  _ToonThreshold;
                float  _ToonSoftness;
                float  _Rim;
                float  _RimPower;
                float4 _BaseMap_ST;
                float  _BaseMapStrength;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
                float2 uv          : TEXCOORD0;
                float  tHeight     : TEXCOORD3;
            };

            // Radial multiplier R(v) = 1 + bulge(v) - scar(v).
            // bulge = soft outward dome around each node center.
            // scar  = thin inward groove dead-center, sells the sheath-scar look.
            float ComputeRadiusMul(float v)
            {
                float c = frac(v * _NodeFrequency);
                float dist = min(c, 1.0 - c);

                float bulge = 1.0 - smoothstep(0.0, max(_BulgeWidth, 1e-4), dist);
                // Scar = full inside core (scarHalfWidth), then fades over scarSoftness.
                float scarOuter = max(_ScarHalfWidth + _ScarSoftness, _ScarHalfWidth + 1e-4);
                float scar = 1.0 - smoothstep(_ScarHalfWidth, scarOuter, dist);

                return 1.0 + _BulgeAmount * bulge - _ScarDepth * scar;
            }

            // Analytic dR/dv for the modulator above.
            //   d/dv [1 - smoothstep(0, w, dist(v))]
            //     where dist(v) = min(frac(v*F), 1 - frac(v*F))
            //     smoothstep deriv: 6 x (1-x) / w, with x = dist/w  ∈ [0,1]
            //     d(dist)/dv = ±F  (sign flips when frac crosses 0.5)
            // dR/dv = -d(bulge)/dv * bulgeAmount + d(scar)/dv * scarDepth, then bulge has
            // a "1 - smoothstep" so derivative sign flips again. Combine into a single
            // helper below.
            float ComputeRadiusMulDeriv(float v)
            {
                float c = frac(v * _NodeFrequency);
                float dist = min(c, 1.0 - c);
                // d(dist)/dv = F when c<0.5, -F when c>=0.5.
                float ddist_dv = (c < 0.5 ? 1.0 : -1.0) * _NodeFrequency;

                float bw = max(_BulgeWidth, 1e-4);
                // Scar smoothstep is over [_ScarHalfWidth, _ScarHalfWidth + _ScarSoftness].
                float sInner = _ScarHalfWidth;
                float sw = max(_ScarSoftness, 1e-4);

                // smoothstep(0, w, dist) deriv wrt dist, inside [0, w]:
                //   d/dd [3 (d/w)² - 2 (d/w)³] = 6 d (w - d) / w³
                float bulgeDeriv = (dist < bw) ? 6.0 * dist * (bw - dist) / (bw * bw * bw) : 0.0;

                // Scar deriv: smoothstep(sInner, sInner+sw, dist) → x = (dist - sInner)/sw.
                // Valid for dist ∈ [sInner, sInner+sw]; else 0.
                float scarDeriv = 0.0;
                if (dist > sInner && dist < sInner + sw)
                {
                    float x = (dist - sInner) / sw;
                    scarDeriv = 6.0 * x * (1.0 - x) / sw;
                }

                // R(v) = 1 + bulgeAmount*(1 - ssBulge) - scarDepth*(1 - ssScar)
                // dR/dv = -bulgeAmount * bulgeDeriv * ddist_dv + scarDepth * scarDeriv * ddist_dv
                return ddist_dv * (-_BulgeAmount * bulgeDeriv + _ScarDepth * scarDeriv);
            }

            void ApplyBend(inout float3 pos, inout float3 nrm, out float tOut)
            {
                float L = max(_BambooLength, 1e-4);
                float t = saturate(pos.y / L);
                tOut = t;

                // Node geometry FIRST (pre-bend cross-section): modulate radius and
                // tilt the normal in the radial-Y plane so the bulge picks up shading.
                float radialLen = length(float2(pos.x, pos.z));
                float3 radialDir = (radialLen > 1e-4)
                    ? float3(pos.x / radialLen, 0.0, pos.z / radialLen)
                    : float3(0, 0, 0);

                float R = ComputeRadiusMul(t);
                float Rprime = ComputeRadiusMulDeriv(t) / L; // dR/dy in object units
                pos.x *= R;
                pos.z *= R;

                // Outward normal for a barrel-of-revolution: (radialDir, -dR/dy) / sqrt(1 + (dR/dy)²)
                float invNorm = rsqrt(1.0 + Rprime * Rprime);
                float3 baseNormal = float3(radialDir.x, -Rprime, radialDir.z) * invNorm;

                // Now apply bend to (modulated) cross-section.
                float bendRad = radians(_BendAngleDeg);
                float localAng = bendRad * t;

                float2 centerline;
                if (abs(bendRad) > 1e-4)
                {
                    float invK = L / bendRad;
                    centerline.x = invK * (cos(localAng) - 1.0);
                    centerline.y = invK * sin(localAng);
                }
                else
                {
                    centerline = float2(0.0, pos.y);
                }

                float px = pos.x;
                float pz = pos.z;
                float s = sin(localAng);
                float cAng = cos(localAng);

                pos.x = centerline.x + px * cAng;
                pos.y = centerline.y + px * s;
                pos.z = pz;

                // Rotate the bulge-corrected normal in the bend plane.
                float3 n = baseNormal;
                nrm.x = cAng * n.x - s * n.y;
                nrm.y = s * n.x + cAng * n.y;
                nrm.z = n.z;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 pos = IN.positionOS.xyz;
                float3 nrm = IN.normalOS;
                float tHeight;
                ApplyBend(pos, nrm, tHeight);

                float3 posWS = TransformObjectToWorld(pos);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(nrm);
                OUT.viewDirWS = GetWorldSpaceViewDir(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.tHeight = tHeight;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 L = normalize(_LightDirWS.xyz);
                float ndl = dot(N, L);
                float shade = smoothstep(_ToonThreshold - _ToonSoftness,
                                          _ToonThreshold + _ToonSoftness, ndl);

                float tHeight = saturate(IN.tHeight);
                float c = frac(tHeight * _NodeFrequency);
                float dist = min(c, 1.0 - c);
                float scarOuter = max(_ScarHalfWidth + _ScarSoftness, _ScarHalfWidth + 1e-4);
                float scarRaw = 1.0 - smoothstep(_ScarHalfWidth, scarOuter, dist);

                // Shape the color-side scar mask by a power curve. Geometry pinch in
                // ComputeRadiusMul still uses the raw mask so the bulge/pinch stays
                // independent of the color falloff aesthetic.
                float scar = pow(scarRaw, max(_ScarColorCurve, 1e-4));

                // Smooth height gradient on the stalk — base hue → tip hue. Scar uses its
                // own (uniform) color so the dark line reads against either end.
                float3 stalkLit = lerp(_BambooColorBase.rgb, _BambooColorTip.rgb, tHeight);
                float3 litColor = lerp(stalkLit, _ScarColor.rgb, scar);

                // Shadow is the same hue, just darkened by a uniform factor — avoids the
                // hard hue split that made top/bottom feel disconnected.
                float darken = saturate(_ShadowDarken);
                float3 shadowColor = litColor * darken;

                float3 baseColor = lerp(shadowColor, litColor, shade);

                // Optional bark texture mix-in (default off → clean stylized color).
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                baseColor = lerp(baseColor, baseColor * tex.rgb * 2.0, _BaseMapStrength);

                // Rim — Quibli-style sliver on silhouette edges, picks up the bulge too.
                float3 V = normalize(IN.viewDirWS);
                float rim = pow(saturate(1.0 - max(0.0, dot(N, V))), _RimPower) * _Rim;
                baseColor += rim * litColor;

                return half4(baseColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
