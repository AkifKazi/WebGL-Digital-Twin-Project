using UnityEngine.Rendering.Universal;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Authoring action that applies the WebGL download-size settings.
///
/// This rewrites import settings and player settings, so it is deliberately a
/// manual step rather than part of the build: run it, review the diff, then
/// build. <see cref="WebGLBuildAudit"/> reports the resulting size.
/// </summary>
public static class WebGLLoadOptimization
{
    /// <summary>
    /// Per-texture WebGL ceilings. Download size scales with the square of the
    /// resolution, so halving a 2048 map removes three quarters of its payload.
    /// Values are chosen per texture rather than globally: ground and sky are
    /// seen at a distance, machine surfaces are seen up close.
    /// </summary>
    private static readonly (string Path, int MaxSize, bool Crunch)[] TextureBudget =
    {
        // Largest single asset in the build at 5.3 MB, 13% of everything
        // downloaded: a 2048 normal map on a surface that is never inspected
        // closer than a metre.
        ("Assets/Materials/Textures/Worn Metal - Normal.png", 1024, false),

        // Background sky. Its default import was already 512; an earlier pass
        // here raised it to 1024 and doubled it to 4 MB, so it is pinned back.
        ("Assets/Materials/Textures/Site Skybox.exr", 512, false),

        // Ground plane, always seen at a distance and at a grazing angle.
        ("Assets/Materials/Textures/Grass Ground - Base Color.jpg", 1024, true),
        ("Assets/Materials/Textures/Grass Ground - Normal.png", 512, false),
        ("Assets/Materials/Textures/Rocky Dirt - Base Color.jpg", 1024, true),
        ("Assets/Materials/Textures/Concrete Floor - Base Color.jpg", 1024, true),

        // Machine surfaces. Seen closest, so these keep more resolution.
        ("Assets/Materials/Textures/Worn Metal - Base Color.png", 1024, true),
        ("Assets/Materials/Textures/Worn Concrete - Normal.exr", 1024, false),
        ("Assets/Materials/Textures/Galvanized Zinc - Normal.png", 1024, false),
        ("Assets/Materials/Textures/Galvanized Zinc - Base Color.jpg", 1024, true),
        ("Assets/Materials/Textures/Grass Ground - Metallic.jpg", 512, true)
    };

    /// <summary>
    /// Static machine models: none of them carry cameras, lights, blend shapes
    /// or animation, and importing those adds data the runtime never reads.
    /// </summary>
    private static readonly (string Path, ModelImporterMeshCompression Compression)[] StaticModels =
    {
        // Hero geometry: inspected closely in every view, so it keeps the
        // lightest compression that still saves space.
        ("Assets/Models/Separator - Full Model.fbx", ModelImporterMeshCompression.Low),
        ("Assets/Models/Separator - Cross-Section Assembly.fbx", ModelImporterMeshCompression.Low),
        ("Assets/Models/Hopper - Bunker and Supports.fbx", ModelImporterMeshCompression.Low),

        // Background and irregular geometry, where vertex quantisation is not
        // readable. Rock Pile alone is 3.7 MB of the build.
        ("Assets/Models/Rock Pile.fbx", ModelImporterMeshCompression.High),
        ("Assets/Models/Rock Particle.fbx", ModelImporterMeshCompression.High),
        ("Assets/Models/Construction Site.fbx", ModelImporterMeshCompression.High),

        // Never rendered.
        ("Assets/Models/Hopper - Collision Mesh.fbx", ModelImporterMeshCompression.High)
    };

