// Procedural terrain shader for URP.
// Blends sand (near water) -> grass (two tones, noise-varied) -> dirt -> rock (steep slopes)
// using world height and slope. No textures required.
Shader "Bobery/Terrain Blend"
{
    Properties
    {
        [Header(Grass)]
        _GrassColorA ("Grass Color A", Color) = (0.30, 0.52, 0.18, 1)
        _GrassColorB ("Grass Color B", Color) = (0.45, 0.62, 0.22, 1)
        _GrassNoiseScale ("Grass Variation Scale", Float) = 0.06

        [Header(Dirt)]
        _DirtColor ("Dirt Color", Color) = (0.42, 0.33, 0.22, 1)
        _DirtAmount ("Dirt Patches", Range(0, 1)) = 0.25

        [Header(Sand and Lake Bed)]
        _SandColor ("Sand Color", Color) = (0.82, 0.74, 0.52, 1)
        _MudColor ("Lake Bed Color", Color) = (0.30, 0.27, 0.20, 1)
        _WaterLevel ("Water Level (world Y)", Float) = 0
        _SandHeight ("Sand Height Above Water", Float) = 0.6
        _SandBlend ("Sand Blend", Float) = 0.4

        [Header(Rock)]
        _RockColor ("Rock Color", Color) = (0.48, 0.46, 0.43, 1)
        _RockSlope ("Rock Slope Start (0-1)", Range(0, 1)) = 0.35
        _RockBlend ("Rock Blend", Range(0.01, 0.5)) = 0.12

        [Header(Wasteland)]
        _CrackColor ("Crack Color", Color) = (0.12, 0.1, 0.08, 1)
        _CrackScale ("Crack Scale", Float) = 0.6
        _CrackWidth ("Crack Width", Range(0.005, 0.2)) = 0.05
        _CrackStrength ("Crack Strength", Range(0, 1)) = 0
        _ScorchColor ("Scorch Color", Color) = (0.08, 0.07, 0.06, 1)
        _ScorchAmount ("Scorch Patches", Range(0, 1)) = 0
        _ScorchScale ("Scorch Scale", Float) = 0.05

        [Header(Surface)]
        _DetailScale ("Detail Noise Scale", Float) = 1.2
        _DetailStrength ("Detail Noise Strength", Range(0, 1)) = 0.18
        _Smoothness ("Smoothness", Range(0, 1)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GrassColorA;
            half4 _GrassColorB;
            float _GrassNoiseScale;
            half4 _DirtColor;
            half _DirtAmount;
            half4 _SandColor;
            half4 _MudColor;
            float _WaterLevel;
            float _SandHeight;
            float _SandBlend;
            half4 _RockColor;
            half _RockSlope;
            half _RockBlend;
            half4 _CrackColor;
            float _CrackScale;
            half _CrackWidth;
            half _CrackStrength;
            half4 _ScorchColor;
            half _ScorchAmount;
            float _ScorchScale;
            float _DetailScale;
            half _DetailStrength;
            half _Smoothness;
        CBUFFER_END

        // --- small value-noise helpers ---
        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float a = Hash21(i);
            float b = Hash21(i + float2(1, 0));
            float c = Hash21(i + float2(0, 1));
            float d = Hash21(i + float2(1, 1));
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        float Fbm3(float2 p)
        {
            float v = 0.0;
            v += ValueNoise(p) * 0.5;
            v += ValueNoise(p * 2.03 + 17.1) * 0.25;
            v += ValueNoise(p * 4.07 + 41.7) * 0.125;
            return v / 0.875;
        }

        float2 Hash22(float2 p)
        {
            float3 a = frac(float3(p.xyx) * float3(123.34, 234.34, 345.65));
            a += dot(a, a + 34.45);
            return frac(float2(a.x * a.y, a.y * a.z));
        }

        // Distance to the nearest Voronoi cell border (0 on the border) – used for dried-mud cracks.
        float VoronoiEdge(float2 p)
        {
            float2 n = floor(p);
            float2 f = frac(p);
            float2 mg = 0, mr = 0;
            float md = 8.0;
            [unroll] for (int j = -1; j <= 1; j++)
            [unroll] for (int i = -1; i <= 1; i++)
            {
                float2 g = float2(i, j);
                float2 r = g + Hash22(n + g) - f;
                float d = dot(r, r);
                if (d < md) { md = d; mr = r; mg = g; }
            }
            md = 8.0;
            [unroll] for (int j2 = -2; j2 <= 2; j2++)
            [unroll] for (int i2 = -2; i2 <= 2; i2++)
            {
                float2 g = mg + float2(i2, j2);
                float2 r = g + Hash22(n + g) - f;
                float2 diff = r - mr;
                if (dot(diff, diff) > 0.00001)
                    md = min(md, dot(0.5 * (mr + r), normalize(diff)));
            }
            return md;
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

            half3 TerrainAlbedo(float3 posWS, float3 n)
            {
                float2 xz = posWS.xz;
                float h = posWS.y - _WaterLevel;
                float slope = 1.0 - saturate(n.y); // 0 flat, 1 vertical

                // Grass with large-scale colour variation.
                float gNoise = Fbm3(xz * _GrassNoiseScale);
                half3 col = lerp(_GrassColorA.rgb, _GrassColorB.rgb, smoothstep(0.3, 0.7, gNoise));

                // Dirt patches.
                float dNoise = Fbm3(xz * _GrassNoiseScale * 2.3 + 91.0);
                float dirt = smoothstep(1.0 - _DirtAmount, 1.0 - _DirtAmount + 0.15, dNoise);
                dirt = max(dirt, smoothstep(_RockSlope * 0.5, _RockSlope, slope) * 0.6); // worn ground on slopes
                col = lerp(col, _DirtColor.rgb, dirt);

                // Rock on steep slopes.
                float rock = smoothstep(_RockSlope - _RockBlend, _RockSlope + _RockBlend, slope + (gNoise - 0.5) * 0.1);
                col = lerp(col, _RockColor.rgb, rock);

                // Sand band around the water and muddy lake bed below it.
                float sandEdge = _SandHeight + (dNoise - 0.5) * _SandBlend;
                float sand = 1.0 - smoothstep(sandEdge - _SandBlend * 0.5, sandEdge + _SandBlend * 0.5, h);
                col = lerp(col, _SandColor.rgb, sand * (1.0 - rock * 0.7));
                float mud = 1.0 - smoothstep(-1.2, -0.2, h);
                col = lerp(col, _MudColor.rgb, mud);

                // Scorched / ash patches.
                if (_ScorchAmount > 0.001)
                {
                    float sNoise = Fbm3(xz * _ScorchScale + 233.0);
                    float scorch = smoothstep(1.0 - _ScorchAmount, 1.0 - _ScorchAmount + 0.12, sNoise);
                    col = lerp(col, _ScorchColor.rgb, scorch * saturate(h * 2.0));
                }

                // Dried, cracked ground on flat areas (not under water or on rock).
                if (_CrackStrength > 0.001)
                {
                    float edge = VoronoiEdge(xz * _CrackScale);
                    float crack = 1.0 - smoothstep(_CrackWidth * 0.5, _CrackWidth, edge);
                    float flat = 1.0 - smoothstep(0.1, 0.3, slope);
                    float mask = Fbm3(xz * 0.04 + 57.0);       // cracks come and go in large regions
                    mask = smoothstep(0.35, 0.6, mask);
                    col = lerp(col, _CrackColor.rgb, crack * flat * mask * _CrackStrength * saturate(h));
                }

                // Fine detail so close-ups don't look flat.
                float detail = ValueNoise(xz * _DetailScale * 4.0) * 0.6 + ValueNoise(xz * _DetailScale * 13.0) * 0.4;
                col *= 1.0 + (detail - 0.5) * 2.0 * _DetailStrength;

                return col;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 albedo = TerrainAlbedo(i.positionWS, n);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
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
