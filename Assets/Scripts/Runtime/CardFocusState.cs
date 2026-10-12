using System.Collections.Generic;
using UnityEngine;

/// <summary>How a press reached a card.</summary>
public enum CardPressKind
{
    Mouse,
    Touch,
    Keyboard
}

/// <summary>When a layout opens a card's telemetry detail.</summary>
public enum CardDetailOpening
{
    /// <summary>A click or Enter focuses the card and opens its detail at once; a tap only focuses it, and a second tap opens it.</summary>
    FirstClickOrSecondTap,

    /// <summary>Any first press only focuses the card; a second press opens its detail.</summary>
    SecondPress
}

/// <summary>What a press on a card did.</summary>
public enum CardPressResult
{
    None,
    /// <summary>The card took the focus; its detail stays closed.</summary>
    Focused,
    /// <summary>The card took the focus and its detail opens.</summary>
    FocusedAndOpened,
    /// <summary>The card already had the focus; its detail opens.</summary>
    Opened
}

/// <summary>
/// The rules of card focus, without a scene: which card is focused, which
/// cards have their telemetry detail open, what a press on a card does, and
/// how far the other cards and their lines dim. <see cref="CardFocus"/> holds
/// the one instance the twin uses; tests build their own.
///
/// Only one card is ever focused. Pressing a card focuses it, which releases
/// any other; pressing the focused card again opens its detail. A detail is
/// recorded per card with what draws it (a panel beside the rails, say), so a
/// layout can ask whether a card's detail is open and where.
/// </summary>
public sealed class CardFocusState<TCard, TDetail>
    where TCard : class
    where TDetail : class
{
    private readonly Dictionary<TCard, TDetail> details = new();

    public TCard Focused { get; private set; }

    /// <summary>Cards whose detail is open, each with what draws it (which may be null).</summary>
    public IReadOnlyDictionary<TCard, TDetail> Details => details;

    public bool IsFocused(TCard card) => card != null && ReferenceEquals(Focused, card);

    /// <summary>
    /// A press on <paramref name="card"/>. <paramref name="opensOnFirstPress"/>
    /// says whether a press that focuses it also opens its detail (see
    /// <see cref="OpensOnFirstPress"/>).
    /// </summary>
    public CardPressResult Press(TCard card, bool opensOnFirstPress)
    {
        if (card == null)
            return CardPressResult.None;
        if (IsFocused(card))
            return CardPressResult.Opened;

        Focused = card;
        return opensOnFirstPress ? CardPressResult.FocusedAndOpened : CardPressResult.Focused;
    }

    /// <summary>Releases whichever card is focused. True when one was.</summary>
    public bool Release()
    {
        if (Focused == null)
            return false;
        Focused = null;
        return true;
    }

    /// <summary>Releases <paramref name="card"/> if it is the focused one. True when it was.</summary>
    public bool Release(TCard card) => IsFocused(card) && Release();

    /// <summary>A card that leaves for good: its focus and its detail go with it.</summary>
    public bool Forget(TCard card)
    {
        if (card == null)
            return false;
        details.Remove(card);
        return Release(card);
    }

    public void SetDetail(TCard card, TDetail detail)
    {
        if (card != null)
            details[card] = detail;
    }

    /// <summary>
    /// Closes the card's detail, but only if <paramref name="detail"/> is what
    /// draws it: a panel that has already moved to another card cannot close
    /// the detail that card's new panel just opened.
    /// </summary>
    public bool ClearDetail(TCard card, TDetail detail)
    {
        if (card == null || !details.TryGetValue(card, out TDetail current) || !ReferenceEquals(current, detail))
            return false;
        return details.Remove(card);
    }

    public bool HasDetail(TCard card) => card != null && details.ContainsKey(card);

    /// <summary>True when the card's detail is open and something draws it.</summary>
    public bool TryGetDetail(TCard card, out TDetail detail)
    {
        detail = null;
        return card != null && details.TryGetValue(card, out detail) && detail != null;
    }

    /// <summary>
    /// How strongly a card (and its leader line) shows while another may have
    /// the focus: fully unless another card is focused, the setting to dim the
    /// others is on, and this card is not raising an alarm.
    /// </summary>
    public float PeerAlpha(TCard card, bool cardIsAlarm, bool dimOthers, float dimmedAlpha)
    {
        if (Focused == null || IsFocused(card) || cardIsAlarm || !dimOthers)
            return 1f;
        return Mathf.Clamp01(dimmedAlpha);
    }

    /// <summary>Whether a press that focuses a card also opens its detail.</summary>
    public static bool OpensOnFirstPress(CardDetailOpening opening, CardPressKind kind, bool detailShowing) =>
        opening == CardDetailOpening.FirstClickOrSecondTap &&
        (kind != CardPressKind.Touch || detailShowing);
}

/// <summary>
/// A value that eases towards a target over a set time, smoothstepped. Cards
/// and leader lines use it to dim and return as the focus moves.
/// </summary>
public struct SmoothFade
{
    private float start;
    private float startedAt;
    private bool moving;

    public float Value { get; private set; }
    public float Target { get; private set; }
    public bool Moving => moving;

    public SmoothFade(float value)
    {
        start = value;
        startedAt = 0f;
        moving = false;
        Value = value;
        Target = value;
    }

    /// <summary>Jumps to <paramref name="value"/> and stops.</summary>
    public void Snap(float value)
    {
        value = Mathf.Clamp01(value);
        Value = start = Target = value;
        moving = false;
    }

    /// <summary>Starts towards <paramref name="target"/> from where it is. An unchanged target does not restart.</summary>
    public void Begin(float target, float now)
    {
        target = Mathf.Clamp01(target);
        if (Mathf.Approximately(Target, target) && (!moving || Mathf.Approximately(Value, target)))
            return;
        if (Mathf.Approximately(Value, target))
        {
            Snap(target);
            return;
        }
        start = Value;
        Target = target;
        startedAt = now;
        moving = true;
    }

    /// <summary>Moves on to <paramref name="now"/>. True when the value changed.</summary>
    public bool Step(float now, float duration)
    {
        if (!moving)
            return false;
        float progress = Mathf.Clamp01((now - startedAt) / Mathf.Max(0.02f, duration));
        Value = Mathf.Lerp(start, Target, Mathf.SmoothStep(0f, 1f, progress));
        if (progress >= 1f)
        {
            Value = Target;
            moving = false;
        }
        return true;
    }
}
