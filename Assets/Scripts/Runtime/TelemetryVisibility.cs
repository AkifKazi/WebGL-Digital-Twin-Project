using System;
using UnityEngine;

/// <summary>
/// The machine's inspection view, as telemetry needs it: whether the view on
/// show (Full Model, Cross Section or X-Ray) shows telemetry. Implemented by
/// <see cref="MachineViewModeController"/>; generic telemetry presentation
/// reads it through <see cref="TelemetryVisibility"/> and never depends on
/// the machine's own view controllers.
/// </summary>
public interface IInspectionView
{
    bool ShowsTelemetry { get; }

    event Action<bool> ShowsTelemetryChanged;
}

/// <summary>
/// One layout's telemetry visibility, and its switch between the Telemetry and
/// Service views. It combines the view (and the blend while switching), the
/// inspection view and, in the portrait layout, the sheet's cover, and is the
/// only thing that fades the layout's telemetry regions and service groups.
/// Everything that hides or reveals telemetry asks it, never the inputs.
///
/// Nothing is deactivated or rebuilt: the regions fade through CanvasGroups
/// and come back exactly as they were. Each layout's service view
/// (<see cref="WideServiceView"/>, <see cref="PortraitServiceView"/>) reads
/// <see cref="ServiceBlend"/> to place its panel and does nothing else here.
///
/// The chosen view is shared by both layouts (see
/// <see cref="DigitalTwinModeState"/>), so turning a phone keeps it.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(40)]
public sealed class TelemetryVisibility : MonoBehaviour, IDigitalTwinModeController
{
    [Tooltip("This layout's telemetry regions: the parts that hold only telemetry and fade as one.")]
    [SerializeField] private CanvasGroup[] telemetryRegions = Array.Empty<CanvasGroup>();

    [Tooltip("Shown in Service: the service panel, the pins on the model, the backdrop.")]
    [SerializeField] private CanvasGroup[] serviceGroups = Array.Empty<CanvasGroup>();

    [Tooltip("The machine's inspection view: a component implementing IInspectionView (the " +
             "Machine View Mode Controller). Empty, the inspection view always shows telemetry.")]
    [SerializeField] private MonoBehaviour inspectionView;

    [Tooltip("Optional. Releases any focused telemetry card, and closes unpinned detail panels, on entering Service.")]
    [SerializeField] private TelemetryCardFocusReleaser focusReleaser;

    [Tooltip("Seconds the switch between views takes.")]
    [SerializeField, Min(0.01f)] private float transitionSeconds = 0.35f;

    [SerializeField] private DigitalTwinMode startMode = DigitalTwinMode.Telemetry;

    private TelemetryVisibilityState state;
    private IInspectionView subscribedInspection;
    private bool dirty = true;

    public DigitalTwinMode CurrentMode => DigitalTwinModeState.Get(startMode);

    /// <summary>0 in Telemetry, 1 in Service, between the two while switching.</summary>
    public float ServiceBlend => State.ServiceBlend;

    /// <summary>The blend eased as the groups fade, for motion that should keep pace with them.</summary>
    public float EasedServiceBlend => State.EasedServiceBlend;

    /// <summary>Whether the inspection view shows telemetry. What reveals the cards one by one follows it.</summary>
    public bool InspectionShowsTelemetry => State.InspectionShowsTelemetry;

    public event Action<DigitalTwinMode> ModeChanged;

    /// <summary>Raised when <see cref="InspectionShowsTelemetry"/> changes.</summary>
    public event Action<bool> InspectionShowsTelemetryChanged;

    private IInspectionView Inspection => inspectionView as IInspectionView;

    private TelemetryVisibilityState State =>
        state ??= new TelemetryVisibilityState(CurrentMode, Inspection?.ShowsTelemetry ?? true);

    public void SetMode(DigitalTwinMode mode)
    {
        if (mode == CurrentMode)
            return;

        DigitalTwinModeState.Set(mode);
        State.SetMode(mode);
        dirty = true;
        if (mode == DigitalTwinMode.Service)
            focusReleaser?.Dismiss();

        ModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// How far something over the model (the portrait telemetry sheet) hides
    /// telemetry in the Telemetry view: 0 not at all, 1 fully, which also makes it inert.
    /// </summary>
    public void SetCover(float cover)
    {
        if (State.SetCover(cover))
            Apply();
    }

    private void OnEnable()
    {
        subscribedInspection = Inspection;
        if (subscribedInspection != null)
        {
            subscribedInspection.ShowsTelemetryChanged += HandleInspectionChanged;
            HandleInspectionChanged(subscribedInspection.ShowsTelemetry);
        }

        // Returning to this layout lands on the chosen view straight away.
        State.SetMode(CurrentMode);
        State.Snap();
        Apply();
    }

    private void OnDisable()
    {
        if (subscribedInspection != null)
            subscribedInspection.ShowsTelemetryChanged -= HandleInspectionChanged;
        subscribedInspection = null;

        // The other layout takes over: leave this one's telemetry as it expects.
        State.SetCover(0f);
        SetGroups(telemetryRegions, 1f, true, true);
        SetGroups(serviceGroups, 0f, false, false);
        dirty = true;
    }

    private void LateUpdate()
    {
        // The other layout may have chosen the view.
        dirty |= State.SetMode(CurrentMode);
        dirty |= State.Step(Time.unscaledDeltaTime, transitionSeconds);
        if (dirty)
            Apply();
    }

    private void HandleInspectionChanged(bool shows)
    {
        if (State.SetInspectionShowsTelemetry(shows))
            InspectionShowsTelemetryChanged?.Invoke(shows);
    }

    private void Apply()
    {
        dirty = false;
        TelemetryVisibilityState s = State;
        SetGroups(telemetryRegions, s.TelemetryAlpha, s.TelemetryInteractive, s.TelemetryBlocksPointer);
        SetGroups(serviceGroups, s.ServiceAlpha, s.ServiceInteractive, s.ServiceBlocksPointer);
    }

    private static void SetGroups(CanvasGroup[] groups, float alpha, bool interactive, bool blocksPointer)
    {
        foreach (CanvasGroup group in groups)
        {
            if (group == null)
                continue;
            group.alpha = alpha;
            group.interactable = interactive;
            group.blocksRaycasts = blocksPointer;
        }
    }
}
