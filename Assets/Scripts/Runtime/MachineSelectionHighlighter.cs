using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outlines the mechanism behind the focused telemetry card, in every view.
/// The colour follows the mechanism's worst alarm state, using the same
/// palette as the cards: blue within limits, amber on warning, red when
/// critical.
///
/// Follows the card's own focus signal, so mouse hover and the timed touch
/// focus on mobile behave identically here.
/// </summary>
[DisallowMultipleComponent]
public sealed class MachineSelectionHighlighter : MonoBehaviour
{
    [Header("Mechanisms")]
    [Tooltip("Mechanisms that can be outlined. Left empty, they are found under the equipment roots.")]
    [SerializeField] private MachinePartGroup[] partGroups = System.Array.Empty<MachinePartGroup>();

    [Tooltip("Roots searched for mechanisms when the list above is empty.")]
    [SerializeField] private GameObject[] equipmentRoots = System.Array.Empty<GameObject>();

    [Header("Colours")]
    [Tooltip("Outline colour while every sensor on the mechanism is within limits.")]
    [SerializeField] private Color normalColor = new(0.25f, 0.65f, 1f, 1f);

    [Tooltip("Outline colour while a sensor on the mechanism is in warning.")]
    [SerializeField] private Color warningColor = new(1f, 0.72f, 0.18f, 1f);

    [Tooltip("Outline colour while a sensor on the mechanism is critical.")]
    [SerializeField] private Color criticalColor = new(1f, 0.26f, 0.22f, 1f);

    [Tooltip("Outline colour while the mechanism's readings are unavailable or stale.")]
    [SerializeField] private Color unavailableColor = new(0.55f, 0.62f, 0.70f, 1f);

    [Header("Timing")]
    [Tooltip("Seconds for the outline to appear.")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.12f;

    [Tooltip("Seconds for the outline to disappear.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.2f;

    private readonly List<MachinePartGroup> groups = new();

    // Target is what focus asks for; drawn is what the outline currently shows,
    // kept while it fades out so the colour does not change mid-fade.
    private MachinePartGroup target;
    private MachinePartGroup drawn;
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

        if (sensor == null)
            return;

        foreach (MachinePartGroup group in groups)
        {
            if (group != null && group.Owns(sensor))
            {
                target = group;
                break;
            }
        }

        // A sensor with no mechanism (ambient, supply) outlines nothing.
        if (target == null || target == drawn)
            return;

        drawn = target;
        SelectionOutline.SetTargets(drawn.Renderers);
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

    private Color ColorFor(MachinePartGroup group)
    {
        if (group == null)
            return normalColor;

        return group.WorstState switch
        {
            StatVisualState.Critical => criticalColor,
            StatVisualState.Warning => warningColor,
            StatVisualState.Unavailable => unavailableColor,
            _ => normalColor
        };
    }
}
