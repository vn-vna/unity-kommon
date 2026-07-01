using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Editor.TextureAdjustment
{
    [ScriptedImporter(1, "texadj")]
    public class TextureAdjustmentImporter : ScriptedImporter
    {
        private const string ShaderName = "Hidden/Scheherazade/TextureAdjustment";
        private const int LUTSize = 256;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string json = File.ReadAllText(ctx.assetPath);
            AdjustmentData data;

            try
            {
                data = JsonConvert.DeserializeObject<AdjustmentData>(json);
                if (data == null)
                    data = new AdjustmentData();
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[TextureAdjustment] Failed to parse {ctx.assetPath}: {ex.Message}");
                Texture2D fallback = CreateFallbackTexture("JSON Parse Error");
                ctx.AddObjectToAsset("MainTex", fallback);
                ctx.SetMainObject(fallback);
                return;
            }

            string sourcePath = AssetDatabase.GUIDToAssetPath(data.sourceTextureGUID);

            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"[TextureAdjustment] Invalid source texture GUID in {ctx.assetPath}");
                Texture2D fallback = CreateFallbackTexture("Missing Source");
                ctx.AddObjectToAsset("MainTex", fallback);
                ctx.SetMainObject(fallback);
                return;
            }

            Texture2D sourceTex = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);

            if (sourceTex == null)
            {
                Debug.LogWarning($"[TextureAdjustment] Could not load texture at {sourcePath}");
                Texture2D fallback = CreateFallbackTexture("Unloadable Source");
                ctx.AddObjectToAsset("MainTex", fallback);
                ctx.SetMainObject(fallback);
                return;
            }

            List<AdjustmentLayerData> layers = data.GetEffectiveLayers();
            Texture2D outputTex = ApplyLayers(sourceTex, layers);

            if (outputTex == null)
            {
                Texture2D fallback = CreateFallbackTexture("Blit Failed");
                ctx.AddObjectToAsset("MainTex", fallback);
                ctx.SetMainObject(fallback);
                return;
            }

            outputTex.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("MainTex", outputTex);
            ctx.SetMainObject(outputTex);
        }

        internal static Texture2D ApplyLayers(Texture2D source, List<AdjustmentLayerData> layers)
        {
            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[TextureAdjustment] Shader '{ShaderName}' not found.");
                return null;
            }

            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;

            RenderTexture ping = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture pong = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, ping);

            bool usePing = true;

            foreach (AdjustmentLayerData layer in layers)
            {
                if (!layer.enabled) continue;

                RenderTexture src = usePing ? ping : pong;
                RenderTexture dst = usePing ? pong : ping;

                SetMaterialForLayer(material, layer);
                Graphics.Blit(src, dst, material);

                usePing = !usePing;
            }

            RenderTexture finalRT = usePing ? ping : pong;

            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            result.name = source.name + "_Adjusted";

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = finalRT;
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;

            RenderTexture.ReleaseTemporary(ping);
            RenderTexture.ReleaseTemporary(pong);
            DestroyImmediate(material);

            return result;
        }

        internal static void SetMaterialForLayer(Material material, AdjustmentLayerData layer)
        {
            LayerType type = layer.GetLayerType();

            material.SetInt("_Mode", (int)type);

            material.SetFloat("_InputBlack", layer.inputBlack);
            material.SetFloat("_InputWhite", layer.inputWhite);
            material.SetFloat("_OutputBlack", layer.outputBlack);
            material.SetFloat("_OutputWhite", layer.outputWhite);

            switch (type)
            {
                case LayerType.BrightnessContrast:
                    material.SetFloat("_Brightness", layer.brightness);
                    material.SetFloat("_Contrast", layer.contrast);
                    break;
                case LayerType.HueSaturationVibrance:
                    material.SetFloat("_HueShift", layer.hueShift);
                    material.SetFloat("_Saturation", layer.saturation);
                    material.SetFloat("_Vibrance", layer.vibrance);
                    break;
                case LayerType.ToneCurve:
                    AnimationCurve curve = BuildCurveFromKeys(layer.toneCurveKeys);
                    Texture2D lut = BakeToneCurveLUT(curve);
                    lut.hideFlags = HideFlags.HideAndDontSave;
                    material.SetTexture("_ToneCurveLUT", lut);
                    break;
                case LayerType.Tint:
                    material.SetColor("_TintColor", layer.TintColor);
                    material.SetFloat("_TintStrength", layer.tintStrength);
                    break;
                case LayerType.Overlay:
                    material.SetColor("_OverlayColor", layer.OverlayColor);
                    material.SetFloat("_OverlayStrength", layer.overlayStrength);
                    material.SetInt("_OverlayBlendMode", layer.overlayBlendMode);
                    break;
            }
        }

        internal static Texture2D BakeToneCurveLUT(AnimationCurve curve)
        {
            Texture2D lut = new Texture2D(LUTSize, 1, TextureFormat.RGBA32, false, true);
            lut.wrapMode = TextureWrapMode.Clamp;
            lut.filterMode = FilterMode.Bilinear;
            lut.name = "ToneCurveLUT";

            for (int i = 0; i < LUTSize; i++)
            {
                float t = i / (float)(LUTSize - 1);
                float v = curve != null ? Mathf.Clamp01(curve.Evaluate(t)) : t;
                lut.SetPixel(i, 0, new Color(v, v, v, 1f));
            }

            lut.Apply();
            return lut;
        }

        internal static AnimationCurve BuildCurveFromKeys(SerializableKeyframe[] keys)
        {
            if (keys == null || keys.Length == 0)
                return AnimationCurve.Linear(0, 0, 1, 1);

            Keyframe[] kfs = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                kfs[i] = keys[i].ToKeyframe();

            return new AnimationCurve(kfs);
        }

        private static Texture2D CreateFallbackTexture(string reason)
        {
            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color magenta = Color.magenta;
            Color[] pixels = new Color[16];

            for (int i = 0; i < 16; i++)
                pixels[i] = (i / 4 + i % 4) % 2 == 0 ? magenta : Color.black;

            tex.SetPixels(pixels);
            tex.Apply();
            tex.name = $"Fallback ({reason})";
            return tex;
        }

        [MenuItem("Assets/Create/Scheherazade/Texture Adjustment", false, 10)]
        private static void CreateTexadjFile()
        {
            string folder = "Assets";
            UnityEngine.Object selected = Selection.activeObject;

            if (selected != null)
            {
                string selectedPath = AssetDatabase.GetAssetPath(selected);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    if (AssetDatabase.IsValidFolder(selectedPath))
                        folder = selectedPath;
                    else
                        folder = Path.GetDirectoryName(selectedPath);
                }
            }

            string sourceGuid = "";

            if (selected != null && selected is Texture2D selectedTex)
            {
                string texPath = AssetDatabase.GetAssetPath(selectedTex);
                sourceGuid = AssetDatabase.AssetPathToGUID(texPath);
            }

            string baseName = "NewTextureAdjustment";
            string filePath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, baseName + ".texadj"));

            AdjustmentData defaultData = new AdjustmentData
            {
                sourceTextureGUID = sourceGuid,
                brightness = 0f,
                contrast = 1f,
                hueShift = 0f,
                saturation = 1f,
                toneCurveKeys = new[]
                {
                    new SerializableKeyframe(new Keyframe(0f, 0f, 0f, 0f)),
                    new SerializableKeyframe(new Keyframe(1f, 1f, 0f, 0f))
                }
            };

            string json = JsonConvert.SerializeObject(defaultData, Formatting.Indented);
            File.WriteAllText(filePath, json);

            AssetDatabase.ImportAsset(filePath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath));
        }

        [MenuItem("Assets/Create/Scheherazade/Texture Adjustment (from Texture)", true)]
        private static bool ValidateCreateTexadjFromTexture()
        {
            return Selection.activeObject is Texture2D;
        }

        [MenuItem("Assets/Create/Scheherazade/Texture Adjustment (from Texture)", false, 10)]
        private static void CreateTexadjFromTexture()
        {
            Texture2D tex = Selection.activeObject as Texture2D;
            if (tex == null) return;

            string texPath = AssetDatabase.GetAssetPath(tex);
            string folder = Path.GetDirectoryName(texPath);
            string sourceGuid = AssetDatabase.AssetPathToGUID(texPath);
            string baseName = Path.GetFileNameWithoutExtension(texPath);
            string filePath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, baseName + "_Adjust.texadj"));

            AdjustmentData data = new AdjustmentData
            {
                sourceTextureGUID = sourceGuid,
                brightness = 0f,
                contrast = 1f,
                hueShift = 0f,
                saturation = 1f,
                toneCurveKeys = new[]
                {
                    new SerializableKeyframe(new Keyframe(0f, 0f, 0f, 0f)),
                    new SerializableKeyframe(new Keyframe(1f, 1f, 0f, 0f))
                }
            };

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(filePath, json);

            AssetDatabase.ImportAsset(filePath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath));
        }
    }
}
