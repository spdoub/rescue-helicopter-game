// Cockpit glass: faint warm tint, brighter at glancing angles (fresnel), with soft diagonal
// streaks so you can tell it's there without it getting in the way of the view.
Shader "RescueGame/Glass"
{
    Properties
    {
        _Tint("Tint", Color) = (0.85, 0.92, 1.0, 0.05)
        _EdgeColor("Glancing Reflection", Color) = (1.0, 0.85, 0.6, 0.12)
        _StreakStrength("Streaks", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Glass"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _EdgeColor;
                half _StreakStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half facing = abs(dot(normalize(i.normalWS), viewDir));
                half fresnel = pow(1.0h - facing, 5.0h);
                half streak = saturate(sin((i.positionWS.x + i.positionWS.y + i.positionWS.z) * 3.0) * 0.5 + 0.5);
                streak = pow(streak, 12.0h) * _StreakStrength;
                half3 col = lerp(_Tint.rgb, _EdgeColor.rgb, fresnel) + streak;
                half a = saturate(_Tint.a + fresnel * _EdgeColor.a + streak);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
