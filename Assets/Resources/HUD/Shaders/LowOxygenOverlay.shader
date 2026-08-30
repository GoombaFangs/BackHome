Shader "BackHome/Hud/LowOxygenOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex("Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)

        _FrostColor("Frost Color", Color) = (0.78, 0.92, 1, 1)
        _FrostAmount("Frost Amount", Range(0, 1)) = 0
        _FrostScale("Frost Scale", Float) = 3.2
        _FrostSoftness("Frost Softness", Range(0.05, 1)) = 0.42
        _ScrollSpeed("Scroll Speed", Float) = 0.045
        _Pulse("Pulse", Range(0, 1)) = 0

        _VignetteColor("Vignette Color", Color) = (0, 0, 0, 1)
        _VignetteAmount("Vignette Amount", Range(0, 1)) = 0
        _VignetteSoftness("Vignette Softness", Range(0.05, 2)) = 0.6

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
            fixed4 _Color;
            float4 _ClipRect;

            fixed4 _FrostColor;
            half _FrostAmount;
            half _FrostScale;
            half _FrostSoftness;
            half _ScrollSpeed;
            half _Pulse;

            fixed4 _VignetteColor;
            half _VignetteAmount;
            half _VignetteSoftness;

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

                float coverage = tex2D(_MainTex, uv).a;

                // Frost: soft drifting cloud patches (breath fogging the glass), not a flat tint.
                float2 scroll = float2(t * _ScrollSpeed, t * _ScrollSpeed * -0.6);
                float2 nUv = uv * max(_FrostScale, 0.5);
                float n1 = ValueNoise(nUv + scroll);
                float n2 = ValueNoise(nUv * 2.3 + scroll * 1.7 + 11.0);
                float n3 = ValueNoise(nUv * 0.55 - scroll * 0.6 + 33.0);
                float clouds = saturate(n1 * 0.5 + n2 * 0.32 + n3 * 0.18);
                // _FrostSoftness is a contrast control here: low = even haze, high = clumpy patches.
                float sharpness = lerp(0.15, 3.0, saturate((float)_FrostSoftness));
                float shaped = saturate((clouds - 0.5) * sharpness + 0.5);
                float frostAlpha = saturate(shaped * _FrostAmount * (0.85 + _Pulse * 0.15));

                // Vignette: darkens toward the edges, tightening as danger grows.
                float2 centered = uv * 2.0 - 1.0;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                centered.x *= aspect;
                float dist = length(centered);
                float outer = lerp(1.75, 0.55, (float)_VignetteAmount);
                float inner = max(outer - (float)_VignetteSoftness, 0.02);
                float vignetteMask = smoothstep(inner, outer, dist) * saturate(_VignetteAmount * 1.5);

                // Composite frost over vignette (standard alpha-over), then un-premultiply for UI blending.
                float outAlpha = saturate(frostAlpha + vignetteMask * (1.0 - frostAlpha));
                half3 outColor = _FrostColor.rgb * frostAlpha + _VignetteColor.rgb * vignetteMask * (1.0 - frostAlpha);
                outColor /= max(outAlpha, 0.0001);

                half alpha = outAlpha * coverage * input.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                clip(alpha - 0.001);
                return half4(outColor, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
