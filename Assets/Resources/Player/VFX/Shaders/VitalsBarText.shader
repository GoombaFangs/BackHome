Shader "BackHome/Hud/VitalsBarText"
{
    Properties
    {
        [PerRendererData] _MainTex("Font Atlas", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _FaceColor("Face Color", Color) = (1, 1, 1, 1)
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 0.85)
        _OutlineWidth("Outline", Range(0, 0.5)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay+100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
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
                half4 _FaceColor;
                half4 _OutlineColor;
                half _OutlineWidth;
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
                output.color = input.color * _Color * _FaceColor;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half sdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half smoothing = max(fwidth(sdf) * 0.75, 0.04);
                half fill = smoothstep(0.5 - smoothing, 0.5 + smoothing, sdf);
                half outline = smoothstep(0.5 - _OutlineWidth - smoothing, 0.5 - _OutlineWidth + smoothing, sdf);
                half4 color = lerp(_OutlineColor, input.color, fill);
                color.a *= max(fill, outline * _OutlineColor.a);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
