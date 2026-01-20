using System.IO;
using UnityEditor;
using UnityEngine;

public class PngStripperEditorWindow : EditorWindow
{
    private string pngFilePath = "";
    private string pngOutputPath = "";
    private Sprite sourceSprite = null;
    private bool stripVertical = false;
    private bool compressVertical = false;
    private Vector2Int verticalOffset = Vector2Int.zero;
    private int verticalCompressSize = 1;
    private bool stripHorizontal = false;
    private bool compressHorizontal = false;
    private Vector2Int horizontalOffset = Vector2Int.zero;
    private int horizontalCompressSize = 1;
    private Vector2Int sizeModulation = Vector2Int.one;
    private Vector2 scrollPosition;

    [MenuItem("Dev Menu/Tools/PNG Stripper")]
    public static void ShowWindow()
    {
        GetWindow<PngStripperEditorWindow>("PNG Stripper");
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.LabelField("PNG Stripper Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("File Settings", EditorStyles.boldLabel);
        DrawFilePath();
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Vertical Settings", EditorStyles.boldLabel);
        DrawVerticalSettings();
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Horizontal Settings", EditorStyles.boldLabel);
        DrawHorizontalSettings();
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Size Settings", EditorStyles.boldLabel);
        sizeModulation = EditorGUILayout.Vector2IntField("Size Modulation", sizeModulation);
        EditorGUILayout.HelpBox("Final size must be a multiplication of this number.", MessageType.Info);
        EditorGUILayout.Space(10);
        DrawActionButtons();
        EditorGUILayout.EndScrollView();
    }

    private void DrawFilePath()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        sourceSprite = (Sprite)EditorGUILayout.ObjectField("Source Sprite (Optional)", sourceSprite, typeof(Sprite), false);
        if (sourceSprite != null)
        {
            if (GUILayout.Button("Import Settings from Sprite", GUILayout.Height(25)))
            {
                ImportSpriteSettings();
            }
            EditorGUILayout.HelpBox("Import will load the sprite's texture path and border offsets (for Single sprite mode only).", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        pngFilePath = EditorGUILayout.TextField("PNG File Path", pngFilePath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFilePanel("Select PNG File", "", "png");
            if (!string.IsNullOrEmpty(path))
            {
                pngFilePath = path;
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        pngOutputPath = EditorGUILayout.TextField("PNG Output Path", pngOutputPath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.SaveFilePanel("Save PNG File", "", "output.png", "png");
            if (!string.IsNullOrEmpty(path))
            {
                pngOutputPath = path;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawVerticalSettings()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        stripVertical = EditorGUILayout.Toggle("Strip Vertical", stripVertical);
        GUI.enabled = !stripVertical;
        compressVertical = EditorGUILayout.Toggle("Compress Vertical", compressVertical);
        GUI.enabled = true;
        GUI.enabled = stripVertical || compressVertical;
        verticalOffset = EditorGUILayout.Vector2IntField("Vertical Offset", verticalOffset);
        if (compressVertical)
        {
            verticalCompressSize = EditorGUILayout.IntField("Vertical Compress Size", verticalCompressSize);
            verticalCompressSize = Mathf.Max(1, verticalCompressSize);
            EditorGUILayout.HelpBox("The compressed center will be this many pixels tall.", MessageType.Info);
        }
        GUI.enabled = true;
        if (stripVertical && compressVertical)
        {
            EditorGUILayout.HelpBox("Strip and Compress are mutually exclusive!", MessageType.Warning);
            compressVertical = false;
        }
        if (stripVertical)
        {
            EditorGUILayout.HelpBox("Strip mode:  Removes center part vertically, keeps top and bottom.", MessageType.Info);
        }
        else if (compressVertical)
        {
            EditorGUILayout.HelpBox("Compress mode: Resizes center part vertically to fit smaller area.", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawHorizontalSettings()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        stripHorizontal = EditorGUILayout.Toggle("Strip Horizontal", stripHorizontal);
        GUI.enabled = !stripHorizontal;
        compressHorizontal = EditorGUILayout.Toggle("Compress Horizontal", compressHorizontal);
        GUI.enabled = true;
        GUI.enabled = stripHorizontal || compressHorizontal;
        horizontalOffset = EditorGUILayout.Vector2IntField("Horizontal Offset", horizontalOffset);
        if (compressHorizontal)
        {
            horizontalCompressSize = EditorGUILayout.IntField("Horizontal Compress Size", horizontalCompressSize);
            horizontalCompressSize = Mathf.Max(1, horizontalCompressSize);
            EditorGUILayout.HelpBox("The compressed center will be this many pixels wide.", MessageType.Info);
        }
        GUI.enabled = true;
        if (stripHorizontal && compressHorizontal)
        {
            EditorGUILayout.HelpBox("Strip and Compress are mutually exclusive!", MessageType.Warning);
            compressHorizontal = false;
        }
        if (stripHorizontal)
        {
            EditorGUILayout.HelpBox("Strip mode: Removes center part horizontally, keeps left and right.", MessageType.Info);
        }
        else if (compressHorizontal)
        {
            EditorGUILayout.HelpBox("Compress mode: Resizes center part horizontally to fit smaller area.", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Process PNG", GUILayout.Height(30)))
        {
            ProcessPNG();
        }
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Reset", GUILayout.Height(30), GUILayout.Width(80)))
        {
            ResetFields();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void ProcessPNG()
    {
        if (string.IsNullOrEmpty(pngFilePath))
        {
            EditorUtility.DisplayDialog("Error", "Please specify a PNG file path.", "OK");
            return;
        }
        if (!File.Exists(pngFilePath))
        {
            EditorUtility.DisplayDialog("Error", "Input PNG file does not exist.", "OK");
            return;
        }
        if (string.IsNullOrEmpty(pngOutputPath))
        {
            EditorUtility.DisplayDialog("Error", "Please specify an output PNG path.", "OK");
            return;
        }
        if (sizeModulation.x <= 0 || sizeModulation.y <= 0)
        {
            EditorUtility.DisplayDialog("Error", "Size modulation must be greater than zero.", "OK");
            return;
        }
        try
        {
            byte[] fileData = File.ReadAllBytes(pngFilePath);
            Texture2D sourceTexture = new Texture2D(2, 2);
            sourceTexture.LoadImage(fileData);
            Texture2D processedTexture = ProcessImage(sourceTexture);
            Texture2D finalTexture = ApplySizeModulation(processedTexture);
            byte[] pngData = finalTexture.EncodeToPNG();
            File.WriteAllBytes(pngOutputPath, pngData);
            DestroyImmediate(sourceTexture);
            if (processedTexture != finalTexture)
            {
                DestroyImmediate(processedTexture);
            }
            DestroyImmediate(finalTexture);
            EditorUtility.DisplayDialog("Success", $"PNG processed successfully!\nSaved to: {pngOutputPath}", "OK");
            Debug.Log($"PNG processed successfully: {pngOutputPath}");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to process PNG: {e.Message}", "OK");
            Debug.LogError($"PNG processing error: {e}");
        }
    }

    private Texture2D ProcessImage(Texture2D source)
    {
        Texture2D result = source;
        if (stripHorizontal)
        {
            result = StripHorizontal(result);
        }
        else if (compressHorizontal)
        {
            result = CompressHorizontal(result);
        }
        if (stripVertical)
        {
            result = StripVertical(result);
        }
        else if (compressVertical)
        {
            result = CompressVertical(result);
        }
        return result;
    }

    private Texture2D StripHorizontal(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        int leftWidth = horizontalOffset.x;
        int rightWidth = horizontalOffset.y;
        int newWidth = leftWidth + rightWidth;
        if (newWidth <= 0 || leftWidth < 0 || rightWidth < 0 || leftWidth + rightWidth > width)
        {
            Debug.LogWarning("Invalid horizontal strip offset. Returning original.");
            return source;
        }
        Texture2D result = new Texture2D(newWidth, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < leftWidth; x++)
            {
                Color pixel = source.GetPixel(x, y);
                result.SetPixel(x, y, pixel);
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < rightWidth; x++)
            {
                Color pixel = source.GetPixel(width - rightWidth + x, y);
                result.SetPixel(leftWidth + x, y, pixel);
            }
        }
        result.Apply();
        if (source != result)
        {
            DestroyImmediate(source);
        }
        return result;
    }

    private Texture2D StripVertical(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        int topHeight = verticalOffset.x;
        int bottomHeight = verticalOffset.y;
        int newHeight = topHeight + bottomHeight;
        if (newHeight <= 0 || topHeight < 0 || bottomHeight < 0 || topHeight + bottomHeight > height)
        {
            Debug.LogWarning("Invalid vertical strip offset. Returning original.");
            return source;
        }
        Texture2D result = new Texture2D(width, newHeight, TextureFormat.RGBA32, false);
        for (int y = 0; y < topHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = source.GetPixel(x, height - topHeight + y);
                result.SetPixel(x, newHeight - topHeight + y, pixel);
            }
        }
        for (int y = 0; y < bottomHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = source.GetPixel(x, y);
                result.SetPixel(x, y, pixel);
            }
        }
        result.Apply();
        if (source != result)
        {
            DestroyImmediate(source);
        }
        return result;
    }

    private Texture2D CompressHorizontal(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        int leftWidth = horizontalOffset.x;
        int rightWidth = horizontalOffset.y;
        int centerCompressedWidth = horizontalCompressSize;
        int newWidth = leftWidth + centerCompressedWidth + rightWidth;
        if (leftWidth < 0 || rightWidth < 0 || leftWidth + rightWidth >= width)
        {
            Debug.LogWarning("Invalid horizontal compress offset. Returning original.");
            return source;
        }
        Texture2D result = new Texture2D(newWidth, height, TextureFormat.RGBA32, false);
        int centerOriginalWidth = width - leftWidth - rightWidth;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < leftWidth; x++)
            {
                result.SetPixel(x, y, source.GetPixel(x, y));
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < centerCompressedWidth; x++)
            {
                float ratio = (float)x / centerCompressedWidth;
                int sourceX = leftWidth + Mathf.FloorToInt(ratio * centerOriginalWidth);
                sourceX = Mathf.Clamp(sourceX, leftWidth, leftWidth + centerOriginalWidth - 1);
                result.SetPixel(leftWidth + x, y, source.GetPixel(sourceX, y));
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < rightWidth; x++)
            {
                result.SetPixel(leftWidth + centerCompressedWidth + x, y, source.GetPixel(width - rightWidth + x, y));
            }
        }
        result.Apply();
        if (source != result)
        {
            DestroyImmediate(source);
        }
        return result;
    }

    private Texture2D CompressVertical(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        int topHeight = verticalOffset.x;
        int bottomHeight = verticalOffset.y;
        int centerCompressedHeight = verticalCompressSize;
        int newHeight = topHeight + centerCompressedHeight + bottomHeight;
        if (topHeight < 0 || bottomHeight < 0 || topHeight + bottomHeight >= height)
        {
            Debug.LogWarning("Invalid vertical compress offset. Returning original.");
            return source;
        }
        Texture2D result = new Texture2D(width, newHeight, TextureFormat.RGBA32, false);
        int centerOriginalHeight = height - topHeight - bottomHeight;
        for (int y = 0; y < bottomHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                result.SetPixel(x, y, source.GetPixel(x, y));
            }
        }
        for (int y = 0; y < centerCompressedHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float ratio = (float)y / centerCompressedHeight;
                int sourceY = bottomHeight + Mathf.FloorToInt(ratio * centerOriginalHeight);
                sourceY = Mathf.Clamp(sourceY, bottomHeight, bottomHeight + centerOriginalHeight - 1);
                result.SetPixel(x, bottomHeight + y, source.GetPixel(x, sourceY));
            }
        }
        for (int y = 0; y < topHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                result.SetPixel(x, bottomHeight + centerCompressedHeight + y, source.GetPixel(x, height - topHeight + y));
            }
        }
        result.Apply();
        if (source != result)
        {
            DestroyImmediate(source);
        }
        return result;
    }

    private Texture2D ApplySizeModulation(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        int newWidth = Mathf.CeilToInt((float)width / sizeModulation.x) * sizeModulation.x;
        int newHeight = Mathf.CeilToInt((float)height / sizeModulation.y) * sizeModulation.y;
        if (newWidth == width && newHeight == height)
        {
            return source;
        }
        Texture2D result = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
        Color[] clearPixels = new Color[newWidth * newHeight];
        for (int i = 0; i < clearPixels.Length; i++)
        {
            clearPixels[i] = Color.clear;
        }
        result.SetPixels(clearPixels);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                result.SetPixel(x, y, source.GetPixel(x, y));
            }
        }
        result.Apply();
        DestroyImmediate(source);
        return result;
    }

    private void ImportSpriteSettings()
    {
        if (sourceSprite == null)
        {
            EditorUtility.DisplayDialog("Error", "No sprite selected.", "OK");
            return;
        }
        
        string spritePath = AssetDatabase.GetAssetPath(sourceSprite);
        if (string.IsNullOrEmpty(spritePath))
        {
            EditorUtility.DisplayDialog("Error", "Could not get sprite path.", "OK");
            return;
        }
        
        TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not get texture importer.", "OK");
            return;
        }
        
        pngFilePath = System.IO.Path.GetFullPath(spritePath);
        
        if (importer.spriteImportMode == SpriteImportMode.Single)
        {
            Vector4 border = importer.spriteBorder;
            
            horizontalOffset = new Vector2Int((int)border.x, (int)border.z);
            verticalOffset = new Vector2Int((int)border.w, (int)border.y);
            
            EditorUtility.DisplayDialog("Success", 
                $"Imported sprite settings:\n" +
                $"Path: {pngFilePath}\n" +
                $"Horizontal Offset: Left={border.x}, Right={border.z}\n" +
                $"Vertical Offset: Top={border.w}, Bottom={border.y}", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Warning", 
                $"Sprite import mode is {importer.spriteImportMode}.\n" +
                $"Border offsets are only available for Single sprite mode.\n" +
                $"Only the file path has been imported.", "OK");
        }
    }
    
    private void ResetFields()
    {
        pngFilePath = "";
        pngOutputPath = "";
        sourceSprite = null;
        stripVertical = false;
        compressVertical = false;
        verticalOffset = Vector2Int.zero;
        verticalCompressSize = 1;
        stripHorizontal = false;
        compressHorizontal = false;
        horizontalOffset = Vector2Int.zero;
        horizontalCompressSize = 1;
        sizeModulation = Vector2Int.one;
    }
}