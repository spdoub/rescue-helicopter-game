// Main surface shader for the game. Target look: ArtSource/Concept/concept_3_rooftop.png
// - Flat shading: each triangle gets one shade, so low-poly facets read crisply.
// - Soft painterly light: wrapped diffuse so shadows stay soft, with a warm shadow tint
//   instead of going grey/black (golden-hour feel).
// - Optional world-space texture projection for big level pieces, fog, main-light shadows.
// - First-person hiding: vertices tagged in UV channel 3 (x = 1) are not drawn to the camera
//   when _HideTagged is on (your own torso/head), but still cast shadows. Where that leaves an
//   opening, the inside is drawn as a solid flat colour, so cut edges look capped, never hollow.
Shader "RescueGame/Stylized Lit"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        [Toggle] _FlatShading("Flat Shading", Float) = 1
        _LightWrap("Light Wrap (softness)", Range(0, 1)) = 0.35
        _ShadowTint("Shadow Tint", Color) = (0.55, 0.35, 0.45, 1)
        _Ambient("Ambient Boost", Range(0, 1)) = 0.1
        _WorldUVScale("World-Space Texture Scale (0 = use mesh UVs)", Float) = 0
        [Toggle] _HideTagged("Hide Tagged Parts (first person)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _FlatShading;
            half _LightWrap;
            half4 _ShadowTint;
            half _Ambient;
            float _WorldUVScale;
            half _HideTagged;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 hideTag : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half hide : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.hide = v.hideTag.x * _HideTagged;
                return o;
            }

            half4 frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                clip(0.5 - i.hide);
                if (!frontFace)
                {
                    // Inside of a mesh: normally never seen (culled). For a first-person body with
                    // hidden parts, paint it as a solid cap in the surface's own colour.
                    clip(_HideTagged - 0.5);
                    half3 cap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb * 0.75;
                    return half4(MixFog(cap, i.fogFactor), 1);
                }
                float3 smoothN = normalize(i.normalWS);
                float3 n = smoothN;
                if (_FlatShading > 0.5)
                {
                    // One normal per triangle, from the screen-space slope of the surface.
                    float3 flatN = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                    n = dot(flatN, smoothN) < 0 ? -flatN : flatN;
                }

                float2 uv = i.uv;
                if (_WorldUVScale > 0)
                {
                    float3 a = abs(smoothN);
                    float3 p = i.positionWS * _WorldUVScale;
                    uv = a.y > a.x && a.y > a.z ? p.xz : (a.x > a.z ? p.zy : p.xy);
                }
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half wrapped = saturate((dot(n, light.direction) + _LightWrap) / (1 + _LightWrap));
                half lit = wrapped * light.shadowAttenuation;

                half3 ambient = SampleSH(n) + _Ambient;
                // Shadowed areas lean toward a warm purple tint instead of flat grey.
                half3 shade = lerp(_ShadowTint.rgb * ambient, ambient + light.color, lit);
                half3 color = albedo.rgb * shade;
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 vert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float2 hideTag : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; half hide : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.hide = v.hideTag.x * _HideTagged;
                return o;
            }
            half frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                clip(0.5 - i.hide);
                if (!frontFace) clip(_HideTagged - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }
}
