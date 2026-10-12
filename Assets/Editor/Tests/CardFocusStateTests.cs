using NUnit.Framework;

/// <summary>
/// The rules of card focus, without a scene. Run with
/// <c>unity test --mode EditMode</c>.
/// </summary>
public sealed class CardFocusStateTests
{
    private sealed class Card { }
    private sealed class Panel { }

    private CardFocusState<Card, Panel> focus;
    private Card first;
    private Card second;

    [SetUp]
    public void SetUp()
    {
        focus = new CardFocusState<Card, Panel>();
        first = new Card();
        second = new Card();
    }

    [Test]
    public void AFirstPressFocusesAndASecondOpensTheDetail()
    {
        Assert.AreEqual(CardPressResult.Focused, focus.Press(first, false));
        Assert.IsTrue(focus.IsFocused(first));
        Assert.AreEqual(CardPressResult.Opened, focus.Press(first, false));
        Assert.IsTrue(focus.IsFocused(first), "opening the detail keeps the focus");
    }

    [Test]
    public void AFirstPressCanOpenTheDetailAtOnce()
    {
        Assert.AreEqual(CardPressResult.FocusedAndOpened, focus.Press(first, true));
        Assert.AreEqual(CardPressResult.Opened, focus.Press(first, true));
    }

    [Test]
    public void OnlyOneCardIsEverFocused()
    {
        focus.Press(first, false);
        focus.Press(second, false);
        Assert.IsFalse(focus.IsFocused(first));
        Assert.IsTrue(focus.IsFocused(second));
        Assert.AreEqual(CardPressResult.Focused, focus.Press(first, false),
            "a card that lost the focus takes it back before it opens anything");
    }

    [Test]
    public void ReleasingOnlyReleasesTheFocusedCard()
    {
        focus.Press(first, false);
        Assert.IsFalse(focus.Release(second));
        Assert.IsTrue(focus.IsFocused(first));
        Assert.IsTrue(focus.Release(first));
        Assert.IsNull(focus.Focused);
        Assert.IsFalse(focus.Release(), "nothing left to release");
    }

    [Test]
    public void APressOnNothingDoesNothing()
    {
        focus.Press(first, false);
        Assert.AreEqual(CardPressResult.None, focus.Press(null, true));
        Assert.IsTrue(focus.IsFocused(first));
    }

    [Test]
    public void AClickOpensAtOnceButATapOnlyFocuses()
    {
        const CardDetailOpening Wide = CardDetailOpening.FirstClickOrSecondTap;
        Assert.IsTrue(CardFocusState<Card, Panel>.OpensOnFirstPress(Wide, CardPressKind.Mouse, false));
        Assert.IsTrue(CardFocusState<Card, Panel>.OpensOnFirstPress(Wide, CardPressKind.Keyboard, false));
        Assert.IsFalse(CardFocusState<Card, Panel>.OpensOnFirstPress(Wide, CardPressKind.Touch, false));
        Assert.IsTrue(CardFocusState<Card, Panel>.OpensOnFirstPress(Wide, CardPressKind.Touch, true),
            "with a panel on show, a tap on another card moves it there");
    }

    [Test]
    public void ASheetAlwaysWaitsForTheSecondPress()
    {
        foreach (CardPressKind kind in new[] { CardPressKind.Mouse, CardPressKind.Touch, CardPressKind.Keyboard })
        {
            Assert.IsFalse(CardFocusState<Card, Panel>.OpensOnFirstPress(CardDetailOpening.SecondPress, kind, false));
            Assert.IsFalse(CardFocusState<Card, Panel>.OpensOnFirstPress(CardDetailOpening.SecondPress, kind, true));
        }
    }

    [Test]
    public void DetailsAreRecordedPerCard()
    {
        Panel panel = new();
        focus.SetDetail(first, panel);
        Assert.IsTrue(focus.HasDetail(first));
        Assert.IsTrue(focus.TryGetDetail(first, out Panel shown));
        Assert.AreSame(panel, shown);
        Assert.IsFalse(focus.HasDetail(second));
    }

    [Test]
    public void ASheetIsAnOpenDetailWithNothingBesideTheRails()
    {
        focus.SetDetail(first, null);
        Assert.IsTrue(focus.HasDetail(first));
        Assert.IsFalse(focus.TryGetDetail(first, out _));
    }

    [Test]
    public void APanelThatMovedOnCannotCloseAnotherPanelsDetail()
    {
        Panel old = new();
        Panel current = new();
        focus.SetDetail(first, old);
        focus.SetDetail(first, current);
        Assert.IsFalse(focus.ClearDetail(first, old));
        Assert.IsTrue(focus.HasDetail(first));
        Assert.IsTrue(focus.ClearDetail(first, current));
        Assert.IsFalse(focus.HasDetail(first));
    }

    [Test]
    public void ReleasingTheFocusLeavesDetailsOpen()
    {
        // A pinned panel stays when its card is released.
        focus.Press(first, true);
        focus.SetDetail(first, new Panel());
        focus.Release();
        Assert.IsTrue(focus.HasDetail(first));
    }

    [Test]
    public void AForgottenCardTakesItsFocusAndDetailWithIt()
    {
        focus.Press(first, true);
        focus.SetDetail(first, new Panel());
        Assert.IsTrue(focus.Forget(first));
        Assert.IsNull(focus.Focused);
        Assert.IsFalse(focus.HasDetail(first));
    }

    [Test]
    public void OtherCardsDimOnlyWhileAnotherIsFocused()
    {
        Assert.AreEqual(1f, focus.PeerAlpha(second, false, true, 0.15f), "nothing focused");
        focus.Press(first, false);
        Assert.AreEqual(1f, focus.PeerAlpha(first, false, true, 0.15f), "the focused card");
        Assert.AreEqual(0.15f, focus.PeerAlpha(second, false, true, 0.15f));
        Assert.AreEqual(1f, focus.PeerAlpha(second, true, true, 0.15f), "an alarm never dims");
        Assert.AreEqual(1f, focus.PeerAlpha(second, false, false, 0.15f), "the setting is off");
    }

    [Test]
    public void AFadeEasesToItsTargetAndStops()
    {
        SmoothFade fade = new(1f);
        fade.Begin(0.2f, 10f);
        Assert.IsTrue(fade.Moving);
        Assert.IsTrue(fade.Step(10.5f, 1f));
        Assert.AreEqual(0.6f, fade.Value, 1e-4f, "half way");
        fade.Step(11f, 1f);
        Assert.AreEqual(0.2f, fade.Value);
        Assert.IsFalse(fade.Moving);
        Assert.IsFalse(fade.Step(12f, 1f), "settled: nothing more to apply");
    }

    [Test]
    public void AnUnchangedTargetDoesNotRestartAFade()
    {
        SmoothFade fade = new(1f);
        fade.Begin(0f, 0f);
        fade.Step(0.5f, 1f);
        float halfway = fade.Value;
        fade.Begin(0f, 0.5f);
        fade.Step(0.5f, 1f);
        Assert.AreEqual(halfway, fade.Value, 1e-4f);
    }
}
