// PS1-style surface shader for the whole game.
// - Flat shading: each triangle gets one solid shade (the faceted low-poly look).
// - Vertex snapping: corners snap to a coarse screen grid, so models jitter like on the PS1.
// - Affine texture warp: textures skew slightly across big triangles, like PS1 hardware.
// - Point-filtered textures (set on the texture import), fog, and main-light shadows.
Shader "RescueGame/PS1 Lit"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        [Toggle] _FlatShading("Flat Shading", Float) = 1
        _SnapResolution("Vertex Snap Grid (lower = more wobble)", Float) = 220
        _AffineAmount("Texture Warp", Range(0, 1)) = 0.5
        _ShadeSteps("Light Bands (0 = smooth)", Float) = 0
        _Ambient("Ambient Boost", Range(0, 1)) = 0.2
        _WorldUVScale("World-Space Texture Scale (0 = use mesh UVs)", Float) = 0
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
            float _SnapResolution;
            half _AffineAmount;
            half _ShadeSteps;
            half _Ambient;
            float _WorldUVScale;
        CBUFFER_END

        // Snap a clip-space position to a low-res virtual screen grid.
        float4 SnapClipPos(float4 positionCS)
        {
            if (_SnapResolution <= 0) return positionCS;
            float aspect = _ScreenParams.x / _ScreenParams.y;
            float2 grid = float2(_SnapResolution * aspect, _SnapResolution) * 0.5;
            float2 ndc = positionCS.xy / positionCS.w;
            ndc = round(ndc * grid) / grid;
            positionCS.xy = ndc * positionCS.w;
            return positionCS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

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
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 affineUV : TEXCOORD1; // uv * w, w  -> dividing in the fragment gives affine mapping
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = SnapClipPos(pos.positionCS);
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.affineUV = float3(o.uv * o.positionCS.w, o.positionCS.w);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 smoothN = normalize(i.normalWS);

                float2 uv = lerp(i.uv, i.affineUV.xy / i.affineUV.z, _AffineAmount);
                if (_WorldUVScale > 0)
                {
                    // Project the texture from whichever world axis the surface faces, so
                    // stretched grey-box cubes and the huge ground keep evenly sized texels.
                    float3 a = abs(smoothN);
                    float3 p = i.positionWS * _WorldUVScale;
                    uv = a.y > a.x && a.y > a.z ? p.xz : (a.x > a.z ? p.zy : p.xy);
                }
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;

                float3 n = smoothN;
                if (_FlatShading > 0.5)
                {
                    // One normal per triangle, taken from the screen-space slope of the surface.
                    float3 flatN = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                    n = dot(flatN, smoothN) < 0 ? -flatN : flatN;
                }

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndotl = saturate(dot(n, light.direction));
                half lit = ndotl * light.shadowAttenuation;
                if (_ShadeSteps > 0) lit = floor(lit * _ShadeSteps + 0.5) / _ShadeSteps;

                half3 ambient = SampleSH(n) + _Ambient;
                half3 color = albedo.rgb * (ambient + light.color * lit);
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

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; };

            float4 vert(Attributes v) : SV_POSITION
            {
                return SnapClipPos(TransformObjectToHClip(v.positionOS.xyz));
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
