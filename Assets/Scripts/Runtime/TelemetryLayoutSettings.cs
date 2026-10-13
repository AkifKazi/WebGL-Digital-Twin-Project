using System;
using UnityEngine;

/// <summary>How many telemetry card rails a layout runs on each side of the model.</summary>
public enum TelemetryRailCount
{
    // Values are stored in scenes: keep them when adding options.
    [InspectorName("Auto (as many as fit)")] Auto = 0,
    [InspectorName("1")] One = 1,
    [InspectorName("2")] Two = 2,
    [InspectorName("3")] Three = 3,
    [InspectorName("4")] Four = 4,
    /// <summary>As many as the viewer chose in the settings (<see cref="DigitalTwinSettings.RailsPerSide"/>).</summary>
    [InspectorName("Viewer's choice (settings)")] Viewer = 5
}

/// <summary>
/// What one screen shape asks of the telemetry rails. The responsive shell
/// hands it to each layout's <see cref="StatRailManager"/>, which reads
/// nothing else from the <see cref="TelemetryLayoutSettings"/>.
/// </summary>
public readonly struct TelemetryLayoutProfile : IEquatable<TelemetryLayoutProfile>
{
    public const float DefaultAutoModelShare = 0.4f;
    public const float DefaultMinimumModelShare = 0.3f;
    public const float DefaultDetailRailWidth = 1.5f;
    public const float DefaultDetailRailHeight = 470f;

    /// <summary>Cards run in rows above and below the model instead of columns beside it.</summary>
    public readonly bool Rows;
    public readonly TelemetryRailCount Rails;
    /// <summary>A card opens its history panel, on a detail rail of its own.</summary>
    public readonly bool DetailPanel;

    private readonly float autoModelShare;
    private readonly float detailRailWidth;
    private readonly float detailRailHeight;
    private readonly int maximumRails;
    private readonly float minimumModelShare;

    public TelemetryLayoutProfile(
        bool rows,
        TelemetryRailCount rails,
        bool detailPanel,
        float autoModelShare = DefaultAutoModelShare,
        float detailRailWidth = DefaultDetailRailWidth,
        float detailRailHeight = DefaultDetailRailHeight,
        int maximumRails = TelemetryLayoutSettings.MaximumRailsPerSide,
        float minimumModelShare = DefaultMinimumModelShare)
    {
        Rows = rows;
        Rails = rails;
        DetailPanel = detailPanel;
        this.autoModelShare = autoModelShare;
        this.detailRailWidth = detailRailWidth;
        this.detailRailHeight = detailRailHeight;
        this.maximumRails = maximumRails;
        this.minimumModelShare = minimumModelShare;
    }

    // A default profile (all zeros) falls back to the defaults rather than
    // giving Auto no model share and the history panel no room.

    /// <summary>Auto adds rails only while the model keeps this share of the screen across them.</summary>
    public float AutoModelShare => autoModelShare > 0f ? autoModelShare : DefaultAutoModelShare;

    /// <summary>Most rails a side, whatever is asked: a phone runs fewer than a desktop.</summary>
    public int MaximumRails => maximumRails > 0
        ? Mathf.Min(maximumRails, TelemetryLayoutSettings.MaximumRailsPerSide)
        : TelemetryLayoutSettings.MaximumRailsPerSide;

    /// <summary>
    /// A set number of rails is kept only while the model keeps this share of
    /// the screen across them; past it, rails drop and their cards go to the
    /// next page rather than crowd the model and each other.
    /// </summary>
    public float MinimumModelShare => minimumModelShare > 0f ? minimumModelShare : DefaultMinimumModelShare;

    /// <summary>Width of the history panel's rail beside card columns, as a multiple of a column's width.</summary>
    public float DetailRailWidthMultiplier => detailRailWidth > 0f ? detailRailWidth : DefaultDetailRailWidth;

    /// <summary>Height kept for the history panel between card rows, in canvas units.</summary>
    public float DetailRailHeight => detailRailHeight > 0f ? detailRailHeight : DefaultDetailRailHeight;

    public bool Equals(TelemetryLayoutProfile other) =>
        Rows == other.Rows && Rails == other.Rails && DetailPanel == other.DetailPanel &&
        AutoModelShare.Equals(other.AutoModelShare) &&
        DetailRailWidthMultiplier.Equals(other.DetailRailWidthMultiplier) &&
        DetailRailHeight.Equals(other.DetailRailHeight) &&
        MaximumRails == other.MaximumRails && MinimumModelShare.Equals(other.MinimumModelShare);

    public override bool Equals(object obj) => obj is TelemetryLayoutProfile other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        Rows, Rails, DetailPanel, AutoModelShare, DetailRailWidthMultiplier, DetailRailHeight, MaximumRails,
        MinimumModelShare);

    public override string ToString() =>
        $"{(Rows ? "rows" : "columns")}={Rails} (most {MaximumRails}) detail={DetailPanel}";
}

