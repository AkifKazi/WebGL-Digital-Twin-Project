using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a <see cref="UIColor"/> as a token picker: the theme's colour tokens
/// in a menu, with a swatch and an alpha slider, or Custom with a colour field.
/// </summary>
[CustomPropertyDrawer(typeof(UIColor))]
public sealed class UIColorDrawer : PropertyDrawer
{
    private const string CustomLabel = "Custom";

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty token = property.FindPropertyRelative("token");
        SerializedProperty alpha = property.FindPropertyRelative("alpha");
        SerializedProperty custom = property.FindPropertyRelative("custom");

        EditorGUI.BeginProperty(position, label, property);
        Rect field = EditorGUI.PrefixLabel(position, label);
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        UIDesignTokens tokens = UITheme.Active != null ? UITheme.Active.tokens : null;
        List<string> names = tokens != null
            ? tokens.colors.Select(c => c.name).Where(n => !string.IsNullOrEmpty(n)).ToList()
            : new List<string>();
        names.Insert(0, CustomLabel);

        bool isCustom = string.IsNullOrEmpty(token.stringValue);
        int index = isCustom ? 0 : names.IndexOf(token.stringValue);
        if (index < 0)
        {
            // A token that no longer exists stays visible so it can be fixed.
            names.Add(token.stringValue + " (missing)");
            index = names.Count - 1;
        }

        float popupWidth = Mathf.Min(150f, field.width * 0.45f);
        Rect popup = new(field.x, field.y, popupWidth, EditorGUIUtility.singleLineHeight);
        Rect rest = new(popup.xMax + 4f, field.y, field.width - popupWidth - 4f, EditorGUIUtility.singleLineHeight);

        int chosen = EditorGUI.Popup(popup, index, names.ToArray());
        if (chosen != index)
        {
            token.stringValue = chosen == 0 ? string.Empty : names[chosen];
            if (chosen != 0 && alpha.floatValue <= 0f)
                alpha.floatValue = 1f;
            isCustom = chosen == 0;
        }

        if (isCustom)
        {
            custom.colorValue = EditorGUI.ColorField(rest, GUIContent.none, custom.colorValue, true, true, false);
        }
        else
        {
            Color swatch = Color.magenta;
            tokens?.TryGetColor(token.stringValue, out swatch);
            swatch.a *= alpha.floatValue;
            Rect swatchRect = new(rest.x, rest.y + 2f, 18f, rest.height - 4f);
            EditorGUI.DrawRect(swatchRect, Color.black);
            EditorGUI.DrawRect(new Rect(swatchRect.x + 1, swatchRect.y + 1, swatchRect.width - 2, swatchRect.height - 2), swatch);
            Rect slider = new(swatchRect.xMax + 4f, rest.y, rest.width - 22f, rest.height);
            alpha.floatValue = EditorGUI.Slider(slider, alpha.floatValue, 0f, 1f);
        }

        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
}
