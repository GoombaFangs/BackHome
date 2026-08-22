Shader "BackHome/Hud/LowOxygenWarning"
{
    Properties
    {
        [PerRendererData] _MainTex("Font Atlas", 2D) = "white" {}
        _Color("Tint", Color) = (0.35, 0.82, 1, 1)
        _NoiseTex("Frost Noise", 2D) = "gray" {}

        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.35
        _NoiseScale("Noise Scale", Float) = 3.2
        _ScrollSpeed("Scroll Speed", Float) = 0.08
        _Distort("Distort", Range(0, 0.08)) = 0
        _Scanline("Scanline", Range(0, 1)) = 0.12
        _Glitch("Glitch", Range(0, 1)) = 0
        _FrostColor("Frost Color", Color) = (0.72, 0.93, 1, 1)
        _CrackColor("Crack Color", Color) = (0.08, 0.22, 0.38, 1)
        _Danger("Danger", Range(0, 1)) = 0
        _Pulse("Pulse", Range(0, 1)) = 0.5

        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NoiseTex;
            fixed4 _Color;
            float4 _ClipRect;
            half _NoiseStrength;
            half _NoiseScale;
            half _ScrollSpeed;
            half _Distort;
            half _Scanline;
            half _Glitch;
            fixed4 _FrostColor;
            fixed4 _CrackColor;
            half _Danger;
            half _Pulse;

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
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
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float t = _Time.y;
                float danger = saturate(_Danger);
                float pulse = saturate(_Pulse);

                float coverage = tex2D(_MainTex, uv).a;

                float2 scroll = float2(t * _ScrollSpeed, t * _ScrollSpeed * -0.22);
                float2 noiseUv = uv * max(_NoiseScale, 0.5) + scroll;
                float3 texNoise = tex2D(_NoiseTex, noiseUv * 0.22).rgb;
                float n1 = ValueNoise(noiseUv * 0.85);
                float n2 = ValueNoise(noiseUv * 1.7 + 11.0);
                float frost = saturate(texNoise.r * 0.55 + n1 * 0.3 + n2 * 0.15);

                half3 rgb = input.color.rgb;
                float frostMix = frost * _NoiseStrength * (0.28 + danger * 0.22 + pulse * 0.12);
                rgb = lerp(rgb, _FrostColor.rgb, frostMix);
                rgb = lerp(rgb, _CrackColor.rgb, (1.0 - frost) * _NoiseStrength * 0.12 * danger);

                float scan = 0.94 + 0.06 * sin((uv.y + t * 0.18) * 42.0);
                rgb *= lerp(1.0, scan, _Scanline * (0.35 + danger * 0.4));
                rgb *= 0.9 + pulse * 0.12;

                half alpha = coverage * input.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                clip(alpha - 0.001);
                return half4(rgb, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
