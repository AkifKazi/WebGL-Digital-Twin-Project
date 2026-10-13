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
/// the top left, the service panel as a column down the right of the screen
/// that the viewport narrows beside, the pins on the model, and the controller
/// that switches between the two. Also fills
/// the service schedule with the demonstration plans below.
///
/// Then adds it to the portrait layout (see <see cref="ApplyPortrait"/>): the
/// switch centred in the top bar, and the panel over the whole screen below it.
///
/// Safe to run again: everything it built before is replaced.
/// </summary>
public static class ServiceModeSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string LabelFontPath = "Assets/UI/Fonts/Rajdhani-Medium SDF.asset";
    private const string ValueFontPath = "Assets/UI/Fonts/Rajdhani-SemiBold SDF.asset";
    private const string RunBarShaderPath = "Assets/Materials/UI/ServiceRunBar.shader";
    private const string RunBarMaterialPath = "Assets/Materials/UI/Service Run Bar.mat";

    private const float PanelWidth = 560f;
    private const float SwitchSegmentWidth = 166f;
    private const float PortraitSwitchSegmentWidth = 190f;
    // Opaque: the portrait service view hides the model entirely.
    private static readonly Color PortraitBackdrop = new(0.031f, 0.086f, 0.118f, 1f);
    private const float MachineRunHours = 14860f;

    public readonly struct PlanRow
    {
        public readonly string Part;
        public readonly ServiceTask Task;
        public readonly float Plan;
        public readonly float Since;
        public readonly float Estimate;
        public readonly ServiceCriticality Criticality;
        public readonly string Cause;
        public readonly string Fix;
        public readonly string Materials;
        public readonly float Minutes;
        public readonly float[] History;

        public PlanRow(string part, ServiceTask task, float plan, float since, float estimate,
            ServiceCriticality criticality, string cause, string fix, string materials, float minutes, params float[] history)
        {
            Part = part;
            Task = task;
            Plan = plan;
            Since = since;
            Estimate = estimate;
            Criticality = criticality;
            Cause = cause;
            Fix = fix;
            Materials = materials;
            Minutes = minutes;
            History = history ?? Array.Empty<float>();
        }
    }

    private const string PlannedRate = "Wear follows the planned rate";

    /// <summary>
    /// Demonstration plans by machine part: task, the manufacturer's interval,
    /// hours run since the last service, the interval estimated for this
    /// machine's conditions, then the detail view's data: criticality, likely
    /// cause, suggested fix, materials, minutes the service takes, and the
    /// hours run before each past service, oldest first. Replace with the
    /// site's maintenance plan.
    /// </summary>
    public static readonly PlanRow[] Plans =
    {
        new("Bearings", ServiceTask.Regrease, 1000f, 740f, 900f, ServiceCriticality.High,
            "Bearings run 8 \u00b0C above normal, so the grease breaks down sooner",
            "Regrease both bearings, then check the temperature settles",
            "Lithium complex grease, NLGI 2, 30 g per bearing", 30f, 980f, 930f, 890f),
        new("Screen Deck", ServiceTask.Replace, 1500f, 1180f, 1350f, ServiceCriticality.High,
            "Feed heavier than rated wears the mesh faster",
            "Replace the screen panels and re-tension the deck",
            "Polyurethane screen panels, 20 mm aperture, full deck", 120f, 1490f, 1410f, 1330f),
        new("Drive Roller", ServiceTask.Regrease, 2000f, 1460f, 2200f, ServiceCriticality.Medium,
            "Light belt load keeps the roller bearings cool",
            "Regrease the roller bearings",
            "Lithium complex grease, NLGI 2, 20 g per bearing", 20f, 2150f, 2240f, 2180f),
        new("Motor Frame", ServiceTask.Inspect, 2000f, 1210f, 2000f, ServiceCriticality.Medium,
            PlannedRate,
            "Check the mounting bolts and the frame for cracks",
            "None", 30f, 2000f, 1990f, 2010f),
        new("Upper Mixer Rotor", ServiceTask.Inspect, 3000f, 1740f, 2800f, ServiceCriticality.High,
            "Abrasive feed wears the rotor tips faster",
            "Measure rotor tip wear and check the balance",
            "None for the inspection. Rotor tip set held in stock", 45f, 2950f, 2860f, 2790f),
        new("Lower Rotor", ServiceTask.Inspect, 3000f, 1740f, 2800f, ServiceCriticality.High,
            "Abrasive feed wears the rotor tips faster",
            "Measure rotor tip wear and check the balance",
            "None for the inspection. Rotor tip set held in stock", 45f, 2980f, 2840f, 2800f),
        new("Drive Shaft", ServiceTask.Inspect, 4000f, 2480f, 3800f, ServiceCriticality.Medium,
            "Vibration slightly above baseline loads the couplings",
            "Check coupling alignment and shaft run-out",
            "None for the inspection. Coupling insert held in stock", 60f, 3950f, 3860f),
        new("Discharge Outlets", ServiceTask.Inspect, 2000f, 610f, 2000f, ServiceCriticality.Low,
            PlannedRate,
            "Clear build-up and check the liners",
            "None", 30f, 2000f, 1980f, 2020f),
        new("Idler Rollers", ServiceTask.Inspect, 5000f, 2150f, 5000f, ServiceCriticality.Low,
            PlannedRate,
            "Spin-check the idlers and replace any that drag",
            "Idler rollers, 89 mm, as needed", 60f, 4950f, 5020f),
        new("Stator Windings", ServiceTask.Inspect, 8000f, 5210f, 8000f, ServiceCriticality.High,
            PlannedRate,
            "Measure insulation resistance and check the terminals",
            "None", 90f, 8000f, 7900f),
        new("Rotor Core", ServiceTask.Inspect, 8000f, 5210f, 8000f, ServiceCriticality.Medium,
            PlannedRate,
            "Check the rotor core and the air gap",
            "None", 90f, 8050f, 7980f),
        new("Bunker", ServiceTask.Inspect, 8760f, 4020f, 8760f, ServiceCriticality.Low,
            PlannedRate,
            "Inspect the liners and welds for wear",
            "None", 60f, 8760f),
        new("Conveyor Belts", ServiceTask.Replace, 20000f, 11700f, 18500f, ServiceCriticality.High,
            "Sharp rock edges cut the top cover faster",
            "Replace the belt and re-tension it",
            "EP 400/3 belt, 6 + 2 mm covers, 24 m", 480f, 19200f),
        new("Support Frame", ServiceTask.Inspect, 17520f, 6300f, 17520f, ServiceCriticality.Low,
            PlannedRate,
            "Check welds, bolts and corrosion",
            "None", 120f, 17500f)
    };

    public static void WritePlan(SerializedProperty plan, MachinePartGroup part, in PlanRow row)
    {
        plan.FindPropertyRelative("part").objectReferenceValue = part;
        plan.FindPropertyRelative("task").enumValueIndex = (int)row.Task;
        plan.FindPropertyRelative("serviceIntervalHours").floatValue = row.Plan;
        plan.FindPropertyRelative("estimatedIntervalHours").floatValue = row.Estimate;
        plan.FindPropertyRelative("hoursSinceService").floatValue = row.Since;
        plan.FindPropertyRelative("criticality").enumValueIndex = (int)row.Criticality;
        plan.FindPropertyRelative("likelyCause").stringValue = row.Cause;
        plan.FindPropertyRelative("suggestedFix").stringValue = row.Fix;
        plan.FindPropertyRelative("materials").stringValue = row.Materials;
        plan.FindPropertyRelative("durationMinutes").floatValue = row.Minutes;
        SerializedProperty history = plan.FindPropertyRelative("pastIntervals");
        history.arraySize = row.History.Length;
        for (int i = 0; i < row.History.Length; i++)
            history.GetArrayElementAtIndex(i).floatValue = row.History[i];
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
        RemoveChild(wide, "Service Panel");
        RemoveChild(content, "Service Panel");
        RemoveChild(topBar, "Mode Switch");

        TelemetryServiceSchedule schedule = ConfigureSchedule();
        TMP_FontAsset labelFont = Load<TMP_FontAsset>(LabelFontPath);
        TMP_FontAsset valueFont = Load<TMP_FontAsset>(ValueFontPath);
        Camera camera = Camera.main ?? throw new InvalidOperationException("Main Camera not found.");

        // Pagination is wrapped so its own CanvasGroup stays free for the
        // pager's availability fade; the wrapper is a telemetry region.
        WrapPagination(topBar);

        RectTransform pinLayer = CreatePinLayer(wide, leaderLayer, labelFont);
        RectTransform panel = CreatePanel(wide, schedule, labelFont, valueFont, false);

        GameObject modeObject = new("Service Mode", typeof(RectTransform));
        modeObject.layer = wide.gameObject.layer;
        modeObject.transform.SetParent(wide, false);
        WideServiceView controller = modeObject.AddComponent<WideServiceView>();
        TelemetryVisibility visibility = TelemetryVisibilitySetup.Ensure(wide);
        SerializedObject data = new(controller);
        data.FindProperty("targetCamera").objectReferenceValue = camera;
        data.FindProperty("servicePanel").objectReferenceValue = panel;
        data.FindProperty("viewport").objectReferenceValue = baseLayout;
        data.FindProperty("contentArea").objectReferenceValue = content;
        data.FindProperty("panelWidth").floatValue = PanelWidth;
        data.ApplyModifiedPropertiesWithoutUndo();

        ServicePinLayer pins = pinLayer.GetComponent<ServicePinLayer>();
        SerializedObject pinData = new(pins);
        pinData.FindProperty("panel").objectReferenceValue = panel.GetComponent<ServicePanelView>();
        pinData.FindProperty("targetCamera").objectReferenceValue = camera;
        pinData.ApplyModifiedPropertiesWithoutUndo();

        CreateSwitch(topBar, viewMode, visibility, false, SwitchSegmentWidth);
        BuildPortrait(schedule, labelFont, valueFont);

        // Points the switches, pins, sheet, pagers and sequences at each
        // layout's telemetry visibility, and gives it its regions.
        TelemetryVisibilitySetup.WireAll();
        // The switches were rebuilt: lines fade over them again.
        LeaderLineObstacleSetup.MarkAll();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"SERVICE_MODE_SETUP plans={schedule.Plans.Count}");
    }

    [MenuItem("Tools/Digital Twin/Set Up Portrait Service Mode")]
    private static void SetUpPortraitFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        ApplyPortrait();
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("Service Mode", "The service view was added to the portrait layout and the scene saved.", "OK");
    }

    /// <summary>
    /// Adds the service view to the portrait layout only, leaving the wide
    /// layout's as it is. Safe to run again.
    /// </summary>
    public static void ApplyPortrait()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TelemetryServiceSchedule schedule = UnityEngine.Object.FindAnyObjectByType<TelemetryServiceSchedule>(FindObjectsInactive.Include)
            ?? throw new InvalidOperationException("Run Set Up Service Mode first: no service schedule.");
        BuildPortrait(schedule, Load<TMP_FontAsset>(LabelFontPath), Load<TMP_FontAsset>(ValueFontPath));
        TelemetryVisibilitySetup.Wire(FindTransform("Portrait layout"), false);
        LeaderLineObstacleSetup.MarkAll();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// The portrait service view: the switch centred in the top bar, sized like
    /// the bottom controls; an opaque fill out to the screen's edges behind it;
    /// the panel over the rest of the screen; and the service view, which
    /// <see cref="TelemetryVisibilitySetup"/> then points at the layout's visibility.
    /// </summary>
    private static void BuildPortrait(TelemetryServiceSchedule schedule, TMP_FontAsset labelFont, TMP_FontAsset valueFont)
    {
        Transform portrait = FindTransform("Portrait layout");
        Transform baseLayout = Child(portrait, "Base layout");
        Transform topBar = Child(baseLayout, "Top bar");
        Transform content = Child(baseLayout, "Content area");
        Transform bottom = Child(baseLayout, "Bottom controls");
        ViewModeSegmentedControl viewMode = bottom.GetComponentInChildren<ViewModeSegmentedControl>(true)
            ?? throw new InvalidOperationException("The portrait view-mode control was not found.");

        RemoveChild(portrait, "Service Mode");
        RemoveChild(portrait, "Service Panel");
        RemoveChild(portrait, "Service Backdrop");
        RemoveChild(topBar, "Mode Switch");

        // First, so the switch in the top bar and the panel draw over it.
        RectTransform backdrop = NewRect("Service Backdrop", portrait);
        backdrop.SetAsFirstSibling();
        Stretch(backdrop);
        Image fill = backdrop.gameObject.AddComponent<Image>();
        fill.color = PortraitBackdrop;
        // Catches the pointer, so a touch on the service view never turns the model.
        fill.raycastTarget = true;
        CanvasGroup backdropGroup = backdrop.gameObject.AddComponent<CanvasGroup>();
        backdropGroup.alpha = 0f;
        backdropGroup.interactable = false;
        backdropGroup.blocksRaycasts = false;

        RectTransform panel = CreatePanel(portrait, schedule, labelFont, valueFont, true);

        GameObject modeObject = new("Service Mode", typeof(RectTransform));
        modeObject.layer = portrait.gameObject.layer;
        modeObject.transform.SetParent(portrait, false);
        PortraitServiceView controller = modeObject.AddComponent<PortraitServiceView>();
        TelemetryVisibility visibility = TelemetryVisibilitySetup.Ensure(portrait);
        SerializedObject data = new(controller);
        data.FindProperty("servicePanel").objectReferenceValue = panel;
        data.FindProperty("topBar").objectReferenceValue = topBar;
        data.FindProperty("backdrop").objectReferenceValue = backdrop;
        data.ApplyModifiedPropertiesWithoutUndo();

        CreateSwitch(topBar, viewMode, visibility, true, PortraitSwitchSegmentWidth);
        BuildTelemetrySheet(portrait, baseLayout, topBar, Child(content, "Bottom primary rail"), visibility, labelFont);
        Debug.Log("SERVICE_MODE_SETUP portrait service view added");
    }

    /// <summary>
    /// The portrait telemetry sheet: a layer over the cards and under the
    /// service panel, set up like the wide layout's detail panels (the same
    /// prefab, graphs, figures and providers). The Time To Service figure
    /// takes the other controls' hover look (a hovered Segment) on its own.
    /// </summary>
    private static void BuildTelemetrySheet(Transform portrait, Transform baseLayout, Transform topBar, Transform bottomRail,
        TelemetryVisibility visibility, TMP_FontAsset font)
    {
        TelemetryDetailPanelController wide = UnityEngine.Object.FindAnyObjectByType<TelemetryDetailPanelController>(FindObjectsInactive.Include)
            ?? throw new InvalidOperationException("The wide layout's Telemetry Detail Panel Controller was not found.");
        RemoveChild(portrait, "Telemetry Sheet Layer");
        RectTransform layer = NewRect("Telemetry Sheet Layer", portrait);
        Stretch(layer);
        layer.SetSiblingIndex(baseLayout.GetSiblingIndex() + 1);

        PortraitTelemetrySheetController sheet = layer.gameObject.AddComponent<PortraitTelemetrySheetController>();
        SerializedObject wideData = new(wide);
        SerializedObject data = new(sheet);
        foreach (string shared in new[]
                 {
                     "panelPrefab", "historyProvider", "insightProvider", "serviceLifeProvider",
                     "graphs", "keyFigures", "keyFigureLayout", "defaultRange", "graphStyle"
                 })
            data.CopyFromSerializedProperty(wideData.FindProperty(shared));
        data.FindProperty("visibility").objectReferenceValue = visibility;
        data.FindProperty("cardRoot").objectReferenceValue = portrait;
        data.FindProperty("hintAnchor").objectReferenceValue = bottomRail;
        data.FindProperty("topBar").objectReferenceValue = topBar;
        data.FindProperty("targetCamera").objectReferenceValue = Camera.main;
        data.FindProperty("hintFont").objectReferenceValue = font;
        data.ApplyModifiedPropertiesWithoutUndo();
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

    /// <summary>
    /// The Telemetry / Service switch, at the top bar's left, or
    /// <paramref name="centred"/> and drawn at the view-mode control's scale.
    /// </summary>
    private static void CreateSwitch(Transform topBar, ViewModeSegmentedControl viewMode, MonoBehaviour controller,
        bool centred, float segmentWidth)
    {
        Transform source = viewMode.transform;
        RectTransform root = NewRect("Mode Switch", topBar);
        Vector2 anchor = centred ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
        root.anchorMin = root.anchorMax = anchor;
        root.pivot = anchor;
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(segmentWidth * 2f + 12f, 68f);
        if (centred)
            root.localScale = source.localScale;

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
        Button telemetry = CloneSegment(template, root, "Telemetry Button", "TELEMETRY", segmentWidth);
        Button service = CloneSegment(template, root, "Service Button", "SERVICE", segmentWidth);

        // Its segments are Segment shapes cloned from the view-mode control's,
        // shown chosen or at rest by state: no sprites to hand over.
        DigitalTwinModeSwitch modeSwitch = root.gameObject.AddComponent<DigitalTwinModeSwitch>();
        SerializedObject data = new(modeSwitch);
        data.FindProperty("controller").objectReferenceValue = controller;
        data.FindProperty("telemetryButton").objectReferenceValue = telemetry;
        data.FindProperty("serviceButton").objectReferenceValue = service;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A segment cloned from the view-mode control: same shape, hover and type.</summary>
    private static Button CloneSegment(Transform template, Transform parent, string name, string text, float width)
    {
        GameObject segment = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        segment.name = name;

        Transform icon = segment.transform.Find("Icon");
        if (icon != null)
            UnityEngine.Object.DestroyImmediate(icon.gameObject);

        TMP_Text label = segment.GetComponentInChildren<TMP_Text>(true);
        label.text = text;

        LayoutElement size = segment.GetComponent<LayoutElement>();
        size.preferredWidth = width;

        Button button = segment.GetComponent<Button>();
        // Drop anything wired to the view-mode control.
        button.onClick = new Button.ButtonClickedEvent();
        return button;
    }

    // -----------------------------------------------------------------------
    // Service panel and pins
    // -----------------------------------------------------------------------

    private static RectTransform CreatePanel(
        Transform layout,
        TelemetryServiceSchedule schedule,
        TMP_FontAsset labelFont,
        TMP_FontAsset valueFont,
        bool fullScreen)
    {
        // Over everything else in the layout. The controller places it and
        // brings it in; in the wide layout it starts beyond the right edge.
        RectTransform panel = NewRect("Service Panel", layout);
        panel.SetAsLastSibling();
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = Vector2.one;
        panel.pivot = new Vector2(1f, 0.5f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = new Vector2(PanelWidth, 0f);

        CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        ServicePanelView view = panel.gameObject.AddComponent<ServicePanelView>();
        SerializedObject data = new(view);
        data.FindProperty("schedule").objectReferenceValue = schedule;
        data.FindProperty("labelFont").objectReferenceValue = labelFont;
        data.FindProperty("valueFont").objectReferenceValue = valueFont;
        data.FindProperty("runBarMaterial").objectReferenceValue = RunBarMaterial();
        data.FindProperty("fullScreen").boolValue = fullScreen;
        data.ApplyModifiedPropertiesWithoutUndo();
        return panel;
    }

    /// <summary>The run bars' material, made from its shader the first time.</summary>
    private static Material RunBarMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RunBarMaterialPath);
        if (material != null)
            return material;

        material = new Material(Load<Shader>(RunBarShaderPath)) { name = "Service Run Bar" };
        AssetDatabase.CreateAsset(material, RunBarMaterialPath);
        AssetDatabase.SaveAssets();
        return material;
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
