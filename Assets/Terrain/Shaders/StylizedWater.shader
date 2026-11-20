// Simple stylized lake water for URP.
// Depth-based colour (shallow -> deep), soft shoreline foam, gentle waves, fresnel + sun highlight.
// Needs "Depth Texture" enabled on the URP asset for depth colour / foam (PC_RPAsset has it on).
Shader "Bobery/Stylized Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.32, 0.72, 0.70, 0.55)
        _DeepColor ("Deep Color", Color) = (0.06, 0.25, 0.38, 0.92)
        _DepthDistance ("Depth Fade Distance", Float) = 2.5
        _FoamColor ("Foam Color", Color) = (0.95, 0.98, 1, 1)
        _FoamDistance ("Foam Distance", Float) = 0.35
        _WaveHeight ("Wave Height", Float) = 0.04
        _WaveScale ("Wave Scale", Float) = 0.35
        _WaveSpeed ("Wave Speed", Float) = 0.6
        _NormalStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _FresnelPower ("Fresnel Power", Float) = 4
        _SkyColor ("Reflected Sky Color", Color) = (0.62, 0.78, 0.92, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthDistance;
                half4 _FoamColor;
                float _FoamDistance;
                float _WaveHeight;
                float _WaveScale;
                float _WaveSpeed;
                half _NormalStrength;
                half _Smoothness;
                float _FresnelPower;
                half4 _SkyColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            // Sum of a few directional sine waves -> height + analytic derivatives.
            float3 Waves(float2 p, float t)
            {
                float h = 0, dx = 0, dz = 0;
                float2 dirs[3] = { float2(1, 0.3), float2(-0.4, 1), float2(0.7, -0.8) };
                float freqs[3] = { 1.0, 1.7, 2.9 };
                float amps[3]  = { 1.0, 0.55, 0.3 };
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    float2 d = normalize(dirs[k]);
                    float f = freqs[k] * _WaveScale;
                    float phase = dot(d, p) * f + t * _WaveSpeed * (1.0 + k * 0.35);
                    h  += sin(phase) * amps[k];
                    float c = cos(phase) * amps[k] * f;
                    dx += c * d.x;
                    dz += c * d.y;
                }
                return float3(h, dx, dz);
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                posWS.y += Waves(posWS.xz, _Time.y).x * _WaveHeight;
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // --- water depth from the camera depth texture ---
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEye = i.screenPos.w;
                float depth = max(0.0, sceneEye - surfaceEye);

                float depth01 = saturate(depth / _DepthDistance);
                half4 water = lerp(_ShallowColor, _DeepColor, depth01);

                // --- ripples (two scrolling wave sets for normal detail) ---
                float t = _Time.y;
                float3 w1 = Waves(i.positionWS.xz * 3.1, t * 1.3);
                float3 w2 = Waves(i.positionWS.xz * 7.3 + 13.0, -t * 1.7);
                float3 n = normalize(float3(-(w1.y + w2.y * 0.5) * _NormalStrength * 0.15,
                                            1.0,
                                            -(w1.z + w2.z * 0.5) * _NormalStrength * 0.15));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = lerp(0.6, 1.0, mainLight.shadowAttenuation);

                // Diffuse-ish tint by light + ambient.
                half3 ambient = SampleSH(n);
                half ndl = saturate(dot(n, mainLight.direction));
                half3 col = water.rgb * (ambient + mainLight.color * (0.35 + 0.65 * ndl) * shadow);

                // Fresnel sky reflection.
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), _FresnelPower);
                col = lerp(col, _SkyColor.rgb * (ambient + mainLight.color * 0.5), fresnel * 0.7);

                // Sun specular.
                float3 hv = normalize(mainLight.direction + viewDir);
                float specPow = exp2(_Smoothness * 10.0 + 1.0);
                half spec = pow(saturate(dot(n, hv)), specPow) * _Smoothness * 2.0;
                col += mainLight.color * spec * shadow;

                // Shoreline foam (broken up by a moving pattern).
                float foamNoise = sin(i.positionWS.x * 2.1 + t * 1.1) * sin(i.positionWS.z * 2.4 - t * 0.9) * 0.5 + 0.5;
                float foam = 1.0 - smoothstep(0.0, _FoamDistance * (0.6 + foamNoise * 0.8), depth);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                half alpha = saturate(max(water.a, foam) + fresnel * 0.3 + spec);
                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
