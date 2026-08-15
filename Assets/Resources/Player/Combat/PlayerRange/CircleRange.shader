Shader "BackHome/CircleRange"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, 1, 1, 0.65)
        _Radius("Radius", Range(0, 0.5)) = 0.45
        _Thickness("Thickness", Range(0, 0.2)) = 0.018
        _Softness("Softness", Range(0, 0.1)) = 0.005
        _Opacity("Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Radius;
                half _Thickness;
                half _Softness;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                float dist = length(centered);
                float ring = abs(dist - _Radius) - _Thickness;

                // Screen-space AA floored by Softness so the ring stays readable at distance.
                float aa = max(fwidth(dist), (float)_Softness);
                float mask = 1.0 - smoothstep(-aa, aa, ring);

                half alpha = mask * _Color.a * _Opacity;
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
