Shader "BackHome/ToonOutline"
{
    Properties
    {
        [Header(Ink)]
        _OutlineColor("Ink Color", Color) = (0.04, 0.03, 0.07, 1)
        _OutlineThickness("Thickness (pixels)", Range(1, 4)) = 2
        _SilhouetteStrength("Silhouette", Range(0, 1)) = 1
        _CreaseStrength("Creases", Range(0, 1)) = 0.55

        [Header(Depth Silhouette)]
        _DepthSensitivity("Depth Sensitivity", Range(0.001, 0.08)) = 0.006
        _DepthSoftness("Depth Softness", Range(0.05, 2)) = 1

        [Header(Surface Creases)]
        _NormalThreshold("Crease Angle", Range(0.05, 0.8)) = 0.22
        _NormalSoftness("Crease Softness", Range(0.01, 0.4)) = 0.12

        [Header(Distance)]
        _FadeStart("Fade Start", Float) = 80
        _FadeEnd("Fade End", Float) = 180
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ToonOutline"
            ZTest Always
            ZWrite Off
            Cull Off

            // Bit 0 is set on visible planet pixels. Ink stays on characters and props.
            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask 0
                Comp NotEqual
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Kept as plain uniforms. The fullscreen pass draws with a property block that only
            // sets _BlitTexture; a UnityPerMaterial cbuffer gets dropped on that draw.
            float4 _OutlineColor;
            float _OutlineThickness;
            float _SilhouetteStrength;
            float _CreaseStrength;
            float _DepthSensitivity;
            float _DepthSoftness;
            float _NormalThreshold;
            float _NormalSoftness;
            float _FadeStart;
            float _FadeEnd;

            float RawDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv).r;
            }

            float EyeAt(float2 uv)
            {
                return LinearEyeDepth(RawDepth(uv), _ZBufferParams);
            }

            float3 SurfaceNormal(float2 uv, float2 offset)
            {
                float raw0 = RawDepth(uv);
                float rawX = RawDepth(uv + float2(offset.x, 0));
                float rawY = RawDepth(uv + float2(0, offset.y));

                float sky = step(0.995, Linear01Depth(raw0, _ZBufferParams));
                sky += step(0.995, Linear01Depth(rawX, _ZBufferParams));
                sky += step(0.995, Linear01Depth(rawY, _ZBufferParams));
                if (sky > 0.5)
                    return float3(0, 0, 0);

                float3 p0 = ComputeViewSpacePosition(uv, raw0, unity_CameraInvProjection);
                float3 pX = ComputeViewSpacePosition(uv + float2(offset.x, 0), rawX, unity_CameraInvProjection);
                float3 pY = ComputeViewSpacePosition(uv + float2(0, offset.y), rawY, unity_CameraInvProjection);
                float3 c = cross(pX - p0, pY - p0);
                float lenSq = dot(c, c);
                return lenSq > 1e-8 ? c * rsqrt(lenSq) : float3(0, 0, 0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 texel = _CameraDepthTexture_TexelSize.xy * max(_OutlineThickness, 1.0);

                float eyeC = EyeAt(uv);
                float eyeL = EyeAt(uv + float2(-texel.x, 0));
                float eyeR = EyeAt(uv + float2( texel.x, 0));
                float eyeD = EyeAt(uv + float2(0, -texel.y));
                float eyeU = EyeAt(uv + float2(0,  texel.y));

                float scale = max(eyeC, 0.5);
                float delta = max(max(eyeL, eyeR), max(eyeD, eyeU)) - eyeC;
                delta = max(delta, 0.0);
                float spread = max(max(abs(eyeL - eyeC), abs(eyeR - eyeC)), max(abs(eyeD - eyeC), abs(eyeU - eyeC)));

                // Derivative catches silhouettes even when the manual taps land inside a flat pixel.
                float deriv = max(abs(ddx(eyeC)), abs(ddy(eyeC)));
                delta = max(delta, deriv);
                spread = max(spread, deriv);

                float gate = scale * _DepthSensitivity;
                float depthHigh = gate * (1.0 + max(_DepthSoftness, 0.001));
                float depthMask = smoothstep(gate, depthHigh, delta);
                float spreadMask = smoothstep(gate, depthHigh, spread);

                float3 n0 = SurfaceNormal(uv, texel);
                float3 n1 = SurfaceNormal(uv + texel, texel);
                float crease = 0;
                if (dot(n0, n0) > 0.5 && dot(n1, n1) > 0.5)
                {
                    float angle = 1.0 - saturate(dot(n0, n1));
                    float creaseHigh = _NormalThreshold + max(_NormalSoftness, 0.001);
                    crease = smoothstep(_NormalThreshold, creaseHigh, angle);
                }

                crease *= saturate(1.0 - spreadMask);

                float fadeEnd = max(_FadeEnd, _FadeStart + 0.01);
                float fade = 1.0 - smoothstep(_FadeStart, fadeEnd, eyeC);
                float outline = max(depthMask * _SilhouetteStrength, crease * _CreaseStrength) * fade;
                outline = saturate(outline);

                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 ink = _OutlineColor.rgb;
                float3 color = lerp(scene, ink, outline * _OutlineColor.a);
                return float4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
