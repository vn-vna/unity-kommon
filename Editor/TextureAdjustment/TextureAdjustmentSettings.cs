using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Editor.TextureAdjustment
{
    public class TextureAdjustmentSettings : ScriptableObject
    {
        public string sourceTextureGUID;

        [Range(-1f, 1f)]
        public float brightness = 0f;

        [Range(0f, 2f)]
        public float contrast = 1f;

        [Range(-180f, 180f)]
        public float hueShift = 0f;

        [Range(0f, 2f)]
        public float saturation = 1f;

        public AnimationCurve toneCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    public enum LayerType
    {
        BrightnessContrast = 0,
        HueSaturationVibrance = 1,
        ToneCurve = 2,
        Tint = 3,
        Overlay = 4
    }

    [Serializable]
    public struct SerializableKeyframe
    {
        public float time;
        public float value;
        public float inTangent;
        public float outTangent;
        public float inWeight;
        public float outWeight;
        public WeightedMode weightedMode;

        public SerializableKeyframe(Keyframe kf)
        {
            time = kf.time;
            value = kf.value;
            inTangent = kf.inTangent;
            outTangent = kf.outTangent;
            inWeight = kf.inWeight;
            outWeight = kf.outWeight;
            weightedMode = kf.weightedMode;
        }

        public Keyframe ToKeyframe()
        {
            return new Keyframe(time, value, inTangent, outTangent, inWeight, outWeight)
            {
                weightedMode = weightedMode
            };
        }
    }

    [Serializable]
    public class AdjustmentLayerData
    {
        public bool enabled = true;
        public string layerType;
        public string name;

        public float brightness;
        public float contrast;
        public float hueShift;
        public float saturation;
        public float vibrance;
        public SerializableKeyframe[] toneCurveKeys;

        public float inputBlack;
        public float inputWhite;
        public float outputBlack;
        public float outputWhite;

        public float tintR = 1f;
        public float tintG = 1f;
        public float tintB = 1f;
        public float tintStrength;

        public float overlayR = 1f;
        public float overlayG = 1f;
        public float overlayB = 1f;
        public float overlayStrength;
        public int overlayBlendMode;

        public AdjustmentLayerData() { }

        public AdjustmentLayerData(LayerType type, string displayName)
        {
            layerType = type.ToString();
            name = displayName;
            enabled = true;
            inputBlack = 0f; inputWhite = 1f; outputBlack = 0f; outputWhite = 1f;

            switch (type)
            {
                case LayerType.BrightnessContrast:
                    brightness = 0f; contrast = 1f;
                    break;
                case LayerType.HueSaturationVibrance:
                    hueShift = 0f; saturation = 1f; vibrance = 0f;
                    break;
                case LayerType.ToneCurve:
                    toneCurveKeys = new[] {
                        new SerializableKeyframe(new Keyframe(0f, 0f, 0f, 0f)),
                        new SerializableKeyframe(new Keyframe(1f, 1f, 0f, 0f))
                    };
                    break;
                case LayerType.Tint:
                    tintR = 1f; tintG = 0.7f; tintB = 0.5f; tintStrength = 0.3f;
                    break;
                case LayerType.Overlay:
                    overlayR = 1f; overlayG = 1f; overlayB = 1f; overlayStrength = 0.3f;
                    overlayBlendMode = 0;
                    break;
            }
        }

        public LayerType GetLayerType()
        {
            if (Enum.TryParse(layerType, out LayerType t))
                return t;
            return LayerType.BrightnessContrast;
        }

        [JsonIgnore]
        public Color TintColor
        {
            get => new Color(tintR, tintG, tintB, 1f);
            set { tintR = value.r; tintG = value.g; tintB = value.b; }
        }

        [JsonIgnore]
        public Color OverlayColor
        {
            get => new Color(overlayR, overlayG, overlayB, 1f);
            set { overlayR = value.r; overlayG = value.g; overlayB = value.b; }
        }
    }

    [Serializable]
    public class AdjustmentData
    {
        public string sourceTextureGUID = "";

        public float brightness = 0f;
        public float contrast = 1f;
        public float hueShift = 0f;
        public float saturation = 1f;
        public float vibrance = 0f;
        public SerializableKeyframe[] toneCurveKeys;

        public float inputBlack = 0f;
        public float inputWhite = 1f;
        public float outputBlack = 0f;
        public float outputWhite = 1f;

        public float tintR = 1f;
        public float tintG = 1f;
        public float tintB = 1f;
        public float tintStrength = 0f;

        public float overlayR = 1f;
        public float overlayG = 1f;
        public float overlayB = 1f;
        public float overlayStrength = 0f;
        public int overlayBlendMode = 0;

        public List<AdjustmentLayerData> layers;

        [JsonIgnore]
        public Color TintColor
        {
            get => new Color(tintR, tintG, tintB, 1f);
            set { tintR = value.r; tintG = value.g; tintB = value.b; }
        }

        [JsonIgnore]
        public Color OverlayColor
        {
            get => new Color(overlayR, overlayG, overlayB, 1f);
            set { overlayR = value.r; overlayG = value.g; overlayB = value.b; }
        }

        public AdjustmentData() { }

        public List<AdjustmentLayerData> GetEffectiveLayers()
        {
            if (layers != null && layers.Count > 0)
                return layers;

            layers = new List<AdjustmentLayerData>();

            AdjustmentLayerData toneLayer = new AdjustmentLayerData(LayerType.ToneCurve, "Tone Curve");
            toneLayer.toneCurveKeys = toneCurveKeys ?? new[] {
                new SerializableKeyframe(new Keyframe(0f, 0f, 0f, 0f)),
                new SerializableKeyframe(new Keyframe(1f, 1f, 0f, 0f))
            };
            layers.Add(toneLayer);

            AdjustmentLayerData bcLayer = new AdjustmentLayerData(LayerType.BrightnessContrast, "Brightness / Contrast");
            bcLayer.brightness = brightness;
            bcLayer.contrast = contrast;
            bcLayer.inputBlack = inputBlack;
            bcLayer.inputWhite = inputWhite;
            bcLayer.outputBlack = outputBlack;
            bcLayer.outputWhite = outputWhite;
            layers.Add(bcLayer);

            AdjustmentLayerData hsvLayer = new AdjustmentLayerData(LayerType.HueSaturationVibrance, "Hue / Saturation / Vibrance");
            hsvLayer.hueShift = hueShift;
            hsvLayer.saturation = saturation;
            hsvLayer.vibrance = vibrance;
            layers.Add(hsvLayer);

            AdjustmentLayerData tintLayer = new AdjustmentLayerData(LayerType.Tint, "Tint");
            tintLayer.tintR = tintR;
            tintLayer.tintG = tintG;
            tintLayer.tintB = tintB;
            tintLayer.tintStrength = tintStrength;
            tintLayer.enabled = tintStrength > 0.001f;
            layers.Add(tintLayer);

            AdjustmentLayerData overlayLayer = new AdjustmentLayerData(LayerType.Overlay, "Overlay");
            overlayLayer.overlayR = overlayR;
            overlayLayer.overlayG = overlayG;
            overlayLayer.overlayB = overlayB;
            overlayLayer.overlayStrength = overlayStrength;
            overlayLayer.overlayBlendMode = overlayBlendMode;
            overlayLayer.enabled = overlayStrength > 0.001f;
            layers.Add(overlayLayer);

            return layers;
        }
    }
}
