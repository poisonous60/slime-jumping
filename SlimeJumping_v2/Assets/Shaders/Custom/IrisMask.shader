Shader "SlimeJumping/UI/IrisMask"
{
    Properties
    {
        _MainTex ("Sprite Texture (unused)", 2D) = "white" {}
        _Color  ("Color", Color)          = (0, 0, 0, 1)
        _Center ("Center (UV)", Vector)   = (0.5, 0.5, 0, 0)
        _Radius ("Radius (UV)", Float)    = 0.0
        _Edge   ("Edge Softness", Float)  = 0.008
        _Aspect ("Screen Aspect (w/h)", Float) = 1.7777
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Overlay"
            "RenderType"     = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"    = "Plane"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color    : COLOR;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color    : COLOR;
            };

            float4 _Color;
            float4 _Center;
            float  _Radius;
            float  _Edge;
            float  _Aspect;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color    = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.texcoord - _Center.xy;
                d.x *= _Aspect;
                float dist = length(d);
                // Inside radius -> alpha 0 (transparent hole); outside -> alpha 1 (opaque black).
                float a = smoothstep(_Radius - _Edge, _Radius + _Edge, dist);
                return fixed4(_Color.rgb, _Color.a * a);
            }
            ENDCG
        }
    }
}
