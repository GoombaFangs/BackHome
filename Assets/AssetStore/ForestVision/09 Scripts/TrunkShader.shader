Shader "ForestVision/FV_TrunkShader"
{
    Properties
    {
        [Header(Global Control)] _Intensity ("Overall Intensity", Range(5, 1)) = 1
        [Space(10)]

        [Header(Main Texture Controls)] _Color("Main Tint Color", Color) = (0.5,0.5,0.5,1)
        _MainTex ("Base Texture", 2D) = "white" {}
        _Bump ("Normal", 2D) = "bump" {}
        _BumpPower ("Normal Power", Range (0.1, 2)) = 1

        [Header(Detail Texture Controls)] _Detail ("Detail", 2D) = "gray" {}
        _DetailPower ("Detail Power", Range (0, 1)) = 0.5

        [Space(10)]

        [Header(Side Texture Controls)] _SideDirection ("Side Direction", Vector) = (-1,0,1)
        _SideLevel ("Side Level", Range(0,1) ) = 0.3
        _SideDepth ("Side Depth", Range(0,1)) = 0.7
        _SideColor ("Side Color", Color) = (0.0,0.392,0.0,1.0)
        _SideTex ("Side Texture", 2D) = "white" {}

        [Space(10)]

        [Header(Top Texture Controls)] _TopDirection ("Top Direction", Vector) = (0,1.5,0)
        _TopLevel ("Top Level", Range(0,1) ) = 0.2
        _TopDepth ("Top Depth", Range(0,1)) = 0.7
        _TopColor ("Top Color", Color) = (1,0.894,0.710,1.0)
        _TopTex ("Top Texture", 2D) = "white" {}

        [Space(10)]

        [Header(Bottom Texture Controls)] _BottomDirection ("Bottom Direction", Vector) = (0,-1.5,0)
        _BottomLevel ("Bottom Level", Range(0,1) ) = 0.3
        _BottomDepth ("Bottom Depth", Range(0,1)) = 0.7
        _BottomColor ("Bottom Color", Color) = (0.502,0.502,0.0,1.0)
        _BottomTex ("Bottom Texture", 2D) = "white" {}

        [HideInInspector] _Depth ("Depth", Range(0,0.2)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TrunkVert
            #pragma fragment TrunkFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Bump); SAMPLER(sampler_Bump);
            TEXTURE2D(_Detail); SAMPLER(sampler_Detail);
            TEXTURE2D(_SideTex); SAMPLER(sampler_SideTex);
            TEXTURE2D(_TopTex); SAMPLER(sampler_TopTex);
            TEXTURE2D(_BottomTex); SAMPLER(sampler_BottomTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Bump_ST;
                float4 _Detail_ST;
                float4 _SideTex_ST;
                float4 _TopTex_ST;
                float4 _BottomTex_ST;
                half4 _Color;
                half _BumpPower;
                half _DetailPower;
                float4 _SideDirection;
                float _SideLevel;
                float _SideDepth;
                half4 _SideColor;
                float4 _TopDirection;
                float _TopLevel;
                float _TopDepth;
                half4 _TopColor;
                float4 _BottomDirection;
                float _BottomLevel;
                float _BottomDepth;
                half4 _BottomColor;
                float _Depth;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half3 LambertLighting(half3 albedo, float3 normalWS, float3 positionWS)
            {
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = mainLight.color * (ndl * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                lighting += SampleSH(normalWS);

                #ifdef _ADDITIONAL_LIGHTS
                uint lightsCount = GetAdditionalLightsCount();
                UNITY_LOOP
                for (uint li = 0u; li < lightsCount; li++)
                {
                    Light addLight = GetAdditionalLight(li, positionWS);
                    half addNdl = saturate(dot(normalWS, addLight.direction));
                    lighting += addLight.color * (addNdl * addLight.distanceAttenuation * addLight.shadowAttenuation);
                }
                #endif

                return albedo * lighting;
            }

            Varyings TrunkVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = nrmInputs.normalWS;
                real tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = float4(nrmInputs.tangentWS, tangentSign);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 TrunkFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 uvMain = TRANSFORM_TEX(input.uv, _MainTex);
                float2 uvBump = TRANSFORM_TEX(input.uv, _Bump);
                float2 uvDetail = TRANSFORM_TEX(input.uv, _Detail);
                float2 uvSide = TRANSFORM_TEX(input.uv, _SideTex);
                float2 uvTop = TRANSFORM_TEX(input.uv, _TopTex);
                float2 uvBottom = TRANSFORM_TEX(input.uv, _BottomTex);

                half3 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain).rgb;
                half3 sideColor = SAMPLE_TEXTURE2D(_SideTex, sampler_SideTex, uvSide).rgb;
                half3 topColor = SAMPLE_TEXTURE2D(_TopTex, sampler_TopTex, uvTop).rgb;
                half3 bottomColor = SAMPLE_TEXTURE2D(_BottomTex, sampler_BottomTex, uvBottom).rgb;

                float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                float3x3 tangentToWorld = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_Bump, sampler_Bump, uvBump), 1.0h);
                normalTS = lerp(half3(0.0h, 0.0h, 1.0h), normalTS, _BumpPower);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                half intensity = max(_Intensity, 0.0001);
                half difference = saturate((dot(normalWS, _SideDirection.xyz) - lerp(1.0h, -1.0h, _SideLevel)) / _SideDepth) / intensity;
                half difference2 = saturate((dot(normalWS, _TopDirection.xyz) - lerp(1.0h, -1.0h, _TopLevel)) / _TopDepth) / intensity;
                half difference3 = saturate((dot(normalWS, _BottomDirection.xyz) - lerp(1.0h, -1.0h, _BottomLevel)) / _BottomDepth) / intensity;

                if (_DetailPower > 0.0h)
                {
                    baseColor *= SAMPLE_TEXTURE2D(_Detail, sampler_Detail, uvDetail).rgb * 2.0h + _DetailPower;
                }

                sideColor = (difference * (sideColor * 2.0h)) * _SideColor.rgb + (1.0h - difference);
                topColor = (difference2 * topColor) * _TopColor.rgb + (1.0h - difference2);
                bottomColor = (difference3 * (bottomColor * 2.0h)) * _BottomColor.rgb + (1.0h - difference3);

                half3 albedo = (sideColor * topColor * bottomColor) * baseColor * _Color.rgb;
                half3 color = LambertLighting(albedo, normalWS, input.positionWS);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Bump_ST;
                float4 _Detail_ST;
                float4 _SideTex_ST;
                float4 _TopTex_ST;
                float4 _BottomTex_ST;
                half4 _Color;
                half _BumpPower;
                half _DetailPower;
                float4 _SideDirection;
                float _SideLevel;
                float _SideDepth;
                half4 _SideColor;
                float4 _TopDirection;
                float _TopLevel;
                float _TopDepth;
                half4 _TopColor;
                float4 _BottomDirection;
                float _BottomLevel;
                float _BottomDepth;
                half4 _BottomColor;
                float _Depth;
                float _Intensity;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                positionCS = ApplyShadowClamping(positionCS);
                return positionCS;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Bump_ST;
                float4 _Detail_ST;
                float4 _SideTex_ST;
                float4 _TopTex_ST;
                float4 _BottomTex_ST;
                half4 _Color;
                half _BumpPower;
                half _DetailPower;
                float4 _SideDirection;
                float _SideLevel;
                float _SideDepth;
                half4 _SideColor;
                float4 _TopDirection;
                float _TopLevel;
                float _TopDepth;
                half4 _TopColor;
                float4 _BottomDirection;
                float _BottomLevel;
                float _BottomDepth;
                half4 _BottomColor;
                float _Depth;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
