using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PortraitPaginationUISetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PortraitRowName = "Telemetry Pagination and Zoom";
    private const string WidePaginationName = "Telemetry Pagination Controls";
    private const string SpacerName = "Bottom Control Spacer";
    private const string PanelPath = "Assets/UI/Sprites/Panel Background.png";
    private const string RestingPath = "Assets/UI/Sprites/Solid UI Fill.png";
    private const string PressedPath = "Assets/UI/Sprites/Compact Button Background.png";
    private const string PreviousIconPath = "Assets/UI/Sprites/arrow-left.png";
    private const string NextIconPath = "Assets/UI/Sprites/arrow-right.png";
    private const float PortraitSpacing = 26f;
    private const float MinimumArrowWidth = 96f;
    private const float InteractionWidth = 96f;

    [MenuItem("Tools/Digital Twin/Set Up Telemetry Pagination")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        HybridHopperClipController hopper = UnityEngine.Object
            .FindAnyObjectByType<HybridHopperClipController>(FindObjectsInactive.Include);
        PortraitStatRailManager portraitManager = UnityEngine.Object
            .FindAnyObjectByType<PortraitStatRailManager>(FindObjectsInactive.Include);
        WideStatRailManager wideManager = UnityEngine.Object
            .FindAnyObjectByType<WideStatRailManager>(FindObjectsInactive.Include);
        if (hopper == null || portraitManager == null || wideManager == null)
            throw new InvalidOperationException("Telemetry managers or machine view controller are missing.");

        RectTransform portraitBottom = FindInLayout("Portrait layout", "Bottom controls");
        RectTransform wideBottom = FindInLayout("Wide layout", "Bottom controls");
        RectTransform wideTop = FindInLayout("Wide layout", "Top bar");
        if (portraitBottom == null || wideBottom == null || wideTop == null)
            throw new InvalidOperationException("Responsive control containers are missing.");

        ConfigurePortrait(portraitBottom, portraitManager, hopper);
        ConfigureWide(wideBottom, wideTop, wideManager, hopper);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Portrait and wide telemetry controls configured successfully.");
    }

    private static void ConfigurePortrait(
        RectTransform bottom,
        PortraitStatRailManager manager,
        HybridHopperClipController hopper)
    {
        RectTransform zoom = FindDescendant(bottom, "Camera Zoom Controls");
        RectTransform view = bottom.GetComponentsInChildren<ViewModeSegmentedControl>(true)
            .Select(control => control.transform as RectTransform).FirstOrDefault();
        if (zoom == null || view == null)
            throw new InvalidOperationException("Portrait zoom or view controls are missing.");

        Transform existing = bottom.Find(PortraitRowName);
        if (existing != null)
        {
            zoom.SetParent(bottom, false);
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        float width = Mathf.Max(1f, view.sizeDelta.x);
        float height = Mathf.Max(1f, zoom.sizeDelta.y);
        float zoomWidth = Mathf.Max(1f, zoom.sizeDelta.x);
        GameObject rowObject = CreateUiObject(PortraitRowName, bottom);
        RectTransform row = rowObject.GetComponent<RectTransform>();
        row.sizeDelta = new Vector2(width, height);
        row.localScale = view.localScale;
        row.SetSiblingIndex(0);
        ConfigureFixedLayout(rowObject, width, height);

        HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset();
        layout.spacing = PortraitSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        Button previous = CreatePortraitPageButton(
            "Previous Telemetry Page", row, LoadSprite(PreviousIconPath), height, out Graphic previousIcon);
        zoom.SetParent(row, false);
        zoom.SetSiblingIndex(1);
        NormaliseNestedControl(zoom);
        ConfigureFixedLayout(zoom.gameObject, zoomWidth, height);
        EnsureZoomAvailability(zoom);
        Button next = CreatePortraitPageButton(
            "Next Telemetry Page", row, LoadSprite(NextIconPath), height, out Graphic nextIcon);

        ConfigurePager(
            rowObject.AddComponent<PortraitTelemetryPager>(), manager, null, hopper, null,
            previous, previous.GetComponent<CanvasGroup>(), previousIcon,
            next, next.GetComponent<CanvasGroup>(), nextIcon);
    }

    private static void ConfigureWide(
        RectTransform bottom,
        RectTransform top,
        WideStatRailManager manager,
        HybridHopperClipController hopper)
    {
        RectTransform zoom = FindDescendant(bottom, "Camera Zoom Controls");
        RectTransform view = bottom.GetComponentsInChildren<ViewModeSegmentedControl>(true)
            .Select(control => control.transform as RectTransform).FirstOrDefault();
        if (zoom == null || view == null)
            throw new InvalidOperationException("Wide zoom or view controls are missing.");

        Transform oldRow = bottom.Find(PortraitRowName);
        if (oldRow != null)
        {
            zoom.SetParent(bottom, false);
            UnityEngine.Object.DestroyImmediate(oldRow.gameObject);
        }
        DestroyChild(bottom, SpacerName);
        DestroyChild(top, WidePaginationName);

        float zoomWidth = Mathf.Max(1f, zoom.sizeDelta.x);
        float controlHeight = Mathf.Max(1f, zoom.sizeDelta.y);
        ConfigureWideBottom(bottom, view, zoom, zoomWidth, controlHeight);
        EnsureZoomAvailability(zoom);

        GameObject paginationObject = UnityEngine.Object.Instantiate(zoom.gameObject, top);
        paginationObject.name = WidePaginationName;
        foreach (EventTrigger trigger in paginationObject.GetComponentsInChildren<EventTrigger>(true))
            UnityEngine.Object.DestroyImmediate(trigger);
        ZoomControlAvailabilityView clonedZoomView =
            paginationObject.GetComponent<ZoomControlAvailabilityView>();
        if (clonedZoomView != null)
            UnityEngine.Object.DestroyImmediate(clonedZoomView);

        RectTransform paginationRect = paginationObject.GetComponent<RectTransform>();
        paginationRect.anchorMin = new Vector2(1f, 0.5f);
        paginationRect.anchorMax = new Vector2(1f, 0.5f);
        paginationRect.pivot = new Vector2(1f, 0.5f);
        paginationRect.anchoredPosition = Vector2.zero;
        paginationRect.localScale = Vector3.one;
        paginationRect.sizeDelta = new Vector2(zoomWidth, controlHeight);

        LayoutElement topLayout = top.GetComponent<LayoutElement>();
        if (topLayout != null)
            topLayout.preferredHeight = Mathf.Max(topLayout.preferredHeight, controlHeight);

        Button[] buttons = paginationObject.GetComponentsInChildren<Button>(true)
            .OrderBy(button => button.transform.GetSiblingIndex()).ToArray();
        if (buttons.Length != 2)
            throw new InvalidOperationException("Copied pagination control must contain two buttons.");

        Button previous = buttons[0];
        Button next = buttons[1];
        previous.name = "Previous Telemetry Page";
        next.name = "Next Telemetry Page";
        previous.onClick.RemoveAllListeners();
        next.onClick.RemoveAllListeners();
        previous.navigation = new Navigation { mode = Navigation.Mode.None };
        next.navigation = new Navigation { mode = Navigation.Mode.None };
        Image previousIcon = FindButtonIcon(previous);
        Image nextIcon = FindButtonIcon(next);
        previousIcon.sprite = LoadSprite(PreviousIconPath);
        nextIcon.sprite = LoadSprite(NextIconPath);
        previousIcon.preserveAspect = true;
        nextIcon.preserveAspect = true;
        CanvasGroup previousGroup = GetOrAddCanvasGroup(previous.gameObject);
        CanvasGroup nextGroup = GetOrAddCanvasGroup(next.gameObject);
        CanvasGroup paginationGroup = GetOrAddCanvasGroup(paginationObject);

        ConfigurePager(
            paginationObject.AddComponent<PortraitTelemetryPager>(), null, manager, hopper,
            paginationGroup, previous, previousGroup, previousIcon,
            next, nextGroup, nextIcon);
    }

    private static void ConfigureWideBottom(
        RectTransform bottom,
        RectTransform view,
        RectTransform zoom,
        float zoomWidth,
        float height)
    {
        HorizontalLayoutGroup layout = bottom.GetComponent<HorizontalLayoutGroup>();
        if (layout == null)
            throw new InvalidOperationException("Wide bottom controls need a HorizontalLayoutGroup.");
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ConfigureFixedLayout(view.gameObject, view.sizeDelta.x, height);
        NormaliseNestedControl(zoom);
        ConfigureFixedLayout(zoom.gameObject, zoomWidth, height);
        GameObject spacer = CreateUiObject(SpacerName, bottom);
        LayoutElement spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.minWidth = 0f;
        spacerLayout.preferredWidth = 0f;
        spacerLayout.flexibleWidth = 1f;
        view.SetSiblingIndex(0);
        spacer.transform.SetSiblingIndex(1);
        zoom.SetSiblingIndex(2);
    }

    private static Button CreatePortraitPageButton(
        string name,
        RectTransform parent,
        Sprite iconSprite,
        float height,
        out Graphic iconGraphic)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(MinimumArrowWidth, height);
        Image panel = buttonObject.AddComponent<Image>();
        panel.sprite = LoadSprite(PanelPath);
        panel.type = Image.Type.Sliced;
        Button button = buttonObject.AddComponent<Button>();
        button.transition = Selectable.Transition.SpriteSwap;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        LayoutElement element = buttonObject.AddComponent<LayoutElement>();
        element.minWidth = MinimumArrowWidth;
        element.preferredWidth = MinimumArrowWidth;
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleWidth = 1f;
        buttonObject.AddComponent<CanvasGroup>();

        GameObject interactionObject = CreateUiObject("Interaction Background", rect);
        RectTransform interactionRect = interactionObject.GetComponent<RectTransform>();
        interactionRect.anchorMin = interactionRect.anchorMax = new Vector2(0.5f, 0.5f);
        interactionRect.sizeDelta = new Vector2(InteractionWidth, Mathf.Max(1f, height - 8f));
        Image interaction = interactionObject.AddComponent<Image>();
        interaction.sprite = LoadSprite(RestingPath);
        interaction.type = Image.Type.Sliced;
        interaction.raycastTarget = false;
        button.targetGraphic = interaction;
        Sprite pressed = LoadSprite(PressedPath);
        SpriteState states = button.spriteState;
        states.highlightedSprite = pressed;
        states.pressedSprite = pressed;
        states.selectedSprite = pressed;
        button.spriteState = states;

        GameObject iconObject = CreateUiObject("Icon", interactionRect);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(34f, 34f);
        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = iconSprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        iconGraphic = icon;
        return button;
    }

    private static void ConfigurePager(
        PortraitTelemetryPager pager,
        PortraitStatRailManager portraitManager,
        WideStatRailManager wideManager,
        HybridHopperClipController hopper,
        CanvasGroup paginationGroup,
        Button previous,
        CanvasGroup previousGroup,
        Graphic previousIcon,
        Button next,
        CanvasGroup nextGroup,
        Graphic nextIcon)
    {
        SerializedObject data = new(pager);
        data.FindProperty("railManager").objectReferenceValue = portraitManager;
        data.FindProperty("wideRailManager").objectReferenceValue = wideManager;
        data.FindProperty("hopperController").objectReferenceValue = hopper;
        data.FindProperty("paginationCanvasGroup").objectReferenceValue = paginationGroup;
        data.FindProperty("previousButton").objectReferenceValue = previous;
        data.FindProperty("previousCanvasGroup").objectReferenceValue = previousGroup;
        data.FindProperty("previousIcon").objectReferenceValue = previousIcon;
        data.FindProperty("nextButton").objectReferenceValue = next;
        data.FindProperty("nextCanvasGroup").objectReferenceValue = nextGroup;
        data.FindProperty("nextIcon").objectReferenceValue = nextIcon;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureZoomAvailability(RectTransform zoom)
    {
        if (zoom.GetComponent<ZoomControlAvailabilityView>() == null)
            zoom.gameObject.AddComponent<ZoomControlAvailabilityView>();
    }

    private static void ConfigureFixedLayout(GameObject target, float width, float height)
    {
        LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        element.enabled = true;
        element.ignoreLayout = false;
        element.minWidth = width;
        element.preferredWidth = width;
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
    }

    private static void NormaliseNestedControl(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static Image FindButtonIcon(Button button) => button
        .GetComponentsInChildren<Image>(true)
        .First(image => image.transform != button.transform);

    private static CanvasGroup GetOrAddCanvasGroup(GameObject target) =>
        target.GetComponent<CanvasGroup>() ?? target.AddComponent<CanvasGroup>();

    private static RectTransform FindDescendant(RectTransform root, string name) => root
        .GetComponentsInChildren<RectTransform>(true).FirstOrDefault(rect => rect.name == name);

    private static RectTransform FindInLayout(string layoutName, string objectName) => UnityEngine.Object
        .FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .FirstOrDefault(rect => rect.name == objectName && HasAncestor(rect, layoutName));

    private static bool HasAncestor(Transform transform, string name)
    {
        for (Transform current = transform; current != null; current = current.parent)
            if (current.name == name)
                return true;
        return false;
    }

    private static void DestroyChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static Sprite LoadSprite(string path) =>
        AssetDatabase.LoadAssetAtPath<Sprite>(path) ??
        throw new InvalidOperationException($"Required sprite is missing: {path}");

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }
}
