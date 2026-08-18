using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(OrbitCameraController))]
public sealed class OrbitCameraControllerEditor : Editor
{
    private bool showAdvanced;

    private static readonly string[] SimpleProperties =
    {
        "target",
        "targetOffset",
        "currentDistance",
        "minDistance",
        "maxDistance",
        "enableMouseDrag",
        "enableMouseWheelZoom",
        "enableTouchDrag",
        "enablePinchZoom",
        "pinchZoomSensitivity"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        EditorGUILayout.HelpBox(
            "Everyday camera controls are shown below. Pinch zoom is enabled for " +
            "mobile; intro animation and sensitivity calibration remain under Advanced.",
            MessageType.Info);

        EditorGUILayout.LabelField("Focus", EditorStyles.boldLabel);
        Draw("target");
        Draw("targetOffset");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Zoom Range", EditorStyles.boldLabel);
        Draw("currentDistance");
        Draw("minDistance");
        Draw("maxDistance");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Desktop and Mobile Input", EditorStyles.boldLabel);
        Draw("enableMouseDrag");
        Draw("enableMouseWheelZoom");
        Draw("enableTouchDrag");
        Draw("enablePinchZoom");
        Draw("pinchZoomSensitivity");

        EditorGUILayout.Space();
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced Camera Tuning", true);
        if (showAdvanced)
        {
            string[] exclusions = new string[SimpleProperties.Length + 1];
            exclusions[0] = "m_Script";
            for (int i = 0; i < SimpleProperties.Length; i++)
                exclusions[i + 1] = SimpleProperties[i];

            DrawPropertiesExcluding(serializedObject, exclusions);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void Draw(string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            EditorGUILayout.PropertyField(property);
    }
}
