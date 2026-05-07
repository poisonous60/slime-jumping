Shader "World/SDFRing"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _PixelThickness ("Outline Thickness (px)", Range(1, 20)) = 3
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            float _PixelThickness;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float distNorm = length(i.texcoord - 0.5) * 2.0;
                float pxStep = fwidth(distNorm);
                float thicknessNorm = _PixelThickness * pxStep;
                float aa = pxStep;

                float outer = 1.0 - smoothstep(1.0 - aa, 1.0, distNorm);
                float innerStart = 1.0 - thicknessNorm;
                float inner = smoothstep(innerStart - aa, innerStart, distNorm);
                float alpha = outer * inner;

                fixed4 c = i.color;
                c.a *= alpha;
                return c;
            }
            ENDCG
        }
    }
}
