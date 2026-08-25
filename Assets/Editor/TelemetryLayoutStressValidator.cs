using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
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
        ValidatePortraitPagination(errors);

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
            PerformanceStatSource source = presentation.Source;
            if (source == null)
                errors.Add($"'{presentation.Key}' has no sensor source.");
            else if (!presented.Add(source))
                errors.Add($"'{source.StatId}' appears in more than one card presentation.");
        }

        foreach (PerformanceStatSource source in sources.Where(source => source != null && source.IsVisible))
        {
            if (!presented.Contains(source))
                errors.Add($"Visible source '{source.StatId}' has no card presentation.");
        }

        int visibleSourceCount = sources.Count(source => source != null && source.IsVisible);
        if (presentations.Count != visibleSourceCount)
            errors.Add("Presentation count must exactly match the visible sensor count.");
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

        SerializedProperty horizontalPadding =
            data.FindProperty("contentPaddingInsideBackground");
        if (horizontalPadding == null ||
            !Mathf.Approximately(horizontalPadding.floatValue, 32f))
            errors.Add("Card left and right content padding must be exactly 32.");

        SerializedProperty verticalPadding = data.FindProperty("verticalContentPadding");
        if (verticalPadding == null ||
            !Mathf.Approximately(verticalPadding.floatValue, 16f))
            errors.Add("Card top and bottom content padding must be exactly 16.");

        SerializedProperty contentGap = data.FindProperty("metricContentGap");
        if (contentGap == null || !Mathf.Approximately(contentGap.floatValue, 8f))
            errors.Add("Card label-to-reading spacing must be exactly 8.");

        SerializedProperty valueHeight = data.FindProperty("valueRowHeight");
        if (valueHeight == null || !Mathf.Approximately(valueHeight.floatValue, 48f))
            errors.Add("Card reading row height must remain stable at 48.");

        TMP_Text legacyUnit = data.FindProperty("unitText")?.objectReferenceValue as TMP_Text;
        if (legacyUnit == null || legacyUnit.gameObject.activeSelf)
            errors.Add("Card unit text must remain an inactive legacy reference; the reading uses one baseline.");

        SerializedProperty labelMode = data.FindProperty("labelLayoutMode");
        SerializedProperty maximumLines = data.FindProperty("maximumWrappedLabelLines");
        SerializedProperty headerHeight = data.FindProperty("singleLineHeaderHeight");
        if (labelMode == null)
            errors.Add("Card label layout mode is unavailable in the Inspector.");
        SerializedProperty portraitSliding = data.FindProperty("useSlidingLabelsInPortrait");
        if (portraitSliding == null || !portraitSliding.boolValue)
            errors.Add("Portrait cards must slide overflowing labels by default.");
        if (maximumLines == null || maximumLines.intValue != 4)
            errors.Add("Adaptive card labels must support four lines before sliding.");
        if (headerHeight == null || headerHeight.floatValue < 28f)
            errors.Add("Card single-line header height is too small.");

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

        if (first.rectTransform.localScale != Vector3.one ||
            second.rectTransform.localScale != Vector3.one)
        {
            errors.Add(
                $"Card accent trails '{firstProperty}' and '{secondProperty}' " +
                "must use identity scale so responsive parents cannot alter thickness.");
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

        SerializedProperty resizeSettle = data.FindProperty("resizeSettleSeconds");
        if (resizeSettle == null || resizeSettle.floatValue < 0.05f)
            errors.Add("Responsive shell must debounce browser resize rebuilds.");
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
        ValidateMetricHeight(wide, errors);
        ValidateMetricHeight(portrait, errors);
    }

    private static void ValidateMetricHeight(MonoBehaviour manager, ICollection<string> errors)
    {
        if (manager == null)
            return;
        SerializedProperty height = new SerializedObject(manager).FindProperty("cardHeight");
        if (height == null || !Mathf.Approximately(height.floatValue, 119f))
            errors.Add($"'{manager.name}' base card height must be 119 for the 16/8/16 spacing model.");
    }

    private static void ValidatePortraitPagination(ICollection<string> errors)
    {
        PortraitTelemetryPager[] pagers = UnityEngine.Object
            .FindObjectsByType<PortraitTelemetryPager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        if (pagers.Length != 1)
        {
            errors.Add($"Exactly one portrait telemetry pager is required; found {pagers.Length}.");
            return;
        }

        PortraitTelemetryPager pager = pagers[0];
        if (pager.RailManager == null || pager.PreviousButton == null || pager.NextButton == null)
            errors.Add("Portrait telemetry pager has incomplete manager or button references.");
        if (!Mathf.Approximately(pager.AvailabilityFadeDuration, 0.3f))
            errors.Add("Portrait telemetry pager availability fade must be 0.3 seconds.");
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
