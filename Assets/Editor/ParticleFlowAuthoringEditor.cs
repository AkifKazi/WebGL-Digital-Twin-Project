using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ParticleFlowController))]
[CanEditMultipleObjects]
public sealed class ParticleFlowControllerEditor : Editor
{
    private bool showTargets;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Normal operation only needs Flow Speed and Quantity. The individual " +
            "particle path components contain calibrated geometry and should normally " +
            "be left unchanged.",
            MessageType.Info);

        SerializedProperty flowSpeed = serializedObject.FindProperty("flowSpeed");
        SerializedProperty quantity = serializedObject.FindProperty("quantity");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.Slider(flowSpeed, 0f, 3f, new GUIContent(
            "Flow Speed",
            "Scales the complete physical timeline without changing the path."));
        EditorGUILayout.Slider(quantity, 0f, 3f, new GUIContent(
            "Particle Quantity",
            "Scales emission without changing rock size or trajectory."));

        if (GUILayout.Button("Restore Nominal Flow (1×)"))
        {
            flowSpeed.floatValue = 1f;
            quantity.floatValue = 1f;
            GUI.changed = true;
        }

        bool controlsChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();
        showTargets = EditorGUILayout.Foldout(
            showTargets,
            "Advanced: Managed Targets",
            true);

        if (showTargets)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("discoverInChildren"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("particleSystems"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rockFlows"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("filteredFlows"), true);

            if (!serializedObject.isEditingMultipleObjects &&
                GUILayout.Button("Refresh Child Targets"))
            {
                ParticleFlowController controller = (ParticleFlowController)target;
                Undo.RecordObject(controller, "Refresh Particle Targets");
                controller.RefreshTargets();
                EditorUtility.SetDirty(controller);
            }
        }

        serializedObject.ApplyModifiedProperties();

        if (controlsChanged && Application.isPlaying)
        {
            foreach (Object item in targets)
                ((ParticleFlowController)item).Apply();
        }
    }
}

public abstract class CalibratedParticlePathEditor : Editor
{
    private bool showAdvanced;

    protected abstract string[] ReferenceProperties { get; }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        EditorGUILayout.HelpBox(
            "Calibrated particle path. Use Particle Flow Controller for everyday " +
            "speed and quantity changes.",
            MessageType.Info);

        EditorGUILayout.LabelField("Path References", EditorStyles.boldLabel);
        foreach (string propertyName in ReferenceProperties)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property);
        }

        EditorGUILayout.Space();
        showAdvanced = EditorGUILayout.Foldout(
            showAdvanced,
            "Advanced Physical Tuning",
            true);

        if (showAdvanced)
        {
            EditorGUILayout.HelpBox(
                "Changing these values can alter the approved motion and physical " +
                "calibration. Record the previous value before tuning.",
                MessageType.Warning);

            string[] exclusions = new string[ReferenceProperties.Length + 1];
            exclusions[0] = "m_Script";
            for (int i = 0; i < ReferenceProperties.Length; i++)
                exclusions[i + 1] = ReferenceProperties[i];

            DrawPropertiesExcluding(serializedObject, exclusions);
        }

        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(ControlledRockFlow))]
[CanEditMultipleObjects]
public sealed class ControlledRockFlowEditor : CalibratedParticlePathEditor
{
    private static readonly string[] References =
    {
        "centerPoint",
        "outletPoint"
    };

    protected override string[] ReferenceProperties => References;
}

[CustomEditor(typeof(FilteredParticleFlow))]
[CanEditMultipleObjects]
public sealed class FilteredParticleFlowEditor : CalibratedParticlePathEditor
{
    private static readonly string[] References =
    {
        "centerPoint",
        "slopeStartLeftPoint",
        "slopeStartRightPoint",
        "slopeEndLeftPoint",
        "slopeEndRightPoint",
        "rightOutletPoint"
    };

    protected override string[] ReferenceProperties => References;
}
