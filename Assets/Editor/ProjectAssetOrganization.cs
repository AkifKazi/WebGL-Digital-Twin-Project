using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time, idempotent asset migration for the mentor-review project structure.
/// AssetDatabase.MoveAsset preserves Unity GUIDs and all serialized references.
/// </summary>
public static class ProjectAssetOrganization
{
    [MenuItem("Tools/Digital Twin/Organize and Rename Assets")]
    public static void Apply()
    {
        EnsureFolder("Assets", "UI");
        EnsureFolder("Assets/Materials", "Textures");
        EnsureFolder("Assets/Scripts", "Runtime");

        Move("Assets/Materials/Sprites", "Assets/UI/Sprites");

        MoveMany(MaterialMoves);
        MoveMany(TextureMoves);
        MoveMany(ShaderMoves);
        MoveMany(SpriteMoves);
        MoveMany(ModelMoves);
        MoveMany(PrefabMoves);
        MoveMany(RuntimeScriptMoves);

        RenameCaseOnly(
            "Assets/Prefabs/Performance stat card.prefab",
            "Performance Stat Card");
        RenameCaseOnly(
            "Assets/Prefabs/Stat leader line.prefab",
            "Stat Leader Line");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PROJECT_ASSET_ORGANIZATION_COMPLETE");
    }

    private static readonly (string From, string To)[] MaterialMoves =
    {
        ("Assets/Materials/Bunker.mat", "Assets/Materials/Hopper Exterior.mat"),
        ("Assets/Materials/C-Body.mat", "Assets/Materials/Cross Section Body.mat"),
        ("Assets/Materials/C-Dark.mat", "Assets/Materials/Cross Section Dark Metal.mat"),
        ("Assets/Materials/C-Rock.mat", "Assets/Materials/Rock Particles.mat"),
        ("Assets/Materials/C-Spring.mat", "Assets/Materials/Cross Section Spring.mat"),
        ("Assets/Materials/Concrete.mat", "Assets/Materials/Concrete Structure.mat"),
        ("Assets/Materials/Conveyor.mat", "Assets/Materials/Conveyor Belt.mat"),
        ("Assets/Materials/Grass.mat", "Assets/Materials/Grass Terrain.mat"),
        ("Assets/Materials/Ground Dirt.mat", "Assets/Materials/Rocky Ground.mat"),
        ("Assets/Materials/HDRI.mat", "Assets/Materials/Skybox HDRI.mat"),
        ("Assets/Materials/MAT_DARKMETAL.mat", "Assets/Materials/Dark Metal.mat"),
        ("Assets/Materials/MAT_LIGHTGREY.mat", "Assets/Materials/Light Grey Metal.mat"),
        ("Assets/Materials/MAT_MID GREY.mat", "Assets/Materials/Mid Grey Metal.mat"),
        ("Assets/Materials/MAT_ROCK.mat", "Assets/Materials/Rock Surface.mat"),
        ("Assets/Materials/Motor.mat", "Assets/Materials/Drive Motor.mat"),
        ("Assets/Materials/SG line ui.mat", "Assets/Materials/Telemetry Leader Line.mat"),
        ("Assets/Materials/Smoke.mat", "Assets/Materials/Dust Particles.mat"),
        ("Assets/Materials/Supports.mat", "Assets/Materials/Support Frame.mat")
    };

    private static readonly (string From, string To)[] TextureMoves =
    {
        ("Assets/Materials/Texture new.png", "Assets/Materials/Textures/Worn Metal - Base Color.png"),
        ("Assets/Materials/worn-aluminum_normal-ogl.png", "Assets/Materials/Textures/Worn Metal - Normal.png"),
        ("Assets/Materials/GroundDirtRocky020_COL_2K.jpg", "Assets/Materials/Textures/Rocky Dirt - Base Color.jpg"),
        ("Assets/Materials/concrete_floor_worn_001_diff_2k.jpg", "Assets/Materials/Textures/Concrete Floor - Base Color.jpg"),
        ("Assets/Materials/Poliigon_GrassPatchyGround_4585_BaseColor.jpg", "Assets/Materials/Textures/Grass Ground - Base Color.jpg"),
        ("Assets/Materials/Poliigon_GrassPatchyGround_4585_Metallic.jpg", "Assets/Materials/Textures/Grass Ground - Metallic.jpg"),
        ("Assets/Materials/Poliigon_GrassPatchyGround_4585_Normal.png", "Assets/Materials/Textures/Grass Ground - Normal.png"),
        ("Assets/Materials/Poliigon_MetalGalvanizedZinc_7184_BaseColor.jpg", "Assets/Materials/Textures/Galvanized Zinc - Base Color.jpg"),
        ("Assets/Materials/Poliigon_MetalGalvanizedZinc_7184_Normal.png", "Assets/Materials/Textures/Galvanized Zinc - Normal.png"),
        ("Assets/Materials/Smoke.png", "Assets/Materials/Textures/Dust Particle.png"),
        ("Assets/Materials/HDRI Cubemap.exr", "Assets/Materials/Textures/Site Skybox.exr"),
        ("Assets/Materials/concrete_floor_worn_001_nor_gl_2k.exr", "Assets/Materials/Textures/Worn Concrete - Normal.exr")
    };

