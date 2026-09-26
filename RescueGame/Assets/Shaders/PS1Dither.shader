// Full-screen PS1 colour crunch: reduces the number of shades per colour channel
// and hides the banding with an ordered (Bayer) dither pattern, like the PS1 did.
// Used by the Full Screen Pass renderer feature on PC_Renderer.
Shader "RescueGame/PS1 Dither"
{
    Properties
    {
        _ColorLevels("Shades per Channel", Float) = 32
        _DitherStrength("Dither Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "PS1Dither"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _ColorLevels;
            float _DitherStrength;

            static const float kBayer4x4[16] =
            {
                 0,  8,  2, 10,
                12,  4, 14,  6,
                 3, 11,  1,  9,
                15,  7, 13,  5
            };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);

                uint2 p = uint2(input.positionCS.xy) % 4;
                float threshold = (kBayer4x4[p.y * 4 + p.x] + 0.5) / 16.0 - 0.5;

                float levels = max(_ColorLevels - 1, 1);
                c.rgb = floor(c.rgb * levels + 0.5 + threshold * _DitherStrength) / levels;
                return half4(saturate(c.rgb), 1);
            }
            ENDHLSL
        }
    }
}