    [MenuItem("Tools/Digital Twin/Apply WebGL Load Optimization", priority = 60)]
    public static void Apply()
    {
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;

        // GitHub Pages cannot provide custom Content-Encoding rules reliably.
        // Gzip plus Unity's fallback is the most compatible deployment profile.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;

        ApplyCodeSizeSettings();
        ApplyTextMeshProDefaultFont();
        ApplyPostProcessData();

        int textures = 0;
        int models = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach ((string path, int maxSize, bool crunch) in TextureBudget)
            {
                if (SetWebGLTextureBudget(path, maxSize, crunch))
                    textures++;
            }

            foreach ((string path, ModelImporterMeshCompression compression) in StaticModels)
            {
                if (SetStaticModelImport(path, compression))
                    models++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath("ProjectSettings/ProjectSettings.asset"));
        EditorApplication.ExecuteMenuItem("File/Save Project");

        Debug.Log(
            $"WebGL loading optimization applied: {textures} texture(s), {models} model(s). " +
            $"codeGeneration={PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL)} " +
            $"stripping={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL)}");
    }

    /// <summary>
    /// Code size settings. These change how IL2CPP compiles, not what the game
    /// does, so they are safe to apply without re-authoring content.
    /// </summary>
    /// <summary>
    /// Applied by the release build as well as by <see cref="Apply"/>. Batch
    /// mode does not reliably serialise these into ProjectSettings.asset, so
    /// setting them in the building session is what guarantees the output
    /// actually uses them.
    /// </summary>
    public static void ApplyCodeSizeSettings()
    {
        NamedBuildTarget webgl = NamedBuildTarget.WebGL;

        // Smaller generated code in exchange for slightly slower execution. The
        // scene is light on CPU, so download size is the better trade here.
        PlayerSettings.SetIl2CppCodeGeneration(webgl, Il2CppCodeGeneration.OptimizeSize);

        // Removes unused engine and managed code. High is the aggressive tier;
        // Link.xml preserves the telemetry contracts that JsonUtility reaches
        // by name so live data keeps deserialising.
        PlayerSettings.SetManagedStrippingLevel(webgl, ManagedStrippingLevel.High);

        PlayerSettings.stripEngineCode = true;
    }

    private static bool SetWebGLTextureBudget(string path, int maximumSize, bool crunch)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
        {
            Debug.LogWarning($"WebGL optimization skipped, texture not found: {path}");
            return false;
        }

        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("WebGL");

        settings.name = "WebGL";
        settings.overridden = true;
        settings.maxTextureSize = maximumSize;
        settings.textureCompression = TextureImporterCompression.Compressed;

        // Crunch is applied to colour maps only. It shrinks the download well
        // beyond what gzip manages, but it quantises, which shows on the
        // gradients in a normal map.
        settings.crunchedCompression = crunch;

        if (crunch)
            settings.compressionQuality = 50;

        importer.SetPlatformTextureSettings(settings);
        importer.SaveAndReimport();

