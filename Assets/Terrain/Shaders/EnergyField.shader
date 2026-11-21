// Transparent glowing force-field for URP (sci-fi outpost barriers).
// Soft base tint, bright edges, slowly scrolling scan lines and a faint hex-ish shimmer.
Shader "Bobery/Energy Field"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.25, 0.85, 1.6, 1)
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.12
        _EdgeWidth ("Edge Width", Range(0.001, 0.3)) = 0.06
        _EdgeAlpha ("Edge Alpha", Range(0, 1)) = 0.9
        _LineDensity ("Scan Line Density", Float) = 18
        _LineSpeed ("Scan Line Speed", Float) = 0.6
        _LineAlpha ("Scan Line Alpha", Range(0, 1)) = 0.25
        _Shimmer ("Shimmer", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _BaseAlpha;
                half _EdgeWidth;
                half _EdgeAlpha;
                float _LineDensity;
                float _LineSpeed;
                half _LineAlpha;
                half _Shimmer;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                // Bright frame along the panel edges (mostly top and bottom).
                float ex = min(uv.x, 1.0 - uv.x);
                float ey = min(uv.y, 1.0 - uv.y);
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, ey);
                edge = max(edge, (1.0 - smoothstep(0.0, _EdgeWidth * 0.5, ex)) * 0.6);

                // Scan lines drifting upward in world space (continuous across panels).
                float t = _Time.y;
                float lines = sin((i.positionWS.y * _LineDensity) - t * _LineSpeed * 6.2831) * 0.5 + 0.5;
                lines = pow(lines, 6.0);

                // Shimmer: slow interference pattern along the panel.
                float s = sin(i.positionWS.x * 3.1 + i.positionWS.z * 2.7 + t * 1.3) *
                          sin(i.positionWS.y * 4.3 - t * 0.9);
                s = s * 0.5 + 0.5;

                float a = _BaseAlpha + edge * _EdgeAlpha + lines * _LineAlpha + s * _Shimmer * 0.5;
                a *= 0.85 + 0.15 * sin(t * 2.0); // gentle pulse
                half3 col = _Color.rgb * (0.6 + edge * 0.8 + lines * 0.4);
                col = MixFogColor(col, half3(0, 0, 0), i.fogFactor); // additive: fade toward black in fog
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