    private static readonly (string From, string To)[] ShaderMoves =
    {
        ("Assets/Materials/Clipper.shadergraph", "Assets/Materials/Hopper Cross Section Clipping.shadergraph"),
        ("Assets/Materials/Conveyor trial.shadergraph", "Assets/Materials/Conveyor Belt.shadergraph"),
        ("Assets/Materials/SG_UILeaderReveal.shadergraph", "Assets/Materials/Telemetry Leader Reveal.shadergraph")
    };

    private static readonly (string From, string To)[] SpriteMoves =
    {
        ("Assets/UI/Sprites/Background.png", "Assets/UI/Sprites/Panel Background.png"),
        ("Assets/UI/Sprites/Smaller button background.png", "Assets/UI/Sprites/Compact Button Background.png"),
        ("Assets/UI/Sprites/Stat Sprite.png", "Assets/UI/Sprites/Telemetry Card - Normal.png"),
        ("Assets/UI/Sprites/Stat Sprite warning.png", "Assets/UI/Sprites/Telemetry Card - Warning.png"),
        ("Assets/UI/Sprites/Stat Sprite critical.png", "Assets/UI/Sprites/Telemetry Card - Critical.png"),
        ("Assets/UI/Sprites/Stat Sprite grey.png", "Assets/UI/Sprites/Telemetry Card - Unavailable.png"),
        ("Assets/UI/Sprites/anchor.png", "Assets/UI/Sprites/Leader Line Anchor.png"),
        ("Assets/UI/Sprites/blank sprite.png", "Assets/UI/Sprites/Solid UI Fill.png"),
        ("Assets/UI/Sprites/carbon_add.png", "Assets/UI/Sprites/Zoom In Icon.png"),
        ("Assets/UI/Sprites/carbon_subtract.png", "Assets/UI/Sprites/Zoom Out Icon.png"),
        ("Assets/UI/Sprites/cross section.png", "Assets/UI/Sprites/Cross Section Icon.png"),
        ("Assets/UI/Sprites/full view.png", "Assets/UI/Sprites/Full Model Icon.png"),
        ("Assets/UI/Sprites/pure white sprite.png", "Assets/UI/Sprites/White Pixel.png"),
        ("Assets/UI/Sprites/trail 3.png", "Assets/UI/Sprites/Accent Trail - Top.png"),
        ("Assets/UI/Sprites/trail 4.png", "Assets/UI/Sprites/Accent Trail - Bottom.png"),
        ("Assets/UI/Sprites/trail core.png", "Assets/UI/Sprites/Accent Core.png"),
        ("Assets/UI/Sprites/trail left.png", "Assets/UI/Sprites/Accent Trail - Fade Left.png"),
        ("Assets/UI/Sprites/trail right.png", "Assets/UI/Sprites/Accent Trail - Fade Right.png")
    };

    private static readonly (string From, string To)[] ModelMoves =
    {
        ("Assets/Models/Akif's final cut hopper copy.fbx", "Assets/Models/Hopper - Cross Section.fbx"),
        ("Assets/Models/Akif's final hopper copy.fbx", "Assets/Models/Hopper - Full Model.fbx"),
        ("Assets/Models/Akif's hopper collider.fbx", "Assets/Models/Hopper - Collision Mesh.fbx"),
        ("Assets/Models/background new 2.fbx", "Assets/Models/Site Environment.fbx"),
        ("Assets/Models/conveyor.fbx", "Assets/Models/Conveyor Assembly.fbx"),
        ("Assets/Models/heap of rock.fbx", "Assets/Models/Rock Pile.fbx"),
        ("Assets/Models/rock.fbx", "Assets/Models/Rock Particle.fbx")
    };

    private static readonly (string From, string To)[] PrefabMoves =
    {
        ("Assets/Prefabs/Performance stat card.prefab", "Assets/Prefabs/Performance Stat Card.prefab"),
        ("Assets/Prefabs/Stat leader line.prefab", "Assets/Prefabs/Stat Leader Line.prefab")
    };

