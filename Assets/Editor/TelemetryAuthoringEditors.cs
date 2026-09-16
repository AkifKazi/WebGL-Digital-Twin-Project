using UnityEditor;
using UnityEngine;

public abstract class DigitalTwinComponentEditor : Editor
{
    protected void DrawScriptReference()
    {
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
    }

    protected void Draw(string propertyName, string label = null, string tooltip = null)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            return;

        GUIContent content = label == null
            ? null
            : new GUIContent(label, tooltip ?? string.Empty);
        if (content == null)
            EditorGUILayout.PropertyField(property, true);
        else
            EditorGUILayout.PropertyField(property, content, true);
    }
}

[CustomEditor(typeof(PerformanceStatSource))]
[CanEditMultipleObjects]
public sealed class PerformanceStatSourceEditor : DigitalTwinComponentEditor
{
    private bool showLayout;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawScriptReference();

        EditorGUILayout.HelpBox(
            "The GameObject name and UI label should be easy to read. The Telemetry ID is the " +
            "stable integration key used by live JSON data and should only change when the " +
            "external tag mapping changes.",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Identity and Display", EditorStyles.boldLabel);
        Draw("statId", "Telemetry ID", "Stable machine-readable ID used by live-data integration.");
        Draw("metricName", "UI Label", "Short measurement name displayed on telemetry cards.");
        Draw("unit", "Engineering Unit", "Use accurate SI casing, for example kW, kN, mm/s or °C.");
        Draw("valueFormat", "Number Format", "Numeric format such as 0, 0.0 or 0.00.");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Current Reading", EditorStyles.boldLabel);
        Draw("currentValue", "Preview / Simulated Value");
        Draw("dataQuality", "Data Quality", "Good, uncertain, bad or stale source quality.");
        Draw("staleAfterSeconds", "Mark Stale After", "Seconds without a source update before live data is marked stale.");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Alarm State", EditorStyles.boldLabel);
        Draw("useAutomaticThresholds", "Use Automatic Thresholds");

        SerializedProperty automatic = serializedObject.FindProperty("useAutomaticThresholds");
        if (automatic != null && automatic.boolValue)
        {
            Draw("limitMode", "Threshold Mode");
            TelemetryLimitMode mode =
                (TelemetryLimitMode)serializedObject.FindProperty("limitMode").enumValueIndex;

            if (mode == TelemetryLimitMode.LowOnly || mode == TelemetryLimitMode.OutsideRange)
            {
                Draw("warningBelow", "Warning At or Below");
                Draw("criticalBelow", "Critical At or Below");
            }

            if (mode == TelemetryLimitMode.HighOnly || mode == TelemetryLimitMode.OutsideRange)
            {
                Draw("warningAbove", "Warning At or Above");
                Draw("criticalAbove", "Critical At or Above");
            }

            DrawThresholdWarnings(mode);
        }
        else
        {
            Draw("visualState", "Manual Preview State");
        }

        EditorGUILayout.Space();
        showLayout = EditorGUILayout.Foldout(showLayout, "Advanced: Card Placement", true);
        if (showLayout)
        {
            EditorGUILayout.HelpBox(
                "Alarm severity is always prioritised before this manual display priority.",
                MessageType.None);
            Draw("preferredRail", "Preferred Wide Rail");
            Draw("preferredPortraitRail", "Preferred Portrait Rail");
            Draw("displayPriority", "Display Priority", "Higher values are retained first when space is limited.");
            Draw("visible", "Visible");
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawThresholdWarnings(TelemetryLimitMode mode)
    {
        if (serializedObject.isEditingMultipleObjects)
            return;

        if (mode == TelemetryLimitMode.HighOnly || mode == TelemetryLimitMode.OutsideRange)
        {
            float warning = serializedObject.FindProperty("warningAbove").floatValue;
            float critical = serializedObject.FindProperty("criticalAbove").floatValue;
            if (critical < warning)
                EditorGUILayout.HelpBox("Critical-high must not be lower than warning-high.", MessageType.Error);
        }

        if (mode == TelemetryLimitMode.LowOnly || mode == TelemetryLimitMode.OutsideRange)
        {
            float warning = serializedObject.FindProperty("warningBelow").floatValue;
            float critical = serializedObject.FindProperty("criticalBelow").floatValue;
            if (critical > warning)
                EditorGUILayout.HelpBox("Critical-low must not be higher than warning-low.", MessageType.Error);
        }
    }
}

[CustomEditor(typeof(AdaptiveQualityController))]
public sealed class AdaptiveQualityControllerEditor : DigitalTwinComponentEditor
{
    private bool showPresets;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawScriptReference();
        EditorGUILayout.HelpBox(
            "Every device starts on the light preset. On a desktop browser the HD button raises " +
            "it and the choice is remembered in the browser; phones stay light at 30 FPS. The " +
            "preset is never changed automatically.",
            MessageType.Info);

        EditorGUILayout.LabelField("Mobile WebGL", EditorStyles.boldLabel);
        Draw("lockMobileBrowserToLowQuality", "Lock to Light Preset");
        Draw("mobileBrowserTargetFrameRate", "Target Frame Rate");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Behaviour", EditorStyles.boldLabel);
        Draw("rememberChoice", "Remember HD Choice");

        EditorGUILayout.Space();
        showPresets = EditorGUILayout.Foldout(showPresets, "Advanced: Preset Names", true);
        if (showPresets)
        {
            Draw("highQualityName", "HD Preset");
            Draw("lowQualityName", "Light Preset");
            Draw("logDecisions");
        }

        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(HopperProcessSimulator))]
public sealed class HopperProcessSimulatorEditor : DigitalTwinComponentEditor
{
    private bool showConnections;
    private bool showSimulation;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawScriptReference();
        EditorGUILayout.HelpBox(
            "Deterministic placeholder process model used only in Simulation mode. Replace the " +
            "nameplate and nominal values with approved equipment data when available.",
            MessageType.Info);

        EditorGUILayout.LabelField("Equipment Nameplate", EditorStyles.boldLabel);
        Draw("hopperVolumeCubicMetres", "Hopper Volume (m³)");
        Draw("bulkDensityKgPerCubicMetre", "Bulk Density (kg/m³)");
        Draw("motorRatedPowerKw", "Motor Rated Power (kW)");
        Draw("lineVoltage", "Line Voltage (V)");
        Draw("powerFactor", "Power Factor");
        Draw("motorEfficiency", "Motor Efficiency");
        Draw("nominalSpeedRpm", "Nominal Speed (rpm)");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Nominal Process Point", EditorStyles.boldLabel);
        Draw("nominalInletKgPerSecond", "Inlet Rate (kg/s)");
        Draw("nominalDischargeKgPerSecond", "Discharge Rate (kg/s)");
        Draw("initialFillPercent", "Initial Fill Level (%)");
        Draw("ambientTemperatureC", "Ambient Temperature (°C)");

        EditorGUILayout.Space();
        showSimulation = EditorGUILayout.Foldout(showSimulation, "Advanced: Simulation Tuning", true);
        if (showSimulation)
        {
            Draw("updateInterval");
            Draw("processTimeScale");
            Draw("noiseAmount");
            Draw("deterministicSeed");
        }

        showConnections = EditorGUILayout.Foldout(showConnections, "Advanced: Connections", true);
        if (showConnections)
        {
            Draw("registry");
            Draw("particleFlowController");
            Draw("driveParticleControlsFromProcess");
        }

        serializedObject.ApplyModifiedProperties();
    }
}
