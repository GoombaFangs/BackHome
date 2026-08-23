Shader "BackHome/Hud/PlayerDamageOverlay"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.72, 0.04, 0.06, 0.85)
        _Opacity("Opacity", Range(0, 1)) = 0.8
        _Vignette("Vignette", Range(0.2, 6)) = 2.1
        _VignetteSoftness("Vignette Softness", Range(0.05, 2)) = 0.55
        _Dissolve("Dissolve", Range(0, 1)) = 0
        _NoiseScale("Noise Scale", Float) = 7
        _EdgeWidth("Edge Width", Range(0.01, 0.4)) = 0.11
        [HDR] _EdgeColor("Edge Color", Color) = (1.5, 0.22, 0.08, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PlayerDamageOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                half _Vignette;
                half _VignetteSoftness;
                half _Dissolve;
                half _NoiseScale;
                half _EdgeWidth;
                half4 _EdgeColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(_Dissolve - 0.001h);

                float2 uv = input.uv;
                float2 centered = uv * 2.0 - 1.0;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                centered.x *= aspect;

                float dist = length(centered);
                float inner = max((float)_VignetteSoftness, 0.05);
                float outer = inner + max((float)_Vignette, 0.2);
                float vignette = smoothstep(inner, outer, dist);

                float n = ValueNoise(uv * _NoiseScale);
                n = n * 0.62 + ValueNoise(uv * _NoiseScale * 2.15 + 11.7) * 0.38;

                // Edges fill first; noisy holes eat the rest.
                float threshold = saturate(n * 0.58 + (1.0 - vignette) * 0.42);
                float edge = max((float)_EdgeWidth, 0.01);
                float reveal = _Dissolve * (1.0 + edge);
                float mask = smoothstep(threshold, threshold + edge, reveal);
                float rim = 1.0 - saturate(abs(reveal - threshold) / edge);
                rim *= mask;

                half3 color = lerp(_Color.rgb, _EdgeColor.rgb, rim * 0.9h);
                half alpha = vignette * _Color.a * _Opacity * mask;
                clip(alpha - 0.001h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
