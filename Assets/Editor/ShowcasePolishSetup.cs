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
/// The showcase polish pass: the HD control beside the zoom buttons, the X-Ray
/// segment's own icon, both rotors answering the rotor sensors, and the
/// hand-placed rotor and vibration components folded into the list-driven ones.
/// Telemetry anchors are left where they have been placed. Safe to run again.
/// </summary>
public static class ShowcasePolishSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string IdleSpritePath = "Assets/UI/Sprites/Solid UI Fill.png";
    private const string PressedSpritePath = "Assets/UI/Sprites/Compact Button Background.png";
    private const string HoverSpritePath = "Assets/UI/Sprites/Button Hover Background.png";
    private const string PanelSpritePath = "Assets/UI/Sprites/Panel Background.png";
    private const string HdIconPath = "Assets/UI/Sprites/HD Icon.png";
    private const string XRayIconPath = "Assets/UI/Sprites/XRay Icon.png";
    private const string ControlName = "Graphics Quality Control";

    // Matches the zoom controls: a 68 tall panel, 4 inside it, a 48 px icon. The
    // bottom row does not size its children's height, so the panel sets its own.
    private const float ControlHeight = 68f;
    private const float ControlWidth = 104f;
    private const float IconSize = 48f;

    // White at rest like the zoom icons, cyan while HD is on.
    private static readonly Color IconOnColor = new(0.212f, 0.929f, 1f, 1f);
    private static readonly Color IconOffColor = Color.white;

    private static readonly string[] RotorSensorIds =
    {
        "MIX-01.ROTOR.SPEED", "MIX-01.ROTOR.TORQUE", "MIX-01.ROTOR.IMBALANCE"
    };

    [MenuItem("Tools/Digital Twin/Set Up Showcase Polish")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int controls = ConfigureQualityControls();
        int icons = ConfigureXRayIcons();
        int groups = ShareRotorSensors();
        int spinners = MigrateSpinners();
        int vibrators = MigrateVibrators();
        bool framer = ConfigureAnchorFramer();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"SHOWCASE_POLISH controls={controls} xrayIcons={icons} " +
                  $"rotorGroups={groups} spinners={spinners} vibrators={vibrators} framer={framer}");
    }

    // -----------------------------------------------------------------------
    // Bottom controls
    // -----------------------------------------------------------------------

    /// <summary>Adds the HD control to each layout's bottom row, beside the zoom buttons.</summary>
    private static int ConfigureQualityControls()
    {
        Sprite idle = Load<Sprite>(IdleSpritePath);
        Sprite pressed = Load<Sprite>(PressedSpritePath);
        Sprite hover = Load<Sprite>(HoverSpritePath);
        Sprite panel = Load<Sprite>(PanelSpritePath);
        Sprite hdIcon = Load<Sprite>(HdIconPath);
        AdaptiveQualityController quality = UnityEngine.Object.FindAnyObjectByType<AdaptiveQualityController>(
            FindObjectsInactive.Include);
        int built = 0;

        foreach (Transform zoom in FindAll("Camera Zoom Controls"))
        {
            Transform row = zoom.parent;
            if (row == null)
                continue;

            Transform existing = row.Find(ControlName);
            RectTransform control = existing as RectTransform ?? CreateUIObject(ControlName, row);
            // Immediately before the zoom buttons, one row gap away, which is the
            // screen padding. Measured after any earlier move, so running this
            // again leaves the control where it already is.
            int target = zoom.GetSiblingIndex();
            if (control.GetSiblingIndex() < target)
                target--;
            control.SetSiblingIndex(target);

            Image background = Ensure<Image>(control.gameObject);
            background.sprite = panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            HorizontalLayoutGroup row_ = Ensure<HorizontalLayoutGroup>(control.gameObject);
            row_.padding = new RectOffset(4, 4, 4, 4);
            row_.spacing = 0f;
            row_.childAlignment = TextAnchor.MiddleCenter;
            row_.childControlWidth = true;
            row_.childControlHeight = true;
            row_.childForceExpandWidth = true;
            row_.childForceExpandHeight = true;

            LayoutElement size = Ensure<LayoutElement>(control.gameObject);
            size.minWidth = size.preferredWidth = ControlWidth;
            size.minHeight = size.preferredHeight = ControlHeight;
            size.flexibleWidth = 0f;
            size.flexibleHeight = 0f;
            control.sizeDelta = new Vector2(ControlWidth, ControlHeight);

            RectTransform buttonRect = control.Find("HD Button") as RectTransform
                                       ?? CreateUIObject("HD Button", control);
            Image face = Ensure<Image>(buttonRect.gameObject);
            face.sprite = idle;
            face.type = Image.Type.Sliced;
            face.color = Color.white;
            face.raycastTarget = true;

            Button button = Ensure<Button>(buttonRect.gameObject);
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { pressedSprite = pressed };

            RectTransform iconRect = buttonRect.Find("Icon") as RectTransform
                                     ?? CreateUIObject("Icon", buttonRect);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            iconRect.anchoredPosition = Vector2.zero;
            Image icon = Ensure<Image>(iconRect.gameObject);
            icon.sprite = hdIcon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            // The same three looks as every other control: idle, hover, pressed.
            SerializedObject hoverState = new(Ensure<UIHoverSprite>(buttonRect.gameObject));
            hoverState.FindProperty("target").objectReferenceValue = face;
            hoverState.FindProperty("hoverSprite").objectReferenceValue = hover;
            hoverState.FindProperty("idleSprite").objectReferenceValue = idle;
            hoverState.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject toggle = new(Ensure<QualityToggleControl>(control.gameObject));
            toggle.FindProperty("quality").objectReferenceValue = quality;
            toggle.FindProperty("button").objectReferenceValue = button;
            toggle.FindProperty("background").objectReferenceValue = face;
            toggle.FindProperty("icon").objectReferenceValue = icon;
            toggle.FindProperty("idleSprite").objectReferenceValue = idle;
            toggle.FindProperty("selectedSprite").objectReferenceValue = pressed;
            toggle.FindProperty("controlRoot").objectReferenceValue = control.gameObject;
            toggle.FindProperty("onIconColor").colorValue = IconOnColor;
            toggle.FindProperty("offIconColor").colorValue = IconOffColor;
            toggle.ApplyModifiedPropertiesWithoutUndo();

            built++;
            Debug.Log($"SHOWCASE_POLISH hd control under '{GetPath(row)}'");
        }

        return built;
    }

    /// <summary>The X-Ray segment had the cross-section icon; it has its own now.</summary>
    private static int ConfigureXRayIcons()
    {
        Sprite xray = Load<Sprite>(XRayIconPath);
        int changed = 0;
        foreach (Transform button in FindAll("X-Ray Button"))
        {
            Transform icon = button.Find("Icon");
            if (icon == null || !icon.TryGetComponent(out Image image) || image.sprite == xray)
                continue;
            image.sprite = xray;
            EditorUtility.SetDirty(image);
            changed++;
        }

        return changed;
    }

    // -----------------------------------------------------------------------
    // Rotor parts
    // -----------------------------------------------------------------------

    /// <summary>
    /// A rotor reading describes both rotors turning together, so both light up.
    /// Before this, only the upper rotor did.
    /// </summary>
    private static int ShareRotorSensors()
    {
        int changed = 0;
        foreach (MachinePartGroup group in UnityEngine.Object.FindObjectsByType<MachinePartGroup>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (group.name != "Lower Rotor")
                continue;

            SerializedObject data = new(group);
            SerializedProperty ids = data.FindProperty("sensorIds");
            ids.arraySize = RotorSensorIds.Length;
            for (int i = 0; i < RotorSensorIds.Length; i++)
                ids.GetArrayElementAtIndex(i).stringValue = RotorSensorIds[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            changed++;
        }

        return changed;
    }

    // -----------------------------------------------------------------------
    // Motion components
    // -----------------------------------------------------------------------

    /// <summary>Folds every hand-placed rotor spinner into one list-driven component.</summary>
    private static int MigrateSpinners()
    {
        DualRotorSpin[] old = UnityEngine.Object.FindObjectsByType<DualRotorSpin>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (old.Length == 0)
            return 0;

        Transform host = FindFirst("Vibratory Drive") ?? old[0].transform;
        PartSpinner spinner = Ensure<PartSpinner>(host.gameObject);
        SerializedObject data = new(spinner);
        SerializedProperty parts = data.FindProperty("parts");
        parts.arraySize = 0;
        float rpm = 0f;

        foreach (DualRotorSpin spin in old)
        {
            rpm = Mathf.Max(rpm, spin.rpm);
            AddSpinPart(parts, spin.rotor01, spin.rotor01Axis, false);
            AddSpinPart(parts, spin.rotor02, spin.rotor02Axis, spin.oppositeDirections);
        }

        data.FindProperty("revolutionsPerMinute").floatValue = rpm;
        data.ApplyModifiedPropertiesWithoutUndo();

        foreach (DualRotorSpin spin in old)
            UnityEngine.Object.DestroyImmediate(spin);

        Debug.Log($"SHOWCASE_POLISH spinner on '{GetPath(host)}' parts={parts.arraySize} rpm={rpm}");
        return parts.arraySize;
    }

    private static void AddSpinPart(SerializedProperty parts, Transform target, Vector3 axis, bool reverse)
    {
        if (target == null)
            return;
        int index = parts.arraySize;
        parts.arraySize = index + 1;
        SerializedProperty entry = parts.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("target").objectReferenceValue = target;
        entry.FindPropertyRelative("axis").vector3Value = axis.sqrMagnitude < 0.0001f ? Vector3.forward : axis;
        entry.FindPropertyRelative("speedShare").floatValue = 1f;
        entry.FindPropertyRelative("reverse").boolValue = reverse;
    }

    /// <summary>Folds the five identical shake components into one.</summary>
    private static int MigrateVibrators()
    {
        SeparatorVibration[] old = UnityEngine.Object.FindObjectsByType<SeparatorVibration>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (old.Length == 0)
            return 0;

        Transform host = FindFirst("Hopper Assembly") ?? old[0].transform;
        PartVibrator vibrator = Ensure<PartVibrator>(host.gameObject);
        SerializedObject data = new(vibrator);
        SerializedProperty parts = data.FindProperty("parts");
        parts.arraySize = 0;
        float speed = 0f;

        foreach (SeparatorVibration shake in old)
        {
            speed = Mathf.Max(speed, shake.frequency);
            int index = parts.arraySize;
            parts.arraySize = index + 1;
            SerializedProperty entry = parts.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("target").objectReferenceValue = shake.transform;
            entry.FindPropertyRelative("amplitude").vector3Value =
                new Vector3(shake.amplitudeX, shake.amplitudeY, 0f);
            entry.FindPropertyRelative("rotationAmplitude").vector3Value =
                new Vector3(0f, 0f, shake.rotationAmplitude);
            entry.FindPropertyRelative("speedShare").floatValue = 1f;
            entry.FindPropertyRelative("phase").floatValue = 0f;
        }

        data.FindProperty("angularSpeed").floatValue = speed;
        data.ApplyModifiedPropertiesWithoutUndo();

        foreach (SeparatorVibration shake in old)
            UnityEngine.Object.DestroyImmediate(shake);

        Debug.Log($"SHOWCASE_POLISH vibrator on '{GetPath(host)}' parts={parts.arraySize} speed={speed}");
        return parts.arraySize;
    }

    /// <summary>Clicking a card whose sensor is off screen pulls the camera back.</summary>
    private static bool ConfigureAnchorFramer()
    {
        OrbitCameraController camera = UnityEngine.Object.FindAnyObjectByType<OrbitCameraController>(
            FindObjectsInactive.Include);
        if (camera == null)
            return false;

        TelemetryAnchorFramer framer = Ensure<TelemetryAnchorFramer>(camera.gameObject);
        SerializedObject data = new(framer);
        data.FindProperty("orbitCamera").objectReferenceValue = camera;
        data.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static T Load<T>(string path) where T : UnityEngine.Object =>
        AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException($"Missing asset: {path}");

    private static T Ensure<T>(GameObject target) where T : Component =>
        target.TryGetComponent(out T component) ? component : target.AddComponent<T>();

    private static IEnumerable<Transform> FindAll(string objectName) =>
        UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(transform => transform.name == objectName);

    private static Transform FindFirst(string objectName) => FindAll(objectName).FirstOrDefault();

    private static RectTransform CreateUIObject(string name, Transform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return (RectTransform)gameObject.transform;
    }

    private static string GetPath(Transform transform) =>
        transform.parent == null ? transform.name : GetPath(transform.parent) + "/" + transform.name;
}
