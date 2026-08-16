Shader "BackHome/VFX/TeleportBody"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)

        [Header(Animation)]
        _Progress("Progress", Range(0, 1)) = 0
        _OriginWS("Origin WS", Vector) = (0, 0, 0, 0)
        _AxisWS("Axis WS", Vector) = (0, 1, 0, 0)
        _Height("Height", Float) = 2

        [Header(Dissolve)]
        _NoiseScale("Noise Scale", Float) = 3.2
        _NoiseAmount("Noise Amount", Range(0, 0.4)) = 0.11
        _EdgeWidth("Edge Width", Range(0.01, 0.35)) = 0.08

        [Header(Energy)]
        [HDR] _GlowColor("Glow", Color) = (0.12, 0.42, 1.8, 1)
        [HDR] _EdgeColor("Edge", Color) = (1.6, 2.1, 4.2, 1)
        [HDR] _CoreColor("Core", Color) = (0.35, 0.85, 2.4, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 2.4
        _ScanCount("Scan Lines", Range(8, 80)) = 36
        _ScanStrength("Scan Strength", Range(0, 2)) = 0.85

        [Header(Mesh Warp)]
        _Stretch("Up Stretch", Range(0, 3)) = 1.15
        _Converge("Beam Converge", Range(0, 1)) = 0.55
        _Glitch("Glitch", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "TeleportBody"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Progress;
                float4 _OriginWS;
                float4 _AxisWS;
                float _Height;
                float _NoiseScale;
                float _NoiseAmount;
                float _EdgeWidth;
                half4 _GlowColor;
                half4 _EdgeColor;
                half4 _CoreColor;
                half _RimPower;
                half _ScanCount;
                half _ScanStrength;
                half _Stretch;
                half _Converge;
                half _Glitch;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float height01 : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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

            float BodyNoise(float3 positionWS, float time)
            {
                float n = ValueNoise(positionWS.xz * _NoiseScale + time * 0.55);
                n += ValueNoise(positionWS.xy * _NoiseScale * 1.6 - time * 0.35) * 0.45;
                return n * (1.0 / 1.45);
            }

            void TeleportStages(float progress, out float charge, out float dissolve)
            {
                charge = saturate(progress / 0.22);
                dissolve = saturate((progress - 0.18) / 0.82);
            }

            void WarpAndHeight(inout float3 positionWS, float3 origin, float3 axis, float progress, out float height01)
            {
                float charge, dissolve;
                TeleportStages(progress, charge, dissolve);

                float height = max(_Height, 0.001);
                float h = dot(positionWS - origin, axis);
                height01 = saturate(h / height);

                float3 along = origin + axis * h;
                float3 radial = positionWS - along;
                float converge = dissolve * dissolve * _Converge;
                positionWS -= radial * converge;

                float front = dissolve * 1.08;
                float nearFront = 1.0 - saturate(abs(height01 - front) / 0.28);
                float suck = nearFront * dissolve * _Stretch;
                positionWS += axis * suck * (0.35 + height01);

                float lift = charge * 0.04 + dissolve * dissolve * 0.22;
                positionWS += axis * lift * height01;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 origin = _OriginWS.xyz;
                float3 axis = normalize(_AxisWS.xyz + 1e-5);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float height01;
                WarpAndHeight(positionWS, origin, axis, _Progress, height01);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.height01 = height01;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float progress = saturate(_Progress);
                float charge, dissolve;
                TeleportStages(progress, charge, dissolve);

                float time = _TimeParameters.y;
                float n = BodyNoise(input.positionWS, time);
                float front = dissolve * 1.08 + (n - 0.5) * _NoiseAmount * dissolve;
                clip(input.height01 - front + 0.001);

                float band = floor(input.height01 * 32.0);
                float glitchHash = Hash21(float2(band, floor(time * 20.0)));
                float glitch = step(1.0 - 0.22 * _Glitch * charge, glitchHash) * charge;
                float2 uv = input.uv;
                uv.x += (glitchHash - 0.5) * 0.07 * glitch;

                float2 chroma = float2(0.007, 0) * charge;
                half4 albedoSample;
                albedoSample.r = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + chroma).r;
                albedoSample.g = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).g;
                albedoSample.b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - chroma).b;
                albedoSample.a = 1;
                half3 albedo = albedoSample.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float fres = pow(1.0 - saturate(dot(normalWS, viewDir)), _RimPower);

                Light light = GetMainLight();
                float ndl = saturate(dot(normalWS, light.direction));
                half3 lit = albedo * (0.28 + 0.72 * ndl) * light.color;

                float edge = 1.0 - saturate((input.height01 - front) / max(_EdgeWidth, 0.001));
                edge = pow(saturate(edge), 1.6);

                float scan = sin(input.height01 * _ScanCount * 6.2831853 + time * 14.0);
                scan = pow(saturate(scan * 0.5 + 0.5), 7.0) * _ScanStrength;

                float sweep = frac(time * 0.45 + 0.12);
                float sweepBand = saturate(1.0 - abs(input.height01 - sweep) / 0.045);
                sweepBand *= (1.0 - dissolve);

                float luma = dot(albedo, half3(0.22h, 0.71h, 0.07h));
                half3 holo = luma * _GlowColor.rgb * 1.15;
                holo += _CoreColor.rgb * (0.18 + fres * 0.85);
                holo += _GlowColor.rgb * scan * charge;
                holo += _EdgeColor.rgb * sweepBand * 1.4;

                half3 color = lerp(lit, holo, charge * 0.92);
                color = lerp(color, _EdgeColor.rgb, edge * dissolve);
                color += _EdgeColor.rgb * edge * dissolve * 1.8;
                color += _GlowColor.rgb * fres * (0.2 + charge * 0.7);

                float fadeOut = 1.0 - smoothstep(0.82, 1.0, progress);
                half alpha = lerp(1.0h, 0.42h, charge) * fadeOut;
                alpha = saturate(alpha + edge * dissolve);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "TeleportInterior"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Progress;
                float4 _OriginWS;
                float4 _AxisWS;
                float _Height;
                float _NoiseScale;
                float _NoiseAmount;
                float _EdgeWidth;
                half4 _GlowColor;
                half4 _EdgeColor;
                half4 _CoreColor;
                half _RimPower;
                half _ScanCount;
                half _ScanStrength;
                half _Stretch;
                half _Converge;
                half _Glitch;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float height01 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float progress = saturate(_Progress);
                float dissolve = saturate((progress - 0.18) / 0.82);
                float charge = saturate(progress / 0.22);

                float3 origin = _OriginWS.xyz;
                float3 axis = normalize(_AxisWS.xyz + 1e-5);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float height = max(_Height, 0.001);
                float h = dot(positionWS - origin, axis);
                float height01 = saturate(h / height);

                float3 along = origin + axis * h;
                positionWS -= (positionWS - along) * (dissolve * dissolve * _Converge);
                float front = dissolve * 1.08;
                float nearFront = 1.0 - saturate(abs(height01 - front) / 0.28);
                positionWS += axis * nearFront * dissolve * _Stretch * (0.35 + height01);
                positionWS += axis * (charge * 0.04 + dissolve * dissolve * 0.22) * height01;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.height01 = height01;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float progress = saturate(_Progress);
                float charge = saturate(progress / 0.22);
                float dissolve = saturate((progress - 0.18) / 0.82);
                if (charge < 0.04)
                    return 0;

                float time = _TimeParameters.y;
                float n = ValueNoise(input.positionWS.xz * _NoiseScale + time * 0.55);
                float front = dissolve * 1.08 + (n - 0.5) * _NoiseAmount * dissolve;
                clip(input.height01 - front + 0.001);

                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float fres = pow(1.0 - saturate(dot(normalize(-input.normalWS), viewDir)), 1.6);
                float fadeOut = 1.0 - smoothstep(0.82, 1.0, progress);
                half3 energy = _CoreColor.rgb * (0.35 + fres) * charge * fadeOut;
                energy += _GlowColor.rgb * dissolve * 0.25;
                return half4(energy, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
