Shader "BackHome/PlanetTilesSplat"
{
    Properties
    {
        _Splat0("Splat 0 (R)", 2D) = "white" {}
        _Splat1("Splat 1 (G)", 2D) = "white" {}
        _Splat2("Splat 2 (B)", 2D) = "white" {}
        _Splat3("Splat 3 (A)", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _BlendSharpness("Blend Sharpness", Range(1, 4)) = 1.65
        _ShadeFloor("Shade Floor", Range(0.3, 1)) = 0.88
        _ShadeCeil("Shade Ceil", Range(0.5, 1.2)) = 1.06
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

            TEXTURE2D(_Splat0);
            TEXTURE2D(_Splat1);
            TEXTURE2D(_Splat2);
            TEXTURE2D(_Splat3);
            SAMPLER(sampler_Splat0);

            CBUFFER_START(UnityPerMaterial)
                float4 _Splat0_ST;
                half4 _BaseColor;
                half _BlendSharpness;
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
                o.uv = TRANSFORM_TEX(input.uv, _Splat0);
                o.color = input.color;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 w = max(input.color, 0);
                w = pow(w, half4(_BlendSharpness, _BlendSharpness, _BlendSharpness, _BlendSharpness));
                half sum = w.r + w.g + w.b + w.a + 1e-5;
                w /= sum;

                half4 c0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, input.uv);
                half4 c1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, input.uv);
                half4 c2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat0, input.uv);
                half4 c3 = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat0, input.uv);

                half h0 = dot(c0.rgb, half3(0.299, 0.587, 0.114));
                half h1 = dot(c1.rgb, half3(0.299, 0.587, 0.114));
                half h2 = dot(c2.rgb, half3(0.299, 0.587, 0.114));
                half h3 = dot(c3.rgb, half3(0.299, 0.587, 0.114));
                half4 hw = w * half4(h0, h1, h2, h3);
                half ma = max(max(hw.r, hw.g), max(hw.b, hw.a));
                hw = max(hw - (ma - 0.18), 0) * w;
                half hSum = hw.r + hw.g + hw.b + hw.a + 1e-5;
                hw /= hSum;

                half3 albedo = c0.rgb * hw.r + c1.rgb * hw.g + c2.rgb * hw.b + c3.rgb * hw.a;
                half3 n = normalize(input.normalWS);
                half3 lightDir = normalize(half3(0.35, 0.9, 0.25));
                half ndl = saturate(dot(n, lightDir));
                half shade = lerp(_ShadeFloor, _ShadeCeil, ndl);
                return half4(albedo * _BaseColor.rgb * shade, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
