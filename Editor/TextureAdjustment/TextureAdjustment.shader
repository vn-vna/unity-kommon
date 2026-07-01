Shader "Hidden/Scheherazade/TextureAdjustment"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Mode ("Mode", Int) = 0
        _Brightness ("Brightness", Float) = 0.0
        _Contrast ("Contrast", Float) = 1.0
        _HueShift ("Hue Shift", Float) = 0.0
        _Saturation ("Saturation", Float) = 1.0
        _Vibrance ("Vibrance", Float) = 0.0
        _ToneCurveLUT ("Tone Curve LUT", 2D) = "white" {}
        _InputBlack ("Input Black", Float) = 0.0
        _InputWhite ("Input White", Float) = 1.0
        _OutputBlack ("Output Black", Float) = 0.0
        _OutputWhite ("Output White", Float) = 1.0
        _TintColor ("Tint Color", Color) = (1,1,1,1)
        _TintStrength ("Tint Strength", Float) = 0.0
        _OverlayColor ("Overlay Color", Color) = (1,1,1,1)
        _OverlayStrength ("Overlay Strength", Float) = 0.0
        _OverlayBlendMode ("Overlay Blend Mode", Int) = 0
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            int _Mode;
            float _Brightness;
            float _Contrast;
            float _HueShift;
            float _Saturation;
            float _Vibrance;
            sampler2D _ToneCurveLUT;
            float _InputBlack;
            float _InputWhite;
            float _OutputBlack;
            float _OutputWhite;
            float4 _TintColor;
            float _TintStrength;
            float4 _OverlayColor;
            float _OverlayStrength;
            int _OverlayBlendMode;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            float sampleCurve(float x)
            {
                return tex2Dlod(_ToneCurveLUT, float4(x, 0.5, 0, 0)).r;
            }

            float3 blendOverlay(float3 base, float3 blend, float s)
            {
                float3 result;
                result.r = base.r < 0.5 ? (2.0 * base.r * blend.r) : (1.0 - 2.0 * (1.0 - base.r) * (1.0 - blend.r));
                result.g = base.g < 0.5 ? (2.0 * base.g * blend.g) : (1.0 - 2.0 * (1.0 - base.g) * (1.0 - blend.g));
                result.b = base.b < 0.5 ? (2.0 * base.b * blend.b) : (1.0 - 2.0 * (1.0 - base.b) * (1.0 - blend.b));
                return lerp(base, result, s);
            }

            float3 blendMultiply(float3 base, float3 blend, float s)
            {
                return lerp(base, base * blend, s);
            }

            float3 blendScreen(float3 base, float3 blend, float s)
            {
                return lerp(base, 1.0 - (1.0 - base) * (1.0 - blend), s);
            }

            float3 applyBrightnessContrast(float3 rgb)
            {
                float inRange = _InputWhite - _InputBlack;
                float outRange = _OutputWhite - _OutputBlack;
                if (inRange > 0.001)
                {
                    rgb = (rgb - _InputBlack) / inRange;
                    rgb = saturate(rgb);
                    rgb = rgb * outRange + _OutputBlack;
                }
                rgb += _Brightness;
                rgb = (rgb - 0.5) * max(_Contrast, 0.001) + 0.5;
                return rgb;
            }

            float3 applyHueSaturationVibrance(float3 rgb)
            {
                float3 hsv = rgb2hsv(rgb);
                hsv.r = frac(hsv.r + _HueShift / 360.0);
                rgb = hsv2rgb(hsv);
                float lum = dot(rgb, float3(0.299, 0.587, 0.114));
                rgb = lerp(float3(lum, lum, lum), rgb, saturate(_Saturation));
                if (_Vibrance > 0.001)
                {
                    float maxC = max(rgb.r, max(rgb.g, rgb.b));
                    float minC = min(rgb.r, min(rgb.g, rgb.b));
                    float curSat = maxC > 0.001 ? (maxC - minC) / maxC : 0.0;
                    float vibFactor = (1.0 - curSat) * _Vibrance;
                    rgb = lerp(rgb, rgb * 1.5, vibFactor);
                }
                return rgb;
            }

            float3 applyToneCurve(float3 rgb)
            {
                rgb.r = sampleCurve(saturate(rgb.r));
                rgb.g = sampleCurve(saturate(rgb.g));
                rgb.b = sampleCurve(saturate(rgb.b));
                return rgb;
            }

            float3 applyTint(float3 rgb)
            {
                return lerp(rgb, rgb * _TintColor.rgb, _TintStrength);
            }

            float3 applyOverlay(float3 rgb)
            {
                if (_OverlayBlendMode == 1)
                    return blendMultiply(rgb, _OverlayColor.rgb, _OverlayStrength);
                if (_OverlayBlendMode == 2)
                    return blendScreen(rgb, _OverlayColor.rgb, _OverlayStrength);
                if (_OverlayBlendMode == 3)
                    return blendOverlay(rgb, _OverlayColor.rgb, _OverlayStrength);
                return lerp(rgb, _OverlayColor.rgb, _OverlayStrength);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float3 rgb = col.rgb;

                if (_Mode == 0)
                    rgb = applyBrightnessContrast(rgb);
                else if (_Mode == 1)
                    rgb = applyHueSaturationVibrance(rgb);
                else if (_Mode == 2)
                    rgb = applyToneCurve(rgb);
                else if (_Mode == 3)
                    rgb = applyTint(rgb);
                else if (_Mode == 4)
                    rgb = applyOverlay(rgb);

                return fixed4(saturate(rgb), col.a);
            }
            ENDCG
        }
    }
}
