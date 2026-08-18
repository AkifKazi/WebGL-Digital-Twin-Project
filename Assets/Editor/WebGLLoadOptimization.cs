using System;
using UnityEditor;
using UnityEngine;

public static class WebGLLoadOptimization
{
    private static readonly string[] ConservativeNormalMaps =
    {
        "Assets/Materials/Textures/Galvanized Zinc - Normal.png",
        "Assets/Materials/Textures/Worn Concrete - Normal.exr"
    };

    public static void Apply()
    {
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        // GitHub Pages cannot provide custom Content-Encoding rules reliably.
        // Gzip plus Unity's fallback is the most compatible deployment profile.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;

        foreach (string path in ConservativeNormalMaps)
            SetWebGLMaximumSize(path, 1024);

        AssetDatabase.SaveAssets();
        DigitalTwinSceneOrganization.Apply();
        Debug.Log("WebGL loading optimization completed successfully.");
    }

    public static void LogCompressionValues()
    {
        Debug.Log(
            $"WEBGL_COMPRESSION_VALUES Brotli={(int)WebGLCompressionFormat.Brotli} " +
            $"Gzip={(int)WebGLCompressionFormat.Gzip} " +
            $"Disabled={(int)WebGLCompressionFormat.Disabled} " +
            $"Current={(int)PlayerSettings.WebGL.compressionFormat}");
    }

    private static void SetWebGLMaximumSize(string path, int maximumSize)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Texture importer not found: {path}");

        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("WebGL");
        settings.name = "WebGL";
        settings.overridden = true;
        settings.maxTextureSize = maximumSize;
        settings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        settings.format = TextureImporterFormat.Automatic;
        settings.textureCompression = TextureImporterCompression.Compressed;
        settings.compressionQuality = 60;
        settings.crunchedCompression = false;
        importer.SetPlatformTextureSettings(settings);
        importer.SaveAndReimport();
    }
}
