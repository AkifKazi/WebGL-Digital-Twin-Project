Shader "Hidden/Digital Twin/Selection Outline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        // 0 - coverage mask. Depth is ignored on purpose: an occluded part is
        //     still marked, like an editor selection.
        Pass
        {
            Name "Mask"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings MaskVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MaskFragment(Varyings input) : SV_Target
            {
                return 1.0h;
            }
            ENDHLSL
        }

        // 1 - composite: outline where the mask ends, faint fill where it covers.
        Pass
        {
            Name "Composite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float4 _FillColor;
            float4 _OutlineRadius;

            half Coverage(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
            }

            half4 CompositeFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half inside = Coverage(uv);

                // Eight directions at the full and half radius: the inner ring
                // stops thin parts slipping between the outer samples.
                const float2 directions[8] =
                {
                    float2(1.0, 0.0), float2(0.7071, 0.7071), float2(0.0, 1.0), float2(-0.7071, 0.7071),
                    float2(-1.0, 0.0), float2(-0.7071, -0.7071), float2(0.0, -1.0), float2(0.7071, -0.7071)
                };

                half ring = 0.0h;

                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    ring = max(ring, Coverage(uv + directions[i] * _OutlineRadius.xy));
                    ring = max(ring, Coverage(uv + directions[i] * _OutlineRadius.xy * 0.5));
                }

                half edge = saturate(ring - inside);

                half fillAlpha = _FillColor.a * inside;
                half lineAlpha = _OutlineColor.a * edge;
                half alpha = lineAlpha + fillAlpha * (1.0h - lineAlpha);

                half3 rgb = (_OutlineColor.rgb * lineAlpha + _FillColor.rgb * fillAlpha * (1.0h - lineAlpha))
                            / max(alpha, 1e-4h);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
