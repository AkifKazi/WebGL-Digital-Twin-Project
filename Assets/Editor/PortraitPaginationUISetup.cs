using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PortraitPaginationUISetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string RowName = "Telemetry Pagination and Zoom";
    private const string BackgroundPath = "Assets/UI/Sprites/Panel Background.png";
    private const string PressedBackgroundPath = "Assets/UI/Sprites/Compact Button Background.png";
    private const string PreviousIconPath = "Assets/UI/Sprites/arrow-left.png";
    private const string NextIconPath = "Assets/UI/Sprites/arrow-right.png";

    [MenuItem("Tools/Digital Twin/Set Up Portrait Pagination")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PortraitStatRailManager manager = UnityEngine.Object.FindFirstObjectByType<PortraitStatRailManager>(
            FindObjectsInactive.Include);
        if (manager == null)
            throw new InvalidOperationException("Portrait telemetry rail manager was not found.");

        RectTransform bottomControls = UnityEngine.Object
            .FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(rect => rect.name == "Bottom controls" && HasAncestor(rect, "Portrait layout"));
        if (bottomControls == null)
            throw new InvalidOperationException("Portrait bottom controls were not found.");

        RectTransform zoomControls = bottomControls
            .GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name == "Camera Zoom Controls");
        if (zoomControls == null)
            throw new InvalidOperationException("Portrait camera zoom controls were not found.");

        Transform existing = bottomControls.Find(RowName);
        if (existing != null)
        {
            zoomControls.SetParent(bottomControls, false);
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        GameObject rowObject = CreateUiObject(RowName, bottomControls);
        RectTransform row = rowObject.GetComponent<RectTransform>();
        row.sizeDelta = new Vector2(524.6f, 68f);
        row.localScale = Vector3.one * 1.5f;
        row.SetSiblingIndex(0);

        LayoutElement rowLayout = rowObject.AddComponent<LayoutElement>();
        rowLayout.minWidth = 524.6f;
        rowLayout.preferredWidth = 524.6f;
        rowLayout.minHeight = 68f;
        rowLayout.preferredHeight = 68f;
        rowLayout.flexibleWidth = 0f;
        rowLayout.flexibleHeight = 0f;

        HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childScaleWidth = false;
        layout.childScaleHeight = false;

        Button previousButton = CreatePageButton(
            "Previous Telemetry Page",
            row,
            AssetDatabase.LoadAssetAtPath<Sprite>(PreviousIconPath));

        zoomControls.SetParent(row, false);
        zoomControls.SetSiblingIndex(1);
        zoomControls.anchorMin = new Vector2(0.5f, 0.5f);
        zoomControls.anchorMax = new Vector2(0.5f, 0.5f);
        zoomControls.pivot = new Vector2(0.5f, 0.5f);
        zoomControls.anchoredPosition = Vector2.zero;
        zoomControls.localScale = Vector3.one;

        LayoutElement zoomLayout = zoomControls.GetComponent<LayoutElement>();
        if (zoomLayout == null)
            zoomLayout = zoomControls.gameObject.AddComponent<LayoutElement>();
        zoomLayout.minWidth = 198f;
        zoomLayout.preferredWidth = 198f;
        zoomLayout.minHeight = 68f;
        zoomLayout.preferredHeight = 68f;
        zoomLayout.flexibleWidth = 0f;
        zoomLayout.flexibleHeight = 0f;

        Button nextButton = CreatePageButton(
            "Next Telemetry Page",
            row,
            AssetDatabase.LoadAssetAtPath<Sprite>(NextIconPath));

        PortraitTelemetryPager pager = rowObject.AddComponent<PortraitTelemetryPager>();
        SerializedObject pagerData = new(pager);
        pagerData.FindProperty("railManager").objectReferenceValue = manager;
        pagerData.FindProperty("previousButton").objectReferenceValue = previousButton;
        pagerData.FindProperty("previousCanvasGroup").objectReferenceValue =
            previousButton.GetComponent<CanvasGroup>();
        pagerData.FindProperty("nextButton").objectReferenceValue = nextButton;
        pagerData.FindProperty("nextCanvasGroup").objectReferenceValue =
            nextButton.GetComponent<CanvasGroup>();
        pagerData.FindProperty("availabilityFadeDuration").floatValue = 0.3f;
        pagerData.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Portrait telemetry pagination controls configured successfully.");
    }

    private static Button CreatePageButton(string name, RectTransform parent, Sprite iconSprite)
    {
        Sprite backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        Sprite pressedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PressedBackgroundPath);
        if (backgroundSprite == null || pressedSprite == null || iconSprite == null)
            throw new InvalidOperationException($"Pagination sprite asset is missing for '{name}'.");

        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(153.3f, 68f);

        Image background = buttonObject.AddComponent<Image>();
        background.sprite = backgroundSprite;
        background.type = Image.Type.Sliced;
        background.color = Color.white;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.SpriteSwap;
        SpriteState sprites = button.spriteState;
        sprites.highlightedSprite = pressedSprite;
        sprites.pressedSprite = pressedSprite;
        sprites.selectedSprite = pressedSprite;
        button.spriteState = sprites;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        LayoutElement element = buttonObject.AddComponent<LayoutElement>();
        element.minWidth = 153.3f;
        element.preferredWidth = 153.3f;
        element.minHeight = 68f;
        element.preferredHeight = 68f;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        buttonObject.AddComponent<CanvasGroup>();

        GameObject iconObject = CreateUiObject("Icon", rect);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(34f, 34f);

        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = iconSprite;
        icon.color = Color.white;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        return button;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static bool HasAncestor(Transform transform, string ancestorName)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.name == ancestorName)
                return true;
        }
        return false;
    }
}
