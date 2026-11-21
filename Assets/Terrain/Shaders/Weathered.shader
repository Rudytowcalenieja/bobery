// Weathered surface for URP: base colour with noise variation, dirt patches, vertical rain streaks,
// optional panel grid (HESCO mesh, wall seams) and optional stripes (corrugated metal, canvas folds).
// Patterns are in object-aligned metres (rotation follows the object, scale doesn't stretch them),
// so it works on scaled primitives and generated meshes without UVs.
Shader "Bobery/Weathered"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.5, 0.5, 0.45, 1)
        _ColorVariation ("Color Variation", Range(0, 1)) = 0.25
        _VariationScale ("Variation Scale", Float) = 0.6

        [Header(Dirt)]
        _GrimeColor ("Grime Color", Color) = (0.22, 0.19, 0.14, 1)
        _GrimeAmount ("Grime Patches", Range(0, 1)) = 0.3
        _StreakAmount ("Rain Streaks", Range(0, 1)) = 0.35

        [Header(Grid)]
        _GridColor ("Grid Color", Color) = (0.2, 0.18, 0.14, 1)
        _GridSize ("Grid Size (m)", Float) = 1
        _GridWidth ("Grid Line Width (m)", Float) = 0.03
        _GridStrength ("Grid Strength", Range(0, 1)) = 0

        [Header(Stripes)]
        _StripeAxis ("Stripe Axis (object space)", Vector) = (1, 0, 0, 0)
        _StripeFreq ("Stripes per Metre", Float) = 6
        _StripeStrength ("Stripe Strength", Range(0, 1)) = 0

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _Metallic ("Metallic", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _ColorVariation;
            float _VariationScale;
            half4 _GrimeColor;
            half _GrimeAmount;
            half _StreakAmount;
            half4 _GridColor;
            float _GridSize;
            float _GridWidth;
            half _GridStrength;
            float4 _StripeAxis;
            float _StripeFreq;
            half _StripeStrength;
            half _Smoothness;
            half _Metallic;
        CBUFFER_END

        float WHash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float WNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float a = WHash21(i);
            float b = WHash21(i + float2(1, 0));
            float c = WHash21(i + float2(0, 1));
            float d = WHash21(i + float2(1, 1));
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        float WFbm(float2 p)
        {
            return (WNoise(p) * 0.5 + WNoise(p * 2.03 + 17.1) * 0.25 + WNoise(p * 4.07 + 41.7) * 0.125) / 0.875;
        }

        // World position/normal -> object-aligned (rotation only) metres.
        void ObjectAligned(float3 positionWS, float3 normalWS, out float3 p, out float3 n, out float3 originWS)
        {
            float4x4 m = GetObjectToWorldMatrix();
            float3 ax = normalize(float3(m._m00, m._m10, m._m20));
            float3 ay = normalize(float3(m._m01, m._m11, m._m21));
            float3 az = normalize(float3(m._m02, m._m12, m._m22));
            originWS = float3(m._m03, m._m13, m._m23);
            float3 d = positionWS - originWS;
            p = float3(dot(d, ax), dot(d, ay), dot(d, az));
            n = float3(dot(normalWS, ax), dot(normalWS, ay), dot(normalWS, az));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 nWS = normalize(i.normalWS);
                float3 p, n, origin;
                ObjectAligned(i.positionWS, nWS, p, n, origin);
                float3 an = abs(n);

                // Planar coordinates on the dominant face.
                float2 uv = (an.x >= an.y && an.x >= an.z) ? p.zy : ((an.y >= an.z) ? p.xz : p.xy);
                float2 seed = origin.xz * 0.37;

                half3 col = _BaseColor.rgb;

                // Large colour variation.
                float v = WFbm((uv + seed) * _VariationScale);
                col *= 1.0 + (v - 0.5) * 2.0 * _ColorVariation;

                // Stripes (corrugation / folds), in object space so they follow the object.
                float sAxis = dot(p, normalize(_StripeAxis.xyz + 1e-5));
                float stripe = sin(sAxis * _StripeFreq * 6.2831853) * 0.5 + 0.5;
                col *= 1.0 - _StripeStrength * (1.0 - stripe) * 0.6;

                // Grid lines (panels / wire mesh).
                float2 g = abs(frac(uv / _GridSize + 0.5) - 0.5) * _GridSize; // metres to nearest line
                float gd = min(g.x, g.y);
                float grid = 1.0 - smoothstep(_GridWidth * 0.5, _GridWidth, gd);
                col = lerp(col, _GridColor.rgb, grid * _GridStrength);

                // Dirt patches.
                float grime = WFbm((uv + seed) * 0.9 + 11.0);
                grime = smoothstep(1.0 - _GrimeAmount, 1.0 - _GrimeAmount + 0.25, grime);
                col = lerp(col, _GrimeColor.rgb, grime * 0.75);

                // Vertical rain streaks on walls.
                float vertical = 1.0 - an.y;
                float streak = WNoise(float2(uv.x * 5.0 + seed.x, p.y * 0.35));
                streak *= WNoise(float2(uv.x * 13.0, p.y * 0.8 + 3.0));
                col = lerp(col, _GrimeColor.rgb, saturate(streak * 1.6) * _StreakAmount * vertical);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = col;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness * (1.0 - grime * 0.6);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0 - grid * _GridStrength * 0.3;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vertShadow (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }

            half4 fragShadow (Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vertDepth (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half fragDepth (Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vertDN (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 fragDN (Varyings i) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
