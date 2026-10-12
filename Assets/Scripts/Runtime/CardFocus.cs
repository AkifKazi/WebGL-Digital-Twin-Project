using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A detail drawn beside a card's rail: the panel the card connects to, and
/// from which its leader line continues to the sensor.
/// </summary>
public interface ICardDetailLink
{
    /// <summary>Where the connector from the card meets the panel: its edge facing the card.</summary>
    RectTransform CardSidePoint { get; }

    /// <summary>Where the leader line leaves the panel for the sensor: its edge facing the model.</summary>
    RectTransform ModelSidePoint { get; }

    /// <summary>The panel's opacity, which the connector follows as the panel fades in and out.</summary>
    float Alpha { get; }
}

/// <summary>
/// What opens a card's telemetry detail in one layout: the wide layout's
/// panels, or the portrait layout's sheet. <see cref="CardFocus"/> asks the
/// one that presents a pressed card what the press should do.
/// </summary>
public interface ICardDetailPresenter
{
    /// <summary>True when this presenter shows the card's detail: the card is its layout's, and it is ready to.</summary>
    bool Presents(PerformanceStatCardView card);

    CardDetailOpening Opening { get; }

    /// <summary>True while a detail is on show that a press on another card would move to it.</summary>
    bool ShowsDetail { get; }

    void ShowDetail(PerformanceStatCardView card);

    /// <summary>The card took the focus without opening its detail.</summary>
    void FocusedWithoutDetail(PerformanceStatCardView card);

    /// <summary>A click on the scene, or Esc: close what an outside click closes.</summary>
    void Dismiss();
}

/// <summary>
/// The twin's card focus: which telemetry card is focused, which cards have
/// their detail open, and what a press on a card does. Only one layout is on
/// screen at a time, and the model's own highlights follow the one focused
/// card, so there is one card focus for the whole twin. Its rules live in the
/// plain <see cref="CardFocusState{TCard,TDetail}"/>, covered by edit-mode
/// tests; this holds the instance and tells everything that follows it.
///
/// Cards report presses here; the detail panels and the sheet register as
/// <see cref="ICardDetailPresenter"/>s and record what they show; cards,
/// leader lines, the rails and the model highlights ask it.
/// </summary>
public static class CardFocus
{
    private static CardFocusState<PerformanceStatCardView, ICardDetailLink> state = new();
    private static readonly List<ICardDetailPresenter> Presenters = new();

    /// <summary>Raised with the newly focused card, or null when the focus is released.</summary>
    public static event Action<PerformanceStatCardView> Changed;

    public static PerformanceStatCardView Focused => state.Focused;

    public static bool IsFocused(PerformanceStatCardView card) => state.IsFocused(card);

    // Statics outlive a play session when the editor skips the domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        state = new CardFocusState<PerformanceStatCardView, ICardDetailLink>();
        Presenters.Clear();
        Changed = null;
    }

    // -----------------------------------------------------------------------
    // Presses and releases
    // -----------------------------------------------------------------------

    /// <summary>
    /// A clean click, tap or Enter on a card. The first press focuses it; the
    /// next opens its detail. A layout whose detail opens on a click opens it
    /// with the first click, while a tap there still only focuses, unless a
    /// detail is already on show and simply moves to this card.
    /// </summary>
    public static CardPressResult Press(PerformanceStatCardView card, CardPressKind kind)
    {
        if (card == null)
            return CardPressResult.None;

        ICardDetailPresenter presenter = PresenterFor(card);
        bool opensAtOnce = presenter != null &&
            CardFocusState<PerformanceStatCardView, ICardDetailLink>.OpensOnFirstPress(
                presenter.Opening, kind, presenter.ShowsDetail);

        PerformanceStatCardView previous = state.Focused;
        CardPressResult result = state.Press(card, opensAtOnce);
        if (state.Focused != previous)
            Changed?.Invoke(state.Focused);

        if (presenter != null)
        {
            if (result is CardPressResult.Opened or CardPressResult.FocusedAndOpened)
                presenter.ShowDetail(card);
            else if (result == CardPressResult.Focused)
                presenter.FocusedWithoutDetail(card);
        }
        return result;
    }

    /// <summary>Releases whichever card is focused.</summary>
    public static void Release()
    {
        if (state.Release())
            Changed?.Invoke(null);
    }

    /// <summary>Releases <paramref name="card"/> if it is the focused one.</summary>
    public static void Release(PerformanceStatCardView card)
    {
        if (state.Release(card))
            Changed?.Invoke(null);
    }

    /// <summary>A click on the scene, or Esc: the focus is released and unpinned details close.</summary>
    public static void Dismiss()
    {
        Release();
        // A presenter may unregister while dismissing; walk a copy.
        foreach (ICardDetailPresenter presenter in Presenters.ToArray())
            presenter.Dismiss();
    }

    /// <summary>A card destroyed: nothing of it stays behind.</summary>
    public static void Forget(PerformanceStatCardView card)
    {
        if (state.Forget(card))
            Changed?.Invoke(null);
    }

    // -----------------------------------------------------------------------
    // Details
    // -----------------------------------------------------------------------

    public static void Register(ICardDetailPresenter presenter)
    {
        if (presenter != null && !Presenters.Contains(presenter))
            Presenters.Add(presenter);
    }

    public static void Unregister(ICardDetailPresenter presenter) => Presenters.Remove(presenter);

    /// <summary>Records that the card's detail is open, drawn by <paramref name="link"/> (null for a sheet).</summary>
    public static void OpenDetail(PerformanceStatCardView card, ICardDetailLink link) => state.SetDetail(card, link);

    /// <summary>Records that <paramref name="link"/> no longer shows the card's detail.</summary>
    public static void CloseDetail(PerformanceStatCardView card, ICardDetailLink link) => state.ClearDetail(card, link);

    public static bool HasDetail(PerformanceStatCardView card) => state.HasDetail(card);

    /// <summary>
    /// The panel beside the rails that shows this card's detail, while one is
    /// open and shown. A sheet is not beside the rails and does not count.
    /// </summary>
    public static bool TryGetDetailLink(PerformanceStatCardView card, out ICardDetailLink link)
    {
        if (!state.TryGetDetail(card, out link))
            return false;
        // A panel whose object is gone, or hidden, draws nothing.
        if (link is not Component component || component == null ||
            link.ModelSidePoint == null || !link.ModelSidePoint.gameObject.activeInHierarchy)
        {
            link = null;
            return false;
        }
        return true;
    }

    // -----------------------------------------------------------------------
    // Presentation
    // -----------------------------------------------------------------------

    /// <summary>How strongly <paramref name="card"/> and its leader line show while another card may have the focus.</summary>
    public static float PeerAlpha(PerformanceStatCardView card) =>
        card == null
            ? 1f
            : state.PeerAlpha(card, card.IsAlarm, DigitalTwinSettings.DimOtherCards, card.UnfocusedPeerOpacity);

    private static ICardDetailPresenter PresenterFor(PerformanceStatCardView card)
    {
        foreach (ICardDetailPresenter presenter in Presenters)
        {
            if (presenter != null && presenter.Presents(card))
                return presenter;
        }
        return null;
    }
}
