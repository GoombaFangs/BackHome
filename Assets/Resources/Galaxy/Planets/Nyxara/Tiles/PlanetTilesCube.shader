Shader "BackHome/PlanetTilesCube"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _ShadeFloor("Shade Floor", Range(0.3, 1)) = 0.55
        _ShadeCeil("Shade Ceil", Range(0.5, 1.2)) = 1.05
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
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _ShadeFloor;
                half _ShadeCeil;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.color = input.color;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 n = normalize(input.normalWS);
                // Soft directional shade so cube sides read as blocks.
                half3 lightDir = normalize(half3(0.35, 0.9, 0.25));
                half ndl = saturate(dot(n, lightDir));
                half shade = lerp(_ShadeFloor, _ShadeCeil, ndl);
                half3 rgb = tex.rgb * _BaseColor.rgb * input.color.rgb * shade;
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
