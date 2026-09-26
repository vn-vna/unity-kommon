Shader "Com.Scheherazade/UI/Screen Splash"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _SplashType("Splash Type", Float) = 0
        _SplashColor("Splash Color", Color) = (1, 0.22, 0.08, 0.55)
        _Intensity("Intensity", Range(0, 1)) = 1
        _BorderWidth("Border Width", Range(0.001, 0.5)) = 0.1
        _EdgeSoftness("Edge Softness", Range(0.0001, 0.25)) = 0.025
        _SplashReach("Splash Reach", Range(0, 1)) = 0.65
        _NoiseScale("Noise Scale", Float) = 18
        _NoiseSpeed("Noise Speed", Float) = 2
        _NoiseTime("Noise Time", Float) = 0
        [HideInInspector] _StencilComp("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
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
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "ScreenSplash"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _SplashColor;
                float _SplashType;
                float _Intensity;
                float _BorderWidth;
                float _EdgeSoftness;
                float _SplashReach;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseTime;
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

            float Hash21(float2 value)
            {
                return frac(sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 fraction = frac(value);
                fraction = fraction * fraction * (3.0 - 2.0 * fraction);
                float lower = lerp(
                    Hash21(cell),
                    Hash21(cell + float2(1.0, 0.0)),
                    fraction.x
                );
                float upper = lerp(
                    Hash21(cell + float2(0.0, 1.0)),
                    Hash21(cell + float2(1.0, 1.0)),
                    fraction.x
                );
                return lerp(lower, upper, fraction.y);
            }

            float GetEdgeEnvelope(float edgeDistance, float reachScale)
            {
                float width = max(
                    0.0001,
                    _BorderWidth * (
                        1.0 + saturate(_SplashReach) * reachScale
                    )
                );
                return 1.0 - smoothstep(
                    width,
                    width + max(0.0001, _EdgeSoftness),
                    edgeDistance
                );
            }

            float EvaluateEdgeMask(float2 uv, float edgeDistance)
            {
                float2 drift = float2(
                    _NoiseTime * _NoiseSpeed,
                    -_NoiseTime * _NoiseSpeed * 0.37
                );
                float noise = ValueNoise(uv * _NoiseScale + drift);
                float protrusion = smoothstep(0.5, 0.96, noise)
                    * _SplashReach;
                float width = max(
                    0.0001,
                    _BorderWidth * (1.0 + protrusion)
                );
                return 1.0 - smoothstep(
                    width,
                    width + max(0.0001, _EdgeSoftness),
                    edgeDistance
                );
            }

            float EvaluateStarryMask(float2 uv, float edgeDistance)
            {
                float shortestScreenEdge = max(
                    1.0,
                    min(_ScreenParams.x, _ScreenParams.y)
                );
                float2 aspectPosition = (uv - 0.5)
                    * _ScreenParams.xy / shortestScreenEdge;
                float2 gridPosition = aspectPosition
                    * max(0.01, _NoiseScale);
                float2 cell = floor(gridPosition);
                float2 localPosition = frac(gridPosition) - 0.5;
                float2 starOffset = float2(
                    Hash21(cell + float2(17.17, 43.31)),
                    Hash21(cell + float2(71.43, 11.87))
                ) - 0.5;
                float starSeed = Hash21(
                    cell + float2(31.73, 89.17)
                );
                float starRadius = lerp(0.045, 0.12, starSeed);
                float starDistance = length(
                    localPosition - starOffset * 0.65
                );
                float star = 1.0 - smoothstep(
                    starRadius,
                    starRadius + 0.035,
                    starDistance
                );
                float presence = step(0.68, starSeed);
                float phase = Hash21(
                    cell + float2(97.53, 53.09)
                ) * 6.2831853;
                float twinkle = 0.35 + 0.65 * (
                    0.5 + 0.5 * sin(
                        _NoiseTime * _NoiseSpeed * 6.2831853 + phase
                    )
                );
                return star * presence * twinkle
                    * GetEdgeEnvelope(edgeDistance, 1.0);
            }

            float EvaluateWaveMask(
                float2 uv,
                float2 edgeDistances,
                float edgeDistance
            )
            {
                float shortestScreenEdge = max(
                    1.0,
                    min(_ScreenParams.x, _ScreenParams.y)
                );
                float2 aspectUv = uv
                    * _ScreenParams.xy / shortestScreenEdge;
                float edgeDistanceX = edgeDistances.x
                    * _ScreenParams.x / shortestScreenEdge;
                float edgeDistanceY = edgeDistances.y
                    * _ScreenParams.y / shortestScreenEdge;
                float animationPhase = _NoiseTime * _NoiseSpeed
                    * 6.2831853;
                float patternScale = max(0.01, _NoiseScale);
                float horizontalPhase = aspectUv.x * patternScale
                    - animationPhase;
                float verticalPhase = aspectUv.y * patternScale
                    - animationPhase;
                float edgeBlend = smoothstep(
                    -max(0.02, _EdgeSoftness),
                    max(0.02, _EdgeSoftness),
                    edgeDistanceY - edgeDistanceX
                );
                float waveSignal = lerp(
                    sin(horizontalPhase),
                    sin(verticalPhase),
                    edgeBlend
                );
                float wave = 0.5 + 0.5 * waveSignal;
                float width = max(
                    0.0001,
                    _BorderWidth * (1.0 + wave * _SplashReach)
                );
                float envelope = 1.0 - smoothstep(
                    width,
                    width + max(0.0001, _EdgeSoftness),
                    edgeDistance
                );
                float bandPhase = edgeDistance
                    / max(0.0001, _BorderWidth)
                    * 9.424778 + waveSignal * 0.8;
                float bands = 0.65 + 0.35 * (
                    0.5 + 0.5 * sin(bandPhase)
                );
                return envelope * bands;
            }

            float EvaluateVortexMask(float2 uv, float edgeDistance)
            {
                float shortestScreenEdge = max(
                    1.0,
                    min(_ScreenParams.x, _ScreenParams.y)
                );
                float2 centeredPosition = (uv - 0.5)
                    * _ScreenParams.xy / shortestScreenEdge;
                float radius = length(centeredPosition);
                float angle = atan2(
                    centeredPosition.y,
                    centeredPosition.x
                );
                float arms = floor(
                    clamp(_NoiseScale * 0.25, 2.0, 8.0) + 0.5
                );
                float phase = angle * arms
                    - radius * max(0.01, _NoiseScale) * 2.0
                    + _NoiseTime * _NoiseSpeed * 6.2831853;
                float secondaryArms = max(2.0, arms - 1.0);
                float secondaryPhase = angle * secondaryArms
                    - radius * max(0.01, _NoiseScale) * 1.35
                    - _NoiseTime * _NoiseSpeed * 4.3982297
                    + 2.1;
                float primarySpiral = 0.5 + 0.5 * sin(phase);
                float secondarySpiral = 0.5 + 0.5
                    * sin(secondaryPhase);
                float filaments = max(
                    smoothstep(0.58, 0.94, primarySpiral),
                    smoothstep(0.72, 0.98, secondarySpiral) * 0.65
                );
                return filaments * GetEdgeEnvelope(
                    edgeDistance,
                    0.85
                );
            }

            float EvaluateSplashMask(
                float2 uv,
                float2 edgeDistances,
                float edgeDistance
            )
            {
                float shortestScreenEdge = max(
                    1.0,
                    min(_ScreenParams.x, _ScreenParams.y)
                );
                float2 centeredPosition = (uv - 0.5)
                    * _ScreenParams.xy / shortestScreenEdge;
                float radius = length(centeredPosition);
                float gradientRadius = lerp(
                    0.2,
                    0.8,
                    saturate(_SplashReach)
                );
                float fadeWidth = max(
                    0.04,
                    _EdgeSoftness + _BorderWidth * 0.5
                );
                return smoothstep(
                    gradientRadius - fadeWidth,
                    gradientRadius,
                    radius
                );
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 edgeDistances = min(input.uv, 1.0 - input.uv);
                float shortestScreenEdge = min(_ScreenParams.x, _ScreenParams.y);
                float edgeDistance = min(
                    edgeDistances.x * _ScreenParams.x / shortestScreenEdge,
                    edgeDistances.y * _ScreenParams.y / shortestScreenEdge
                );
                float splashMask = EvaluateSplashMask(
                    input.uv,
                    edgeDistances,
                    edgeDistance
                );
                half textureAlpha = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    input.uv
                ).a;
                half alpha = splashMask * _SplashColor.a * _Intensity
                    * input.color.a * textureAlpha;
                return half4(_SplashColor.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
