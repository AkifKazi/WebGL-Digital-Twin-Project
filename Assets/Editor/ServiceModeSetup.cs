using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds the service view to the wide layout: the Telemetry / Service switch at
/// the top left, the service panel on the right of the content area, the pins
/// on the model, and the controller that switches between the two. Also fills
/// the service schedule with the demonstration plans below.
///
/// Safe to run again: everything it built before is replaced.
/// </summary>
public static class ServiceModeSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string LabelFontPath = "Assets/UI/Fonts/Rajdhani-Medium SDF.asset";
    private const string ValueFontPath = "Assets/UI/Fonts/Rajdhani-SemiBold SDF.asset";

    private const float PanelWidth = 560f;
    private const float SwitchSegmentWidth = 166f;
    private const float MachineRunHours = 14860f;

    public readonly struct PlanRow
    {
        public readonly string Part;
        public readonly ServiceTask Task;
        public readonly float Plan;
        public readonly float Since;
        public readonly float Estimate;

        public PlanRow(string part, ServiceTask task, float plan, float since, float estimate)
        {
            Part = part;
            Task = task;
            Plan = plan;
            Since = since;
            Estimate = estimate;
        }
    }

    /// <summary>
    /// Demonstration plans by machine part: task, the manufacturer's interval,
    /// hours run since the last service, and the interval estimated for this
    /// machine's conditions. Replace with the site's maintenance plan.
    /// </summary>
    public static readonly PlanRow[] Plans =
    {
        new("Bearings", ServiceTask.Regrease, 1000f, 740f, 900f),
        new("Screen Deck", ServiceTask.Replace, 1500f, 1180f, 1350f),
        new("Drive Roller", ServiceTask.Regrease, 2000f, 1460f, 2200f),
        new("Motor Frame", ServiceTask.Inspect, 2000f, 1210f, 2000f),
        new("Upper Mixer Rotor", ServiceTask.Inspect, 3000f, 1740f, 2800f),
        new("Lower Rotor", ServiceTask.Inspect, 3000f, 1740f, 2800f),
        new("Drive Shaft", ServiceTask.Inspect, 4000f, 2480f, 3800f),
        new("Discharge Outlets", ServiceTask.Inspect, 2000f, 610f, 2000f),
        new("Idler Rollers", ServiceTask.Inspect, 5000f, 2150f, 5000f),
        new("Stator Windings", ServiceTask.Inspect, 8000f, 5210f, 8000f),
        new("Rotor Core", ServiceTask.Inspect, 8000f, 5210f, 8000f),
        new("Bunker", ServiceTask.Inspect, 8760f, 4020f, 8760f),
        new("Conveyor Belts", ServiceTask.Replace, 20000f, 11700f, 18500f),
        new("Support Frame", ServiceTask.Inspect, 17520f, 6300f, 17520f)
    };

    public static void WritePlan(SerializedProperty plan, MachinePartGroup part, in PlanRow row)
    {
        plan.FindPropertyRelative("part").objectReferenceValue = part;
        plan.FindPropertyRelative("task").enumValueIndex = (int)row.Task;
        plan.FindPropertyRelative("serviceIntervalHours").floatValue = row.Plan;
        plan.FindPropertyRelative("estimatedIntervalHours").floatValue = row.Estimate;
        plan.FindPropertyRelative("hoursSinceService").floatValue = row.Since;
    }

    [MenuItem("Tools/Digital Twin/Set Up Service Mode")]
    private static void SetUpFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Apply();
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("Service Mode", "The service view was added to the wide layout and the scene saved.", "OK");
    }

    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform wide = FindTransform("Wide layout");
        Transform baseLayout = Child(wide, "Base layout");
        Transform topBar = Child(baseLayout, "Top bar");
        Transform content = Child(baseLayout, "Content area");
        Transform leaderLayer = Child(wide, "Runtime Leader Line Layer");
        ViewModeSegmentedControl viewMode = Child(Child(baseLayout, "Bottom controls"), "Hopper View Mode Control")
            .GetComponent<ViewModeSegmentedControl>();

        RemoveChild(wide, "Service Mode");
        RemoveChild(wide, "Service Pin Layer");
        RemoveChild(content, "Service Panel");
        RemoveChild(topBar, "Mode Switch");

        TelemetryServiceSchedule schedule = ConfigureSchedule();
        TMP_FontAsset labelFont = Load<TMP_FontAsset>(LabelFontPath);
        TMP_FontAsset valueFont = Load<TMP_FontAsset>(ValueFontPath);
        Camera camera = Camera.main ?? throw new InvalidOperationException("Main Camera not found.");

        // Telemetry parts that fade out in Service. Pagination is wrapped so
        // its own CanvasGroup stays free for the pager's availability fade.
        List<CanvasGroup> telemetryGroups = new()
        {
            Group(WrapPagination(topBar)),
            Group(leaderLayer),
            Group(Child(content, "Left primary rail")),
            Group(Child(content, "Right primary rail")),
            Group(Child(content, "Left secondary rail")),
            Group(Child(content, "Right secondary rail")),
            Group(Child(content, "Telemetry Detail Layer"))
        };

        RectTransform panel = CreatePanel(content, viewMode, schedule, labelFont, valueFont);
        RectTransform pinLayer = CreatePinLayer(wide, leaderLayer, labelFont);

        GameObject modeObject = new("Service Mode", typeof(RectTransform));
        modeObject.layer = wide.gameObject.layer;
        modeObject.transform.SetParent(wide, false);
        ServiceModeController controller = modeObject.AddComponent<ServiceModeController>();
        SerializedObject data = new(controller);
        data.FindProperty("targetCamera").objectReferenceValue = camera;
        data.FindProperty("servicePanel").objectReferenceValue = panel;
        SetArray(data.FindProperty("serviceGroups"), new UnityEngine.Object[] { panel.GetComponent<CanvasGroup>(), pinLayer.GetComponent<CanvasGroup>() });
        SetArray(data.FindProperty("telemetryGroups"), telemetryGroups.Cast<UnityEngine.Object>().ToArray());
        data.FindProperty("detailPanels").objectReferenceValue =
            content.GetComponentInChildren<TelemetryDetailPanelController>(true);
        data.FindProperty("focusReleaser").objectReferenceValue =
            UnityEngine.Object.FindAnyObjectByType<TelemetryCardFocusReleaser>(FindObjectsInactive.Include);
        data.ApplyModifiedPropertiesWithoutUndo();

        ServicePinLayer pins = pinLayer.GetComponent<ServicePinLayer>();
        SerializedObject pinData = new(pins);
        pinData.FindProperty("controller").objectReferenceValue = controller;
        pinData.FindProperty("panel").objectReferenceValue = panel.GetComponent<ServicePanelView>();
        pinData.FindProperty("targetCamera").objectReferenceValue = camera;
        pinData.ApplyModifiedPropertiesWithoutUndo();

        CreateSwitch(topBar, viewMode, controller);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"SERVICE_MODE_SETUP plans={schedule.Plans.Count} telemetryGroups={telemetryGroups.Count}");
    }

    // -----------------------------------------------------------------------
    // Schedule
    // -----------------------------------------------------------------------

    private static TelemetryServiceSchedule ConfigureSchedule()
    {
        TelemetryRegistry registry = UnityEngine.Object.FindAnyObjectByType<TelemetryRegistry>(FindObjectsInactive.Include)
            ?? throw new InvalidOperationException("Telemetry Registry not found.");
        if (!registry.TryGetComponent(out TelemetryServiceSchedule schedule))
            schedule = registry.gameObject.AddComponent<TelemetryServiceSchedule>();

        Dictionary<string, MachinePartGroup> parts = UnityEngine.Object
            .FindObjectsByType<MachinePartGroup>(FindObjectsInactive.Include)
            .GroupBy(part => part.name)
            .ToDictionary(group => group.Key, group => group.First());

        SerializedObject data = new(schedule);
        SerializedProperty plans = data.FindProperty("plans");
        plans.arraySize = 0;
        foreach (PlanRow row in Plans)
        {
            if (!parts.TryGetValue(row.Part, out MachinePartGroup part))
            {
                Debug.LogWarning($"SERVICE_MODE_SETUP no machine part named '{row.Part}'; its plan is skipped.");
                continue;
            }
            int index = plans.arraySize++;
            WritePlan(plans.GetArrayElementAtIndex(index), part, row);
        }

        data.FindProperty("machineRunHours").floatValue = MachineRunHours;
        data.ApplyModifiedPropertiesWithoutUndo();
        return schedule;
    }

    // -----------------------------------------------------------------------
    // Top bar
    // -----------------------------------------------------------------------

    private static RectTransform WrapPagination(Transform topBar)
    {
        Transform wrapper = topBar.Find("Telemetry Top Controls");
        if (wrapper == null)
        {
            RectTransform rect = NewRect("Telemetry Top Controls", topBar);
            Stretch(rect);
            wrapper = rect;
        }

        Transform pagination = topBar.Find("Telemetry Pagination Controls");
        if (pagination != null)
            pagination.SetParent(wrapper, false);
        return (RectTransform)wrapper;
    }

    private static void CreateSwitch(Transform topBar, ViewModeSegmentedControl viewMode, ServiceModeController controller)
    {
        Transform source = viewMode.transform;
        RectTransform root = NewRect("Mode Switch", topBar);
        root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
        root.pivot = new Vector2(0f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(SwitchSegmentWidth * 2f + 12f, 68f);

        // The container is drawn exactly like the view-mode control's.
        Image sourceBackground = source.GetComponent<Image>();
        Image background = root.gameObject.AddComponent<Image>();
        CopyImage(sourceBackground, background);
        HorizontalLayoutGroup sourceStack = source.GetComponent<HorizontalLayoutGroup>();
        HorizontalLayoutGroup stack = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        stack.padding = new RectOffset(sourceStack.padding.left, sourceStack.padding.right, sourceStack.padding.top, sourceStack.padding.bottom);
        stack.spacing = sourceStack.spacing;
        stack.childAlignment = sourceStack.childAlignment;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = true;

        Transform template = Child(source, "Full Model Button");
        Button telemetry = CloneSegment(template, root, "Telemetry Button", "TELEMETRY");
        Button service = CloneSegment(template, root, "Service Button", "SERVICE");

        DigitalTwinModeSwitch modeSwitch = root.gameObject.AddComponent<DigitalTwinModeSwitch>();
        SerializedObject sourceData = new(viewMode);
        SerializedObject data = new(modeSwitch);
        data.FindProperty("controller").objectReferenceValue = controller;
        data.FindProperty("telemetryButton").objectReferenceValue = telemetry;
        data.FindProperty("serviceButton").objectReferenceValue = service;
        data.FindProperty("selectedSprite").objectReferenceValue = sourceData.FindProperty("selectedSprite").objectReferenceValue;
        data.FindProperty("idleSprite").objectReferenceValue = sourceData.FindProperty("inactiveSprite").objectReferenceValue;
        data.FindProperty("selectedTextColor").colorValue = sourceData.FindProperty("selectedTextColor").colorValue;
        data.FindProperty("idleTextColor").colorValue = sourceData.FindProperty("inactiveTextColor").colorValue;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A segment cloned from the view-mode control: same sprites, hover and type.</summary>
    private static Button CloneSegment(Transform template, Transform parent, string name, string text)
    {
        GameObject segment = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        segment.name = name;

        Transform icon = segment.transform.Find("Icon");
        if (icon != null)
            UnityEngine.Object.DestroyImmediate(icon.gameObject);

        TMP_Text label = segment.GetComponentInChildren<TMP_Text>(true);
        label.text = text;

        LayoutElement size = segment.GetComponent<LayoutElement>();
        size.preferredWidth = SwitchSegmentWidth;

        Button button = segment.GetComponent<Button>();
        // Drop anything wired to the view-mode control.
        button.onClick = new Button.ButtonClickedEvent();
        return button;
    }

    // -----------------------------------------------------------------------
    // Service panel and pins
    // -----------------------------------------------------------------------

    private static RectTransform CreatePanel(
        Transform content,
        ViewModeSegmentedControl viewMode,
        TelemetryServiceSchedule schedule,
        TMP_FontAsset labelFont,
        TMP_FontAsset valueFont)
    {
        RectTransform panel = NewRect("Service Panel", content);
        panel.SetAsLastSibling();
        LayoutElement layout = panel.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = Vector2.one;
        panel.pivot = new Vector2(1f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(PanelWidth, 0f);

        // The same framed background as every other control.
        Image background = panel.gameObject.AddComponent<Image>();
        CopyImage(viewMode.GetComponent<Image>(), background);
        background.raycastTarget = true;

        CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        ServicePanelView view = panel.gameObject.AddComponent<ServicePanelView>();
        SerializedObject data = new(view);
        data.FindProperty("schedule").objectReferenceValue = schedule;
        data.FindProperty("labelFont").objectReferenceValue = labelFont;
        data.FindProperty("valueFont").objectReferenceValue = valueFont;
        data.ApplyModifiedPropertiesWithoutUndo();
        return panel;
    }

    private static RectTransform CreatePinLayer(Transform wide, Transform leaderLayer, TMP_FontAsset font)
    {
        RectTransform layer = NewRect("Service Pin Layer", wide);
        Stretch(layer);
        // Under the panels and bars, like the leader lines.
        layer.SetSiblingIndex(leaderLayer.GetSiblingIndex() + 1);

        CanvasGroup group = layer.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        ServicePinLayer pins = layer.gameObject.AddComponent<ServicePinLayer>();
        SerializedObject data = new(pins);
        data.FindProperty("font").objectReferenceValue = font;
        data.ApplyModifiedPropertiesWithoutUndo();
        return layer;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CanvasGroup Group(Transform target)
    {
        if (!target.TryGetComponent(out CanvasGroup group))
            group = target.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private static void CopyImage(Image from, Image to)
    {
        to.sprite = from.sprite;
        to.type = from.type;
        to.color = from.color;
        to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
        to.fillCenter = from.fillCenter;
        to.material = from.material;
    }

    private static void SetArray(SerializedProperty array, UnityEngine.Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindTransform(string name) =>
        UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(t => t.name == name)
        ?? throw new InvalidOperationException($"'{name}' not found in {ScenePath}.");

    private static Transform Child(Transform parent, string name) =>
        parent.Find(name) ?? throw new InvalidOperationException($"'{parent.name}/{name}' not found.");

    private static void RemoveChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
    }

    private static T Load<T>(string path) where T : UnityEngine.Object =>
        AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException($"'{path}' not found.");
}
