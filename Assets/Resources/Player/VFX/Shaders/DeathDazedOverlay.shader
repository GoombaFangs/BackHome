Shader "BackHome/Hud/DeathDazedOverlay"
{
    Properties
    {
        [HDR] _Color("Dazed Tint", Color) = (0.4, 0.42, 0.48, 1)
        _Intensity("Intensity", Range(0, 1)) = 0
        _Vignette("Vignette", Range(0.2, 6)) = 1.4
        _VignetteSoftness("Vignette Softness", Range(0.05, 2)) = 0.45
        _NoiseScale("Noise Scale", Float) = 5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.16
        _PulseSpeed("Pulse Speed", Float) = 0.3
        _PulseAmount("Pulse Amount", Range(0, 0.5)) = 0.12
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
            Name "DeathDazedOverlay"
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
                half _Intensity;
                half _Vignette;
                half _VignetteSoftness;
                half _NoiseScale;
                half _NoiseStrength;
                half _PulseSpeed;
                half _PulseAmount;
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
                clip(_Intensity - 0.001h);

                float2 uv = input.uv;
                float2 centered = uv * 2.0 - 1.0;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                centered.x *= aspect;

                // Slow breathing vignette - a woozy, semi-conscious pulse instead of a hard edge.
                float pulse = sin(_Time.y * _PulseSpeed * 6.2831) * 0.5 + 0.5;
                float breathe = 1.0 + (pulse - 0.5) * _PulseAmount;

                float dist = length(centered) * breathe;
                float inner = max((float)_VignetteSoftness, 0.05);
                float outer = inner + max((float)_Vignette, 0.2);
                float vignette = smoothstep(inner, outer, dist);

                float n = ValueNoise(uv * _NoiseScale + _Time.y * 0.04);
                n = n * 0.7 + ValueNoise(uv * _NoiseScale * 2.3 - _Time.y * 0.025 + 5.0) * 0.3;
                float grain = (n - 0.5) * _NoiseStrength;

                half3 color = saturate(_Color.rgb + grain);
                half alpha = saturate((0.3 + vignette * 0.7) * _Intensity * _Color.a);
                clip(alpha - 0.001h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
