Shader "BackHome/Hud/LowOxygenOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex("Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)

        _FrostColor("Frost Tint", Color) = (0.85, 0.93, 1, 1)
        _FrostAmount("Frost / Blur Amount", Range(0, 1)) = 0
        _FrostTintStrength("Frost Tint Strength", Range(0, 1)) = 0.22
        _BlurRadius("Blur Radius (screen-UV)", Range(0, 0.05)) = 0.019
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

            // Populated by URP each frame (requires "Opaque Texture" enabled on the URP asset,
            // which this project already has); lets us blur the real frame behind the HUD
            // instead of faking it with a tinted noise pattern.
            sampler2D _CameraOpaqueTexture;

            fixed4 _FrostColor;
            half _FrostAmount;
            half _FrostTintStrength;
            half _BlurRadius;
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

            // Cheap two-ring disc blur of the real frame behind the HUD - a genuine
            // "camera out of focus" softening rather than a flat tinted haze.
            half3 SampleBlurredScene(float2 uv, float radius, float aspect)
            {
                half3 sum = tex2D(_CameraOpaqueTexture, uv).rgb;
                half total = 1.0;

                const int taps = 8;
                UNITY_UNROLL
                for (int i = 0; i < taps; i++)
                {
                    float angle = (i / (float)taps) * 6.2831853 + 0.3927; // 22.5 degree offset between rings
                    float2 dir = float2(cos(angle), sin(angle));
                    float2 aspectDir = float2(dir.x / max(aspect, 0.0001), dir.y);

                    sum += tex2D(_CameraOpaqueTexture, uv + aspectDir * radius).rgb;
                    total += 1.0;

                    sum += tex2D(_CameraOpaqueTexture, uv + aspectDir * radius * 0.5).rgb;
                    total += 1.0;
                }

                return sum / total;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float coverage = tex2D(_MainTex, uv).a;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);

                float breath = 1.0 + _Pulse * 0.12;
                half3 blurred = SampleBlurredScene(uv, _BlurRadius * _FrostAmount * breath, aspect);
                half3 frostColor = lerp(blurred, blurred * _FrostColor.rgb + _FrostColor.rgb * 0.08, _FrostTintStrength);
                float frostAlpha = saturate(_FrostAmount * (0.92 + _Pulse * 0.08));

                // Vignette: darkens toward the edges, tightening as danger grows.
                float2 centered = uv * 2.0 - 1.0;
                centered.x *= aspect;
                float dist = length(centered);
                float outer = lerp(1.75, 0.55, (float)_VignetteAmount);
                float inner = max(outer - (float)_VignetteSoftness, 0.02);
                float vignetteMask = smoothstep(inner, outer, dist) * saturate(_VignetteAmount * 1.5);

                // Composite frost over vignette (standard alpha-over), then un-premultiply for UI blending.
                float outAlpha = saturate(frostAlpha + vignetteMask * (1.0 - frostAlpha));
                half3 outColor = frostColor * frostAlpha + _VignetteColor.rgb * vignetteMask * (1.0 - frostAlpha);
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
