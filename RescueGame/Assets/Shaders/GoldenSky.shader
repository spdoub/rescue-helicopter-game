// Golden-hour sky for concept 3: warm peach haze at the horizon fading to a soft apricot overhead,
// with a big gentle glow around the sun (follows the main directional light).
Shader "RescueGame/Golden Sky"
{
    Properties
    {
        _Horizon ("Horizon", Color) = (1.0, 0.8, 0.56, 1)
        _Zenith ("Overhead", Color) = (0.9, 0.62, 0.46, 1)
        _Below ("Below horizon", Color) = (0.86, 0.66, 0.5, 1)
        _HorizonFalloff ("Horizon falloff", Range(0.1, 4)) = 0.55
        _SunGlow ("Sun glow colour", Color) = (1.0, 0.9, 0.62, 1)
        _SunGlowPower ("Sun glow tightness", Range(1, 64)) = 6
        _SunDisc ("Sun disc tightness", Range(100, 5000)) = 1400
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Horizon, _Zenith, _Below, _SunGlow;
                half _HorizonFalloff, _SunGlowPower, _SunDisc;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 col = d.y >= 0
                    ? lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(d.y), _HorizonFalloff))
                    : lerp(_Horizon.rgb, _Below.rgb, saturate(-d.y * 4));
                float3 sun = _MainLightPosition.xyz;
                float s = saturate(dot(d, sun));
                col += _SunGlow.rgb * pow(s, _SunGlowPower) * 0.35;
                col += _SunGlow.rgb * pow(s, _SunDisc) * 1.5;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