/// <summary>
/// The telemetry layout, one place for every screen: how many card rails run
/// beside the model on a desktop browser (columns in a wide window, rows in a
/// window taller than it is wide) and on a phone (columns held sideways, rows
/// held upright), and whether a card opens its history panel.
///
/// By default every layout runs the viewer's choice from the settings
/// (<see cref="DigitalTwinSettings.RailsPerSide"/>: one rail a side unless they
/// ask for more); a layout set to a number here runs that instead. Either way
/// a phone runs at most <see cref="MaximumMobileRailsPerSide"/> a side, so the
/// model and its leader lines stay in sight, and any layout drops rails that
/// would leave the model less than its share of the screen: those cards go
/// to the next page.
///
/// Rails are generated and removed to match: a rail that has no card on the
/// current page hides, so the model keeps the room. The history panel has a
/// rail of its own, between the card rails and the model, and never takes a
/// card rail. On a phone held upright it rises from the bottom as a sheet
/// instead, because a panel beside the rows would not fit.
///
/// Changes made here while playing take effect at once.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryLayoutSettings : MonoBehaviour
{
    public const int MaximumRailsPerSide = 4;

    /// <summary>Most rails a side on a phone or tablet: four in all.</summary>
    public const int MaximumMobileRailsPerSide = 2;

    [Header("Desktop browser")]
    [Tooltip("Card columns on each side of the model in a window wider than it is tall. Auto adds " +
             "columns, as the cards need them, while the model keeps the share below.")]
    [SerializeField] private TelemetryRailCount desktopColumns = TelemetryRailCount.Viewer;

    [Tooltip("Card rows above and below the model in a window taller than it is wide " +
             "(a portrait monitor, or a narrow browser window).")]
    [SerializeField] private TelemetryRailCount desktopRows = TelemetryRailCount.Viewer;

    [Tooltip("A click on a card opens its history panel beside the cards.")]
    [SerializeField] private bool desktopDetailPanel = true;

    [Header("Phone and tablet")]
    [Tooltip("Card columns on each side of the model while the phone is held sideways.")]
    [SerializeField] private TelemetryRailCount mobileColumns = TelemetryRailCount.Viewer;

    [Tooltip("Card rows above and below the model while the phone is held upright.")]
    [SerializeField] private TelemetryRailCount mobileRows = TelemetryRailCount.Viewer;

    [Tooltip("A second tap on a card opens its history: beside the cards held sideways, as a " +
             "sheet from the bottom held upright. The first tap only marks the card.")]
    [SerializeField] private bool mobileDetailPanel = true;

    [Header("Room for the model")]
    [Tooltip("Auto adds rails only while the model keeps at least this share of the screen " +
             "across them.")]
    [SerializeField, Range(0.2f, 0.8f)] private float autoModelShare = TelemetryLayoutProfile.DefaultAutoModelShare;

    [Tooltip("A set number of rails, or the viewer's choice, drops rails that would leave the model " +
             "less than this share of the screen across them; their cards go to the next page.")]
    [SerializeField, Range(0.1f, 0.6f)] private float minimumModelShare = TelemetryLayoutProfile.DefaultMinimumModelShare;

    [Header("History panel rail")]
    [Tooltip("Width of the history panel's rail beside card columns, as a multiple of a card column's width.")]
    [SerializeField, Range(1f, 2.5f)] private float detailRailWidth = TelemetryLayoutProfile.DefaultDetailRailWidth;

    [Tooltip("Height kept for the history panel between card rows, in canvas units. Rows that " +
             "would leave less are not added.")]
    [SerializeField, Min(200f)] private float detailRailHeight = TelemetryLayoutProfile.DefaultDetailRailHeight;

    /// <summary>Raised when a value changes in the inspector while playing.</summary>
    public event Action Changed;

    /// <summary>What the rails should do on this device, held this way up.</summary>
    public TelemetryLayoutProfile Resolve(bool mobile, bool rows)
    {
        TelemetryRailCount rails = mobile ? (rows ? mobileRows : mobileColumns) : (rows ? desktopRows : desktopColumns);
        if (rails == TelemetryRailCount.Viewer)
            rails = (TelemetryRailCount)DigitalTwinSettings.RailsPerSide;
        return new TelemetryLayoutProfile(
            rows,
            rails,
            mobile ? mobileDetailPanel : desktopDetailPanel,
            autoModelShare,
            detailRailWidth,
            detailRailHeight,
            mobile ? MaximumMobileRailsPerSide : MaximumRailsPerSide,
            minimumModelShare);
    }

    /// <summary>Sets one layout's rail count from code (tests, setup tools).</summary>
    public void SetRails(bool mobile, bool rows, TelemetryRailCount count)
    {
        if (mobile && rows) mobileRows = count;
        else if (mobile) mobileColumns = count;
        else if (rows) desktopRows = count;
        else desktopColumns = count;
        Changed?.Invoke();
    }

    /// <summary>Turns the history panel on or off from code (tests, setup tools).</summary>
    public void SetDetailPanel(bool mobile, bool enabled)
    {
        if (mobile)
            mobileDetailPanel = enabled;
        else
            desktopDetailPanel = enabled;
        Changed?.Invoke();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
            Changed?.Invoke();
    }
#endif
}
