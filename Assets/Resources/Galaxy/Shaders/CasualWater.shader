Shader "BackHome/CasualWater"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor("Shallow", Color) = (0.42, 0.92, 0.98, 1)
        _DeepColor("Deep", Color) = (0.10, 0.38, 0.82, 1)
        _FoamColor("Foam", Color) = (1.0, 0.99, 0.96, 1)
        _Saturation("Saturation", Range(0.8, 1.6)) = 1.18

        [Header(Waves)]
        _WaveHeight("Wave Height", Range(0, 2)) = 0.35
        _WaveScale("Wave Scale", Range(0.02, 0.8)) = 0.12
        _WaveSpeed("Wave Speed", Range(0, 4)) = 0.85

        [Header(Foam)]
        _FoamScale("Foam Scale", Range(0.05, 1.5)) = 0.28
        _FoamSpeed("Foam Speed", Range(0, 2)) = 0.22
        _FoamCutoff("Foam Cutoff", Range(0.2, 0.95)) = 0.58
        _FoamSoftness("Foam Softness", Range(0.01, 0.4)) = 0.08
        _ShoreWidth("Shore Foam Width", Range(0.2, 12)) = 3.2
        _HorizonFoam("Horizon Foam", Range(0, 1)) = 0.45

        [Header(Light)]
        _ShadeFloor("Shadow Brightness", Range(0.3, 1)) = 0.62
        _ShadeSoftness("Shade Softness", Range(0, 1)) = 0.28
        _SpecularColor("Specular", Color) = (1, 1, 1, 0.7)
        _SpecularSize("Specular Size", Range(0.02, 0.6)) = 0.16
        _SparkleAmount("Sparkles", Range(0, 1)) = 0.35
        _SparkleScale("Sparkle Scale", Range(0.2, 8)) = 2.4
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 2.4

        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Transparent-1"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVert
            #pragma fragment WaterFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                half _Saturation;
                half _WaveHeight;
                half _WaveScale;
                half _WaveSpeed;
                half _FoamScale;
                half _FoamSpeed;
                half _FoamCutoff;
                half _FoamSoftness;
                half _ShoreWidth;
                half _HorizonFoam;
                half _ShadeFloor;
                half _ShadeSoftness;
                half4 _SpecularColor;
                half _SpecularSize;
                half _SparkleAmount;
                half _SparkleScale;
                half _FresnelPower;
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float wave : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float Noise2(float2 p)
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

            float NoiseTri(float3 p, float3 n)
            {
                float3 w = abs(n);
                w = w / max(w.x + w.y + w.z, 1e-5);
                return Noise2(p.yz) * w.x + Noise2(p.xz) * w.y + Noise2(p.xy) * w.z;
            }

            float FbmTri(float3 p, float3 n)
            {
                float v = NoiseTri(p, n);
                v += NoiseTri(p * 2.03, n) * 0.5;
                return v * 0.666;
            }

            float WaveField(float3 posWS, float t)
            {
                float3 p = posWS * _WaveScale;
                float w = sin(p.x * 1.07 + t);
                w += sin(p.y * 0.93 + t * 1.17) * 0.72;
                w += sin(p.z * 1.11 + t * 0.83) * 0.88;
                w += sin((p.x + p.z) * 0.61 + t * 1.35) * 0.46;
                return w * 0.25;
            }

            half3 ApplySaturation(half3 c, half sat)
            {
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                return lerp(luma.xxx, c, sat);
            }

            Varyings WaterVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                float t = _Time.y * _WaveSpeed;
                float wave = WaveField(posWS, t);

                posWS += nrmWS * wave * _WaveHeight;
                o.positionWS = posWS;
                o.normalWS = nrmWS;
                o.wave = wave;
                o.positionCS = TransformWorldToHClip(posWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half ShoreFoam(float4 positionCS)
            {
                float2 uv = GetNormalizedScreenSpaceUV(positionCS);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float waterZ = LinearEyeDepth(positionCS.z, _ZBufferParams);
                float diff = max(sceneZ - waterZ, 0.0);
                return saturate(1.0 - diff / max(_ShoreWidth, 0.0001h));
            }

            half4 WaterFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 n = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float t = _Time.y * _WaveSpeed;
                float foamT = _Time.y * _FoamSpeed;
                float wave = WaveField(input.positionWS, t);
                wave = wave * 0.72 + input.wave * 0.28;

                float ndv = saturate(dot(n, viewDir));
                float fresnel = pow(1.0 - ndv, _FresnelPower);

                // Two-tone cartoon body: deep in facing water, shallow on edges and crests.
                float shallowMask = saturate(fresnel * 0.85 + wave * 0.35 + 0.18);
                half3 waterRgb = lerp(_DeepColor.rgb, _ShallowColor.rgb, shallowMask);

                Light light = GetMainLight();
                half ndl = saturate(dot(n, light.direction) * 0.5h + 0.5h);
                half shade = lerp(_ShadeFloor, 1.0h, smoothstep(0.42h - _ShadeSoftness, 0.42h + _ShadeSoftness, ndl));
                waterRgb *= lerp(half3(0.72h, 0.78h, 0.95h), light.color, 0.55h) * shade;

                // Scrolling foam patches + wave-crest caps.
                float3 foamPos = input.positionWS * _FoamScale + float3(foamT * 0.65, foamT * 0.2, foamT * 0.4);
                float foamNoise = FbmTri(foamPos, n);
                float crest = saturate(wave * 0.55 + 0.45);
                float shore = ShoreFoam(input.positionCS);
                shore = saturate(shore * (0.45 + foamNoise * 0.85));

                float foamSrc = foamNoise * 0.55 + crest * 0.5 + shore * 1.15 + fresnel * _HorizonFoam;
                float cut = _FoamCutoff;
                float foam = smoothstep(cut, cut + _FoamSoftness, foamSrc);
                float foamHi = smoothstep(cut + 0.12, cut + 0.12 + _FoamSoftness, foamSrc);

                half3 foamRgb = lerp(_FoamColor.rgb * 0.86h, _FoamColor.rgb, foamHi);
                waterRgb = lerp(waterRgb, foamRgb, foam);

                // Chunky specular blob + twinkling sparkles.
                float3 halfDir = normalize(light.direction + viewDir);
                half nh = saturate(dot(n, halfDir));
                half specThresh = 1.0h - saturate(_SpecularSize);
                half spec = smoothstep(specThresh - 0.04h, specThresh + 0.02h, nh) * shade;

                float3 sparkP = floor(input.positionWS * _SparkleScale + foamT * 0.35);
                float sparkCell = Hash21(sparkP.xy + sparkP.z);
                float twinkle = sin(_Time.y * 6.0 + sparkCell * 6.2831) * 0.5 + 0.5;
                half spark = step(1.0h - _SparkleAmount * 0.35h, sparkCell) * step(0.55h, twinkle) * shade;

                waterRgb += _SpecularColor.rgb * _SpecularColor.a * spec;
                waterRgb += _FoamColor.rgb * spark * 0.85h;

                waterRgb = ApplySaturation(waterRgb, _Saturation);
                waterRgb = MixFog(waterRgb, input.fogFactor);
                return half4(waterRgb, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
