Shader "BackHome/Hud/VitalsBarFill"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _FlowSpeed("Flow Speed", Range(0, 2)) = 0.26
        _BandScale("Flow Bands", Range(1, 16)) = 6.2
        _WaveHeight("Surface Wave", Range(0, 0.12)) = 0.032
        _Highlight("Glass Highlight", Range(0, 1.5)) = 0.42
        _Meniscus("Edge Meniscus", Range(0, 1)) = 0.5
        _BubbleAmount("Micro Bubbles", Range(0, 1)) = 0.28
        _DepthShade("Tube Depth", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _FlowSpeed;
                half _BandScale;
                half _WaveHeight;
                half _Highlight;
                half _Meniscus;
                half _BubbleAmount;
                half _DepthShade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.56));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 tint = input.color;
                float t = _TimeParameters.x;
                float2 uv = input.uv;

                // Suit-tube depth: darker floor, clearer visor surface.
                half tube = lerp(1.0h - _DepthShade, 1.0h + _DepthShade * 0.35h, saturate(uv.y));

                // Slow laminar coolant flow, not choppy water.
                float flow = uv.x * _BandScale - t * _FlowSpeed;
                float band = 0.5 + 0.5 * sin(flow) * sin(flow * 0.41 + t * 0.17 + uv.y * 3.0);

                // Soft gel surface along the top of the pill.
                float wave = sin(uv.x * 11.0 + t * 1.35) * _WaveHeight;
                float surface = saturate(1.0 - abs(uv.y - (0.74 + wave)) * 9.0);

                // Leading edge reads as a liquid meniscus inside the tank.
                float edge = saturate((uv.x - 0.86) / 0.14);
                float meniscus = edge * edge * _Meniscus;

                // Visor-glass specular streak.
                float spec = saturate(1.0 - abs(uv.y - 0.6) * 13.0);
                spec *= 0.28 + 0.72 * saturate(0.55 + 0.45 * sin(uv.x * 2.8 + t * 0.4));

                // Sparse micro-bubbles drifting up the coolant.
                float2 cell = float2(uv.x * 16.0 - t * 0.12, uv.y * 5.5 + t * 0.22);
                float2 id = floor(cell);
                float2 f = frac(cell) - 0.5;
                float n = Hash21(id);
                float2 jitter = float2(n, frac(n * 7.13)) - 0.5;
                float bubbleMask = step(1.0 - _BubbleAmount * 0.18, n);
                float bubble = saturate(1.0 - length(f - jitter * 0.35) * 5.5) * bubbleMask * 0.55;

                half3 col = tint.rgb * tube;
                col *= lerp(0.88, 1.06, band);
                col += tint.rgb * surface * _Highlight * 0.55;
                col += tint.rgb * meniscus * 0.4;
                col += spec * _Highlight * 0.65;
                col += bubble;

                return half4(col, mask.a * tint.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
