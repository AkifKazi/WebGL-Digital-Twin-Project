using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TelemetryLayoutStressValidator
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Digital Twin/Validate Telemetry Layout Stress")]
    public static void Validate()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        List<string> errors = new();

        PerformanceStatSource[] sources = UnityEngine.Object.FindObjectsByType<PerformanceStatSource>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<TelemetryCardPresentation> presentations = TelemetryPresentationBuilder.Build(sources);

        ValidatePresentations(sources, presentations, errors);
        ValidateCardPrefab(errors);
        ValidateResponsiveShell(errors);
        ValidateRailManagers(errors);

        Debug.Log(
            $"TELEMETRY_LAYOUT_STRESS_RESULT sensors={sources.Length} " +
            $"presentations={presentations.Count} errors={errors.Count}");

        foreach (string error in errors)
            Debug.LogError("TELEMETRY_LAYOUT_STRESS: " + error);

        if (errors.Count > 0)
            throw new InvalidOperationException("Telemetry layout stress validation failed.");
    }

    private static void ValidatePresentations(
        IReadOnlyCollection<PerformanceStatSource> sources,
        IReadOnlyCollection<TelemetryCardPresentation> presentations,
        ICollection<string> errors)
    {
        HashSet<PerformanceStatSource> presented = new();
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (TelemetryCardPresentation presentation in presentations)
        {
            if (!keys.Add(presentation.Key))
                errors.Add($"Duplicate presentation key '{presentation.Key}'.");
            if (presentation.Sources.Count > presentation.MaximumVisibleMetrics)
                errors.Add($"'{presentation.Key}' still hides metrics inside a card.");
            foreach (PerformanceStatSource source in presentation.Sources)
            {
                if (!presented.Add(source))
                    errors.Add($"'{source.StatId}' appears in more than one card presentation.");
            }
        }

        foreach (PerformanceStatSource source in sources.Where(source => source != null && source.IsVisible))
        {
            if (!presented.Contains(source))
                errors.Add($"Visible source '{source.StatId}' has no card presentation.");
        }
    }

    private static void ValidateCardPrefab(ICollection<string> errors)
    {
        PerformanceStatCardView prefab = AssetDatabase.LoadAssetAtPath<PerformanceStatCardView>(
            "Assets/Prefabs/Performance Stat Card.prefab");
        if (prefab == null)
        {
            errors.Add("Performance stat card prefab is unavailable.");
            return;
        }

        SerializedObject data = new(prefab);
        Image background = data.FindProperty("backgroundImage").objectReferenceValue as Image;
        if (background == null)
            errors.Add("Card prefab has no background image reference.");
        else if (background.rectTransform.sizeDelta.x > -1f)
            errors.Add("Card background must remain inset from the external accent-trail root.");

        if (data.FindProperty("contentPaddingInsideBackground").floatValue < 8f)
            errors.Add("Card content padding is too small for scrolling glyph overhang.");

        ValidateAccentPair(data, "leftAccentTrail", "rightAccentTrail", true, errors);
        ValidateAccentPair(data, "topAccentTrail", "bottomAccentTrail", false, errors);
    }

    private static void ValidateAccentPair(
        SerializedObject cardData,
        string firstProperty,
        string secondProperty,
        bool compareWidth,
        ICollection<string> errors)
    {
        Image first = cardData.FindProperty(firstProperty)?.objectReferenceValue as Image;
        Image second = cardData.FindProperty(secondProperty)?.objectReferenceValue as Image;
        if (first == null || second == null)
        {
            errors.Add($"Card prefab is missing '{firstProperty}' or '{secondProperty}'.");
            return;
        }

        float firstSize = compareWidth
            ? first.rectTransform.sizeDelta.x
            : first.rectTransform.sizeDelta.y;
        float secondSize = compareWidth
            ? second.rectTransform.sizeDelta.x
            : second.rectTransform.sizeDelta.y;

        if (firstSize <= 0f || secondSize <= 0f ||
            !Mathf.Approximately(firstSize, secondSize))
        {
            errors.Add(
                $"Card accent trails '{firstProperty}' and '{secondProperty}' " +
                "must have matching positive geometry.");
        }
    }

    private static void ValidateResponsiveShell(ICollection<string> errors)
    {
        ResponsiveLayoutShell shell = UnityEngine.Object.FindFirstObjectByType<ResponsiveLayoutShell>(
            FindObjectsInactive.Include);
        if (shell == null)
        {
            errors.Add("Responsive layout shell is missing.");
            return;
        }

        SerializedObject data = new(shell);
        if (data.FindProperty("landscapeLayout").objectReferenceValue == null ||
            data.FindProperty("portraitLayout").objectReferenceValue == null ||
            data.FindProperty("canvasScaler").objectReferenceValue == null)
        {
            errors.Add("Responsive shell has incomplete layout references.");
        }
        if (data.FindProperty("verticalDesktopControlHeight").floatValue < 48f)
            errors.Add("Vertical desktop controls are below the minimum touch/click height.");

        SerializedProperty mobileScale =
            data.FindProperty("mobileLandscapeBottomControlScale");
        if (mobileScale == null ||
            !Mathf.Approximately(mobileScale.floatValue, 1.1f))
        {
            errors.Add("Mobile landscape bottom controls must use the approved 10% scale increase.");
        }
    }

    private static void ValidateRailManagers(ICollection<string> errors)
    {
        WideStatRailManager wide = UnityEngine.Object.FindFirstObjectByType<WideStatRailManager>(
            FindObjectsInactive.Include);
        PortraitStatRailManager portrait = UnityEngine.Object.FindFirstObjectByType<PortraitStatRailManager>(
            FindObjectsInactive.Include);
        ValidateReferences(wide, new[]
        {
            "leftCardContainer", "rightCardContainer",
            "innerLeftCardContainer", "innerRightCardContainer"
        }, errors);
        ValidateReferences(portrait, new[]
        {
            "topCardContainer", "bottomCardContainer",
            "innerTopCardContainer", "innerBottomCardContainer"
        }, errors);
    }

    private static void ValidateReferences(
        MonoBehaviour manager,
        IEnumerable<string> names,
        ICollection<string> errors)
    {
        if (manager == null)
        {
            errors.Add("A telemetry rail manager is missing.");
            return;
        }

        SerializedObject data = new(manager);
        foreach (string name in names)
        {
            if (data.FindProperty(name)?.objectReferenceValue == null)
                errors.Add($"'{manager.name}' is missing '{name}'.");
        }
    }
}
