using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outlines the parts behind the focused telemetry card, in every view. A card
/// can be read from several parts - motor speed from the rotor core and the
/// drive shaft - and all of them are outlined together. The colour follows the
/// card's own alarm state, using the same palette as the cards: blue within
/// limits, amber on warning, red when critical.
///
/// Follows the card's own focus signal, so mouse hover and the timed touch
/// focus on mobile behave identically here.
/// </summary>
[DisallowMultipleComponent]
public sealed class MachineSelectionHighlighter : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("Parts that can be outlined. Left empty, they are found under the equipment roots.")]
    [SerializeField] private MachinePartGroup[] partGroups = System.Array.Empty<MachinePartGroup>();

    [Tooltip("Roots searched for parts when the list above is empty.")]
    [SerializeField] private GameObject[] equipmentRoots = System.Array.Empty<GameObject>();

    [Header("Colours")]
    // The cards' own palette, so the outline reads as part of the same UI.
    [Tooltip("Outline colour while the focused card is within limits. The telemetry cards' teal.")]
    [SerializeField] private Color normalColor = new(0.08f, 0.90f, 1f, 1f);

    [Tooltip("Outline colour while the focused card is in warning.")]
    [SerializeField] private Color warningColor = new(1f, 0.70f, 0.10f, 1f);

    [Tooltip("Outline colour while the focused card is critical.")]
    [SerializeField] private Color criticalColor = new(1f, 0.22f, 0.20f, 1f);

    [Tooltip("Outline colour while the focused card's reading is unavailable or stale.")]
    [SerializeField] private Color unavailableColor = new(0.45f, 0.50f, 0.55f, 1f);

    [Header("Timing")]
    [Tooltip("Seconds for the outline to appear.")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.12f;

    [Tooltip("Seconds for the outline to disappear.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.2f;

    private readonly List<MachinePartGroup> groups = new();
    private readonly List<Renderer> outlinedRenderers = new();

    // Target is the sensor focus asks for; drawn is the one the outline
    // currently shows, kept while it fades out so the colour does not change
    // mid-fade.
    private PerformanceStatSource target;
    private PerformanceStatSource drawn;
    private float strength;

    private void Awake()
    {
        ResolveGroups();
    }

    private void OnEnable()
    {
        PerformanceStatCardView.ActiveFocusChanged += HandleCardFocusChanged;

        // A card may already hold focus, for example after a view change.
        HandleCardFocusChanged(PerformanceStatCardView.ActiveFocusedCard);
    }

    private void OnDisable()
    {
        PerformanceStatCardView.ActiveFocusChanged -= HandleCardFocusChanged;

        target = null;
        drawn = null;
        strength = 0f;
        SelectionOutline.Clear();
    }

    private void ResolveGroups()
    {
        groups.Clear();

        if (partGroups != null && partGroups.Length > 0)
        {
            foreach (MachinePartGroup group in partGroups)
            {
                if (group != null)
                    groups.Add(group);
            }

            return;
        }

        foreach (GameObject root in equipmentRoots)
        {
            if (root != null)
                groups.AddRange(root.GetComponentsInChildren<MachinePartGroup>(true));
        }
    }

    private void HandleCardFocusChanged(PerformanceStatCardView card)
    {
        PerformanceStatSource sensor = card != null ? card.BoundSource : null;
        target = null;

        // A sensor read from no part (ambient, supply) outlines nothing.
        if (sensor == null || !CollectRenderers(sensor))
            return;

        target = sensor;

        if (target == drawn)
            return;

        drawn = target;
        SelectionOutline.SetTargets(outlinedRenderers);
    }

    /// <summary>Gathers the renderers of every part the sensor is read from.</summary>
    private bool CollectRenderers(PerformanceStatSource sensor)
    {
        outlinedRenderers.Clear();

        foreach (MachinePartGroup group in groups)
        {
            if (group != null && group.Owns(sensor))
                outlinedRenderers.AddRange(group.Renderers);
        }

        return outlinedRenderers.Count > 0;
    }

    private void Update()
    {
        float goal = target != null ? 1f : 0f;

        if (drawn == null && strength <= 0f)
            return;

        float seconds = goal > strength ? fadeInSeconds : fadeOutSeconds;
        strength = seconds <= 0f ? goal : Mathf.MoveTowards(strength, goal, Time.unscaledDeltaTime / seconds);

        if (strength <= 0f)
        {
            drawn = null;
            SelectionOutline.Clear();
            return;
        }

        // Re-read every frame: an alarm that starts while the card is focused
        // recolours the outline straight away.
        SelectionOutline.SetAppearance(ColorFor(drawn), strength);
    }

    private Color ColorFor(PerformanceStatSource sensor)
    {
        if (sensor == null)
            return normalColor;

        return sensor.VisualState switch
        {
            StatVisualState.Critical => criticalColor,
            StatVisualState.Warning => warningColor,
            StatVisualState.Unavailable => unavailableColor,
            _ => normalColor
        };
    }
}
