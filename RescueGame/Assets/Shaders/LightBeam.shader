// Soft visible light beam (the helicopter searchlight in concept 3): additive, brightest at the
// lamp and along the beam's axis, fading out along its length and toward its edges.
Shader "RescueGame/Light Beam"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.85, 0.4, 1)
        _Intensity ("Intensity", Range(0, 2)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Intensity;
            CBUFFER_END

            // uv.x = around (0..1), uv.y = along the beam (0 at the lamp, 1 at the far end)
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float along : TEXCOORD0; float facing : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.along = v.uv.y;
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                float3 view = normalize(GetWorldSpaceViewDir(ws));
                o.facing = abs(dot(n, view));   // edges of the cone (grazing) fade out
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = (1 - i.along) * (1 - i.along) * smoothstep(0.0, 0.6, i.facing) * _Intensity;
                return half4(_Color.rgb * a, 1);
            }
            ENDHLSL
        }
    }
}
