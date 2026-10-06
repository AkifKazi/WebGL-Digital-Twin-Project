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
/// The showcase polish pass: the SD / HD control beside the zoom buttons, the X-Ray
/// segment's own icon, both rotors answering the rotor sensors, and the
/// hand-placed rotor and vibration components folded into the list-driven ones.
/// Telemetry anchors are left where they have been placed. Safe to run again.
/// </summary>
public static class ShowcasePolishSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string XRayIconPath = "Assets/UI/Sprites/XRay Icon.png";
    private const string ControlName = "Graphics Quality Control";

    // As tall as the zoom panel; each segment wide enough for two letters at
    // the view-mode control's type size.
    private const float ControlHeight = 68f;
    private const float QualitySegmentWidth = 96f;

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

    /// <summary>
    /// Adds the SD / HD control to each layout's bottom row, beside the zoom
    /// buttons. It is drawn like the view-mode and Telemetry / Service controls:
    /// the same container, and segments cloned from the view-mode control with
    /// the icon removed. An older single HD button is replaced.
    /// </summary>
    private static int ConfigureQualityControls()
    {
        AdaptiveQualityController quality = UnityEngine.Object.FindAnyObjectByType<AdaptiveQualityController>(
            FindObjectsInactive.Include);
        int built = 0;

        // Listed up front: rebuilding destroys transforms the lazy search would visit.
        foreach (Transform zoom in FindAll("Camera Zoom Controls").ToList())
        {
            Transform row = zoom.parent;
            ViewModeSegmentedControl viewMode = FindViewModeControl(zoom);
            if (row == null || viewMode == null)
                continue;

            // Checked before the old control is removed, so a renamed or
            // restyled view-mode control leaves the current one in place.
            Transform source = viewMode.transform;
            HorizontalLayoutGroup sourceStack = source.GetComponent<HorizontalLayoutGroup>();
            Image sourceImage = source.GetComponent<Image>();
            Transform template = source.Find("Full Model Button");
            if (sourceStack == null || sourceImage == null || template == null ||
                template.GetComponent<Button>() == null ||
                template.GetComponent<LayoutElement>() == null ||
                template.GetComponentInChildren<TMP_Text>(true) == null)
            {
                Debug.LogWarning(
                    $"SHOWCASE_POLISH SD/HD control under '{GetPath(row)}' skipped: the view-mode control " +
                    "needs a HorizontalLayoutGroup, an Image and a 'Full Model Button' with a Button, " +
                    "a LayoutElement and a label.");
                continue;
            }

            // Rebuilt from scratch each run, at the place it already holds:
            // immediately before the zoom buttons, one row gap away.
            Transform existing = row.Find(ControlName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            RectTransform control = CreateUIObject(ControlName, row);
            control.SetSiblingIndex(zoom.GetSiblingIndex());

            int padding = sourceStack.padding.horizontal;
            float width = QualitySegmentWidth * 2f + sourceStack.spacing + padding;

            CopyImage(sourceImage, Ensure<Image>(control.gameObject));
            HorizontalLayoutGroup stack = Ensure<HorizontalLayoutGroup>(control.gameObject);
            stack.padding = new RectOffset(sourceStack.padding.left, sourceStack.padding.right,
                sourceStack.padding.top, sourceStack.padding.bottom);
            stack.spacing = sourceStack.spacing;
            stack.childAlignment = sourceStack.childAlignment;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = true;

            // The bottom row does not size its children's height, so the control
            // sets its own: as tall as the zoom panel.
            LayoutElement size = Ensure<LayoutElement>(control.gameObject);
            size.minWidth = size.preferredWidth = width;
            size.minHeight = size.preferredHeight = ControlHeight;
            size.flexibleWidth = 0f;
            size.flexibleHeight = 0f;
            control.sizeDelta = new Vector2(width, ControlHeight);

            SerializedObject sourceData = new(viewMode);
            Sprite selected = (Sprite)sourceData.FindProperty("selectedSprite").objectReferenceValue;
            Sprite idle = (Sprite)sourceData.FindProperty("inactiveSprite").objectReferenceValue;
            Button sd = CloneSegment(template, control, "SD Button", "SD", idle);
            Button hd = CloneSegment(template, control, "HD Button", "HD", idle);

            SerializedObject toggle = new(Ensure<QualityToggleControl>(control.gameObject));
            toggle.FindProperty("quality").objectReferenceValue = quality;
            toggle.FindProperty("sdButton").objectReferenceValue = sd;
            toggle.FindProperty("hdButton").objectReferenceValue = hd;
            toggle.FindProperty("controlRoot").objectReferenceValue = control.gameObject;
            toggle.FindProperty("selectedSprite").objectReferenceValue = selected;
            toggle.FindProperty("idleSprite").objectReferenceValue = idle;
            toggle.FindProperty("selectedTextColor").colorValue = sourceData.FindProperty("selectedTextColor").colorValue;
            toggle.FindProperty("idleTextColor").colorValue = sourceData.FindProperty("inactiveTextColor").colorValue;
            toggle.ApplyModifiedPropertiesWithoutUndo();

            built++;
            Debug.Log($"SHOWCASE_POLISH SD/HD control under '{GetPath(row)}' width={width}");
        }

        return built;
    }

    /// <summary>The view-mode control in the same layout as these zoom buttons.</summary>
    private static ViewModeSegmentedControl FindViewModeControl(Transform zoom)
    {
        for (Transform level = zoom.parent; level != null; level = level.parent)
        {
            ViewModeSegmentedControl found = level.GetComponentInChildren<ViewModeSegmentedControl>(true);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>A text-only segment cloned from the view-mode control: same sprites, hover and type.</summary>
    private static Button CloneSegment(Transform template, Transform parent, string name, string text, Sprite idle)
    {
        GameObject segment = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        segment.name = name;

        Transform icon = segment.transform.Find("Icon");
        if (icon != null)
            UnityEngine.Object.DestroyImmediate(icon.gameObject);

        // Short copy, the same in every language, so it is not bound to the text table.
        TMP_Text label = segment.GetComponentInChildren<TMP_Text>(true);
        if (label.TryGetComponent(out UITextBinding binding))
            UnityEngine.Object.DestroyImmediate(binding);
        label.text = text;

        LayoutElement size = segment.GetComponent<LayoutElement>();
        size.minWidth = 0f;
        size.preferredWidth = QualitySegmentWidth;

        Button button = segment.GetComponent<Button>();
        // Drop anything wired to the view-mode control.
        button.onClick = new Button.ButtonClickedEvent();
        if (button.targetGraphic is Image face)
            face.sprite = idle;

        // The hover sprite only ever replaces the idle one, whichever segment
        // was selected when the template was cloned.
        if (segment.TryGetComponent(out UIHoverSprite hover))
        {
            SerializedObject hoverState = new(hover);
            hoverState.FindProperty("idleSprite").objectReferenceValue = idle;
            hoverState.ApplyModifiedPropertiesWithoutUndo();
        }

        return button;
    }

    private static void CopyImage(Image from, Image to)
    {
        to.sprite = from.sprite;
        to.type = from.type;
        to.color = from.color;
        to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
        to.fillCenter = from.fillCenter;
        to.material = from.material;
        to.raycastTarget = from.raycastTarget;
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
                     FindObjectsInactive.Include))
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
            FindObjectsInactive.Include);
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
            FindObjectsInactive.Include);
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
        UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
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
