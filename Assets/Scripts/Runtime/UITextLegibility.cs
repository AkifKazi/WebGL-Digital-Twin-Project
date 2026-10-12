using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Gives on-screen text the theme's shadowed font material (a sharp, dark
/// underlay) so labels read over a bright scene as well as a dark one. Every
/// text is covered, including those views build at run time: TextMesh Pro
/// reports each text it lays out, and a text still on its font's default
/// material takes the theme's preset for that font. Texts given a material of
/// their own are left alone. All texts of a font share one preset, so they
/// still batch.
/// </summary>
public static class UITextLegibility
{
    private static readonly HashSet<TextMeshProUGUI> Pending = new();
    private static readonly List<TextMeshProUGUI> Applying = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
        Pending.Clear();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(HandleTextChanged);
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(HandleTextChanged);
        Canvas.preWillRenderCanvases -= ApplyPending;
        Canvas.preWillRenderCanvases += ApplyPending;
    }

    // Raised while the canvas rebuilds its graphics, where a material change
    // cannot be made; it is applied before the next rebuild instead.
    private static void HandleTextChanged(Object changed)
    {
        if (changed is TextMeshProUGUI text && NeedsChange(text))
            Pending.Add(text);
    }

    private static void ApplyPending()
    {
        if (Pending.Count == 0)
            return;
        Applying.Clear();
        Applying.AddRange(Pending);
        Pending.Clear();
        foreach (TextMeshProUGUI text in Applying)
        {
            if (text != null && NeedsChange(text))
                text.fontSharedMaterial = Wanted(text);
        }
        Applying.Clear();
    }

    private static bool NeedsChange(TextMeshProUGUI text)
    {
        Material wanted = Wanted(text);
        return wanted != null && text.fontSharedMaterial != wanted;
    }

    /// <summary>The material the text should show, or null to leave it.</summary>
    private static Material Wanted(TextMeshProUGUI text)
    {
        UITheme theme = UITheme.Active;
        TMP_FontAsset font = text.font;
        if (theme == null || font == null)
            return null;
        Material legible = theme.LegibleMaterial(font);
        if (legible == null)
            return null;
        Material current = text.fontSharedMaterial;
        if (theme.textShadow)
            return current == font.material ? legible : null;
        return current == legible ? font.material : null;
    }
}