        return true;
    }

    private static bool SetStaticModelImport(string path, ModelImporterMeshCompression compression)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
        {
            Debug.LogWarning($"WebGL optimization skipped, model not found: {path}");
            return false;
        }

        bool changed = importer.importBlendShapes
                       || importer.importCameras
                       || importer.importLights
                       || importer.importVisibility
                       || importer.importAnimation
                       || importer.isReadable
                       || importer.meshCompression != compression;

        importer.meshCompression = compression;

        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;
        importer.importAnimation = false;

        // Meshes are never read from script, so the CPU-side copy is dead
        // weight in both the download and in memory.
        importer.isReadable = false;

        if (!changed)
            return false;

        importer.SaveAndReimport();
        return true;
    }

    /// <summary>Reads the current settings back, to confirm they persisted.</summary>
    public static void LogSettings()
    {
        Debug.Log(
            $"WEBGL_SETTINGS codeGeneration={PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL)} " +
            $"stripping={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL)} " +
            $"stripEngineCode={PlayerSettings.stripEngineCode} " +
            $"compression={PlayerSettings.WebGL.compressionFormat}");
    }

    public static void LogCompressionValues()
    {
        Debug.Log(
            $"WEBGL_COMPRESSION_VALUES Brotli={(int)WebGLCompressionFormat.Brotli} " +
            $"Gzip={(int)WebGLCompressionFormat.Gzip} " +
            $"Disabled={(int)WebGLCompressionFormat.Disabled} " +
            $"Current={(int)PlayerSettings.WebGL.compressionFormat}");
    }

    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private const string InterfaceFontPath = "Assets/UI/Fonts/Rajdhani-Medium SDF.asset";
    private const string PostProcessDataPath = "Assets/Settings/Post Process Data.asset";
    private const string PlaceholderTexturePath = "Assets/Settings/Unused Post Process Texture.png";
    private const string DefaultPostProcessDataGuid = "41439944d30ece34e96484bdb6645b55";

    /// <summary>
    /// TextMesh Pro's default font lives in a Resources folder, which ships in
    /// every build whether used or not. The interface only uses Rajdhani, so
    /// the default points there and LiberationSans can be removed.
    /// </summary>
    private static void ApplyTextMeshProDefaultFont()
    {
        UnityEngine.Object settings = AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath);
        UnityEngine.Object font = AssetDatabase.LoadMainAssetAtPath(InterfaceFontPath);

        if (settings == null || font == null)
        {
            Debug.LogWarning("WebGL optimization: TMP settings or interface font not found; default font unchanged.");
            return;
        }

        SerializedObject serialized = new(settings);
        serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
        serialized.FindProperty("m_fallbackFontAssets").arraySize = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    /// <summary>
    /// URP's post-processing data references ten film-grain textures and the
    /// SMAA lookup textures, and all of them ship because they are referenced.
    /// This scene uses neither film grain nor SMAA (the camera uses FXAA).
    ///
    /// The package asset is read-only and URP refills any empty slot in the
    /// editor, so a project copy is used with every unused slot pointed at one
    /// tiny placeholder: a filled slot is left alone, and the placeholder is a
    /// few bytes. Enabling film grain or SMAA later means pointing these slots
    /// back at the package textures.
    /// </summary>
    private static void ApplyPostProcessData()
    {
        Texture2D placeholder = GetOrCreatePlaceholderTexture();
        PostProcessData data = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);

        if (data == null)
        {
            PostProcessData source = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                AssetDatabase.GUIDToAssetPath(DefaultPostProcessDataGuid));

            if (source == null)
            {
                Debug.LogWarning("WebGL optimization: URP default post-process data not found; film grain kept.");
                return;
            }

            data = UnityEngine.Object.Instantiate(source);
            AssetDatabase.CreateAsset(data, PostProcessDataPath);
        }

        SerializedObject serialized = new(data);
        SerializedProperty filmGrain = serialized.FindProperty("textures.filmGrainTex");

        for (int i = 0; i < filmGrain.arraySize; i++)
            filmGrain.GetArrayElementAtIndex(i).objectReferenceValue = placeholder;

        serialized.FindProperty("textures.smaaAreaTex").objectReferenceValue = placeholder;
        serialized.FindProperty("textures.smaaSearchTex").objectReferenceValue = placeholder;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);

        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));

            if (renderer == null || renderer.postProcessData == data)
                continue;

            renderer.postProcessData = data;
            EditorUtility.SetDirty(renderer);
        }
    }

    private static Texture2D GetOrCreatePlaceholderTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(PlaceholderTexturePath);

        if (existing != null)
            return existing;

        Color32[] grey = new Color32[16];

        for (int i = 0; i < grey.Length; i++)
            grey[i] = new Color32(128, 128, 128, 255);

        Texture2D pixels = new(4, 4, TextureFormat.RGBA32, false);
        pixels.SetPixels32(grey);
        System.IO.File.WriteAllBytes(PlaceholderTexturePath, pixels.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(pixels);

        AssetDatabase.ImportAsset(PlaceholderTexturePath);

        if (AssetImporter.GetAtPath(PlaceholderTexturePath) is TextureImporter importer)
        {
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 32;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(PlaceholderTexturePath);
    }
}
