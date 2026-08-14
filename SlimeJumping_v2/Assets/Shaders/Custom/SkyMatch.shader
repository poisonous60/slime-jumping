// Quibli/Skybox 계산을 뷰 방향으로 재현해 지오메트리를 스카이박스와 같은 색으로 그린다
Shader "SlimeJumping/SkyMatch"
{
    Properties
    {
        _Gradient("Gradient", 2D) = "white" {}
        _Intensity("Intensity", Range(0, 5)) = 1.0
        _Exponent("Exponent", Range(0, 5)) = 1.0
        _DirectionYaw("Direction X angle", Range(0, 1)) = 0
        _DirectionPitch("Direction Y angle", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2D_FLOAT(_Gradient);
            float _DirectionYaw, _DirectionPitch;
            float _Intensity;
            float _Exponent;

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                const float pitch = _DirectionPitch * UNITY_PI;
                const float yaw = _DirectionYaw * UNITY_PI;
                const float3 direction = float3(sin(pitch) * sin(yaw), cos(pitch), sin(pitch) * cos(yaw));
                const float3 viewDir = normalize(i.worldPos - _WorldSpaceCameraPos);
                const float d = dot(viewDir, direction) * 0.5f + 0.5f;
                const float alpha = pow(d, _Exponent);
                return UNITY_SAMPLE_TEX2D(_Gradient, float2(alpha, 0.5f)) * _Intensity;
            }
            ENDCG
        }
    }
}