    private static readonly (string From, string To)[] RuntimeScriptMoves =
    {
        ("Assets/Scripts/AdaptiveQualityController.cs", "Assets/Scripts/Runtime/AdaptiveQualityController.cs"),
        ("Assets/Scripts/Clipper.cs", "Assets/Scripts/Runtime/HybridHopperClipController.cs"),
        ("Assets/Scripts/ConnectionHealthMonitor.cs", "Assets/Scripts/Runtime/ConnectionHealthMonitor.cs"),
        ("Assets/Scripts/ConnectionHealthView.cs", "Assets/Scripts/Runtime/ConnectionHealthView.cs"),
        ("Assets/Scripts/ControlledRockFlow.cs", "Assets/Scripts/Runtime/ControlledRockFlow.cs"),
        ("Assets/Scripts/FilteredParticleFlow.cs", "Assets/Scripts/Runtime/FilteredParticleFlow.cs"),
        ("Assets/Scripts/FpsDisplay.cs", "Assets/Scripts/Runtime/FpsDisplay.cs"),
        ("Assets/Scripts/HopperProcessSimulator.cs", "Assets/Scripts/Runtime/HopperProcessSimulator.cs"),
        ("Assets/Scripts/OrbitCameraController_Intro.cs", "Assets/Scripts/Runtime/OrbitCameraController.cs"),
        ("Assets/Scripts/ParticleFlowController.cs", "Assets/Scripts/Runtime/ParticleFlowController.cs"),
        ("Assets/Scripts/PerformanceStatCard.cs", "Assets/Scripts/Runtime/PerformanceStatCardView.cs"),
        ("Assets/Scripts/PerformanceStatsSource.cs", "Assets/Scripts/Runtime/PerformanceStatSource.cs"),
        ("Assets/Scripts/PortraitStatLeaderLineManager.cs", "Assets/Scripts/Runtime/PortraitStatLeaderLineManager.cs"),
        ("Assets/Scripts/PortraitStatRailManager.cs", "Assets/Scripts/Runtime/PortraitStatRailManager.cs"),
        ("Assets/Scripts/ResponsiveLayoutShell.cs", "Assets/Scripts/Runtime/ResponsiveLayoutShell.cs"),
        ("Assets/Scripts/Rotor_Vibrating.cs", "Assets/Scripts/Runtime/DualRotorSpin.cs"),
        ("Assets/Scripts/SafeAreaFitter.cs", "Assets/Scripts/Runtime/SafeAreaFitter.cs"),
        ("Assets/Scripts/StatLeaderLineView.cs", "Assets/Scripts/Runtime/StatLeaderLineView.cs"),
        ("Assets/Scripts/TMPOverflowScroller.cs", "Assets/Scripts/Runtime/TMPOverflowScroller.cs"),
        ("Assets/Scripts/TelemetryJsonIngestor.cs", "Assets/Scripts/Runtime/TelemetryJsonIngestor.cs"),
        ("Assets/Scripts/TelemetryOperatingModeController.cs", "Assets/Scripts/Runtime/TelemetryOperatingModeController.cs"),
        ("Assets/Scripts/TelemetryReading.cs", "Assets/Scripts/Runtime/TelemetryReading.cs"),
        ("Assets/Scripts/TelemetryRegistry.cs", "Assets/Scripts/Runtime/TelemetryRegistry.cs"),
        ("Assets/Scripts/TelemetrySequenceController.cs", "Assets/Scripts/Runtime/TelemetrySequenceController.cs"),
        ("Assets/Scripts/TelemetryValueFitter.cs", "Assets/Scripts/Runtime/TelemetryValueFitter.cs"),
        ("Assets/Scripts/Upper_Body_Vibrating.cs", "Assets/Scripts/Runtime/SeparatorVibration.cs"),
        ("Assets/Scripts/ViewModeSegmentedControl.cs", "Assets/Scripts/Runtime/ViewModeSegmentedControl.cs"),
        ("Assets/Scripts/WideStatLeaderLineManager.cs", "Assets/Scripts/Runtime/WideStatLeaderLineManager.cs"),
        ("Assets/Scripts/WideStatRailManager.cs", "Assets/Scripts/Runtime/WideStatRailManager.cs")
    };

    private static void MoveMany((string From, string To)[] moves)
    {
        foreach ((string from, string to) in moves)
            Move(from, to);
    }

    private static void Move(string from, string to)
    {
        if (AssetDatabase.LoadMainAssetAtPath(to) != null || AssetDatabase.IsValidFolder(to))
            return;

        if (AssetDatabase.LoadMainAssetAtPath(from) == null && !AssetDatabase.IsValidFolder(from))
            return;

        string error = AssetDatabase.MoveAsset(from, to);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"Could not move '{from}' to '{to}': {error}");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void RenameCaseOnly(string path, string desiredName)
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null || string.Equals(asset.name, desiredName, StringComparison.Ordinal))
            return;

        string temporaryName = $"__{desiredName.Replace(' ', '_')}_rename__";
        string error = AssetDatabase.RenameAsset(path, temporaryName);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"Could not temporarily rename '{path}': {error}");

        string directory = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string extension = System.IO.Path.GetExtension(path);
        string temporaryPath = $"{directory}/{temporaryName}{extension}";
        error = AssetDatabase.RenameAsset(temporaryPath, desiredName);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"Could not rename '{temporaryPath}': {error}");
    }
}
