Shader "BackHome/CasualToon"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)

        [Header(Cel Shading)]
        _ShadeSteps("Shade Steps", Range(2, 5)) = 3
        _ShadeSoftness("Band Softness", Range(0, 1)) = 0.35
        _ShadeFloor("Shadow Brightness", Range(0.1, 1)) = 0.48
        _ShadowTint("Shadow Tint", Color) = (0.62, 0.68, 0.92, 1)
        _KeyTint("Key Light Tint", Color) = (1, 0.98, 0.94, 1)
        _Saturation("Saturation", Range(0.5, 1.8)) = 1.12
        _Contrast("Contrast", Range(0.7, 1.4)) = 1.05

        [Header(Specular)]
        _SpecularColor("Specular Color", Color) = (1, 1, 1, 0.55)
        _SpecularSize("Specular Size", Range(0.01, 1)) = 0.22
        _SpecularSoftness("Specular Softness", Range(0.001, 0.5)) = 0.06

        [Header(Rim)]
        _RimColor("Rim Color", Color) = (0.85, 0.95, 1, 0.35)
        _RimPower("Rim Power", Range(0.5, 10)) = 3.5
        _RimLightMask("Rim Light Mask", Range(0, 1)) = 0.75

        [Header(Ambient)]
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.35
        _AmbientTint("Ambient Tint", Color) = (0.55, 0.62, 0.85, 1)

        [Header(Emission)]
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionMap("Emission", 2D) = "white" {}

        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonVert
            #pragma fragment ToonFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _ShadeSteps;
                half _ShadeSoftness;
                half _ShadeFloor;
                half4 _ShadowTint;
                half4 _KeyTint;
                half _Saturation;
                half _Contrast;
                half4 _SpecularColor;
                half _SpecularSize;
                half _SpecularSoftness;
                half4 _RimColor;
                half _RimPower;
                half _RimLightMask;
                half _AmbientStrength;
                half4 _AmbientTint;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half3 ApplySaturation(half3 c, half sat)
            {
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                return lerp(luma.xxx, c, sat);
            }

            half3 ApplyContrast(half3 c, half contrast)
            {
                return saturate((c - 0.5h) * contrast + 0.5h);
            }

            half CelShade(half ndl, half steps, half softness, half floorVal)
            {
                steps = max(2.0h, steps);
                half bands = max(1.0h, steps - 1.0h);
                half t = saturate(ndl);
                half scaled = t * steps;

                half hard = saturate(floor(scaled) / bands);

                half edge = floor(scaled);
                half w = max(fwidth(scaled), 0.0001h) * lerp(0.2h, 2.2h, softness);
                half softEdge = smoothstep(edge - w, edge + w, scaled);
                half soft = saturate((edge - 1.0h + softEdge) / bands);

                half shade = lerp(hard, soft, softness);
                return lerp(floorVal, 1.0h, shade);
            }

            half ToonSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half size, half soft)
            {
                half3 halfDir = normalize(lightDir + viewDir);
                half nh = saturate(dot(normalWS, halfDir));
                half threshold = 1.0h - saturate(size);
                return smoothstep(threshold - soft, threshold + soft, nh);
            }

            half3 ShadeMainLight(half3 baseRgb, half3 normalWS, half3 viewDir, Light light)
            {
                half ndlRaw = dot(normalWS, light.direction);
                half ndl = saturate(ndlRaw);

                half shade = CelShade(ndl, _ShadeSteps, _ShadeSoftness, _ShadeFloor);

                half shadowAtten = light.shadowAttenuation * light.distanceAttenuation;
                half shadowCel = CelShade(shadowAtten, 2.0h, 0.25h, 0.0h);
                shade *= lerp(_ShadeFloor, 1.0h, shadowCel);

                half3 shadowCol = baseRgb * _ShadowTint.rgb;
                half3 litCol = baseRgb * _KeyTint.rgb;
                half3 diffuse = lerp(shadowCol, litCol, shade) * light.color;

                half specMask = ToonSpecular(normalWS, light.direction, viewDir, _SpecularSize, _SpecularSoftness);
                specMask *= shade * shadowCel;
                diffuse += _SpecularColor.rgb * _SpecularColor.a * specMask * light.color;

                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDir)), _RimPower);
                half rimLight = saturate(ndlRaw * 0.5h + 0.5h);
                half rim = fresnel * lerp(1.0h, rimLight, _RimLightMask);
                diffuse += _RimColor.rgb * _RimColor.a * rim;

                return diffuse;
            }

            half3 ShadeAdditionalLight(half3 baseRgb, half3 normalWS, Light light)
            {
                half ndl = saturate(dot(normalWS, light.direction));
                half shade = CelShade(ndl, max(2.0h, _ShadeSteps - 1.0h), _ShadeSoftness, 0.0h);
                half atten = light.distanceAttenuation * light.shadowAttenuation;
                return baseRgb * light.color * shade * atten * 0.55h;
            }

            Varyings ToonVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS);

                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS = nrmInputs.normalWS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.color = half4(input.color);
                o.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return o;
            }

            half4 ToonFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 baseRgb = albedoSample.rgb * _BaseColor.rgb * input.color.rgb;
                baseRgb = ApplySaturation(baseRgb, _Saturation);
                baseRgb = ApplyContrast(baseRgb, _Contrast);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 color = ShadeMainLight(baseRgb, normalWS, viewDir, mainLight);

                half3 sh = SampleSH(normalWS);
                color += baseRgb * sh * _AmbientStrength * _AmbientTint.rgb;

                #ifdef _ADDITIONAL_LIGHTS
                uint lightsCount = GetAdditionalLightsCount();
                UNITY_LOOP
                for (uint li = 0u; li < lightsCount; li++)
                {
                    Light addLight = GetAdditionalLight(li, input.positionWS);
                    color += ShadeAdditionalLight(baseRgb, normalWS, addLight);
                }
                #endif

                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                color += emission;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
