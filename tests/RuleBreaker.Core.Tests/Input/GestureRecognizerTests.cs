namespace RuleBreaker.Core.Tests.Input;

using RuleBreaker.Core.Input;
using RuleBreaker.Core.Model;
using Xunit;

/// <summary>
/// Gesture recognition — GDD principles 50–52 and the plan's disambiguation rules:
/// a press that passes the hold threshold is a Hold, movement past the drag
/// threshold is a Swipe, and a lone Click commits only after the double-click
/// window closes.
/// </summary>
public class GestureRecognizerTests
{
    private const double Tolerance = 1e-9;

    private static Gesture Single(GestureRecognizer recognizer)
    {
        var gestures = recognizer.ReadGestures();
        return Assert.Single(gestures);
    }

    // ── Click: deferred by the double-click window ───────────────────────────────
    [Fact]
    public void Click_IsNotReportedUntilTheDoubleClickWindowCloses()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(100, 100, 0.0);
        recognizer.PointerUp(100, 100, 0.1);

        Assert.Empty(recognizer.ReadGestures());

        recognizer.AdvanceTo(0.399);
        Assert.Empty(recognizer.ReadGestures());

        recognizer.AdvanceTo(0.4); // 0.1 s release + 0.3 s window
        var click = Single(recognizer);

        Assert.Equal(GestureKind.Click, click.Kind);
        Assert.Equal(1, click.Clicks);
        Assert.Equal(0.0, click.PressedAtSeconds, 9);
        Assert.Equal(0.1, click.ReleasedAtSeconds, 9);
        Assert.Equal(0.1, click.DurationSeconds, 9);
        Assert.Equal(CoinAction.Click, click.ToCoinAction());
    }

    [Fact]
    public void TwoTapsInsideTheWindow_AreOneDoubleClick_NotTwoClicks()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(100, 100, 0.00);
        recognizer.PointerUp(100, 100, 0.10);
        recognizer.PointerDown(101, 100, 0.20); // inside the window
        recognizer.PointerUp(101, 100, 0.25);

        var gesture = Single(recognizer);

        Assert.Equal(GestureKind.DoubleClick, gesture.Kind);
        Assert.Equal(2, gesture.Clicks);
        Assert.Equal(0.00, gesture.PressedAtSeconds, 9);
        Assert.Equal(0.25, gesture.ReleasedAtSeconds, 9);
        Assert.Equal(CoinAction.DoubleClick, gesture.ToCoinAction());

        // The deferred Click was cancelled, not emitted late.
        recognizer.AdvanceTo(2.0);
        Assert.Empty(recognizer.ReadGestures());
    }

    [Fact]
    public void APressAfterTheWindow_CommitsTheFirstTapThenStartsANewOne()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(100, 100, 0.0);
        recognizer.PointerUp(100, 100, 0.1);

        recognizer.AdvanceTo(0.45); // window closed → first Click
        var first = Single(recognizer);
        Assert.Equal(GestureKind.Click, first.Kind);

        recognizer.PointerDown(100, 100, 0.6);
        recognizer.PointerUp(100, 100, 0.7);
        recognizer.AdvanceTo(0.99);
        Assert.Empty(recognizer.ReadGestures());

        recognizer.AdvanceTo(1.0); // 0.7 + 0.3
        var second = Single(recognizer);
        Assert.Equal(GestureKind.Click, second.Kind);
        Assert.Equal(0.6, second.PressedAtSeconds, 9);
    }

    [Fact]
    public void AnAmbiguousTap_CanBeResolvedAsAClickBeforeTheWindowCloses()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(100, 100, 0.0);
        recognizer.PointerUp(100, 100, 0.2);
        recognizer.FlushPendingClick();

        var click = Single(recognizer);
        Assert.Equal(GestureKind.Click, click.Kind);
        Assert.Equal(0.2, click.ReleasedAtSeconds, 9);
    }

    // ── Hold: the 400 ms threshold wins over a late drag ─────────────────────────
    [Fact]
    public void APressThatReachesTheHoldThreshold_IsAHold_NotAClick()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(100, 100, 0.0);
        recognizer.PointerUp(100, 100, 0.45);

        var hold = Single(recognizer);

        Assert.Equal(GestureKind.Hold, hold.Kind);
        Assert.Equal(CoinAction.Hold, hold.ToCoinAction());
        Assert.Equal(0.45, hold.DurationSeconds, 9);
    }

    [Fact]
    public void HoldThresholdAtExactly400Milliseconds_CountsAsAHold()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(100, 100, 0.0);
        recognizer.PointerUp(100, 100, 0.4);

        Assert.Equal(GestureKind.Hold, Single(recognizer).Kind);
    }

    [Fact]
    public void TheCoinGlowsWhileTheButtonIsStillDown_AndTheGestureWaitsForRelease()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(100, 100, 0.0);
        recognizer.AdvanceTo(0.399);

        Assert.False(recognizer.IsHolding);
        Assert.Equal(new[] { CoinFeedback.Depress }, recognizer.ReadFeedback());

        recognizer.AdvanceTo(0.4);

        Assert.True(recognizer.IsHolding);
        Assert.Empty(recognizer.ReadGestures());
        Assert.Equal(new[] { CoinFeedback.Glow }, recognizer.ReadFeedback());

        recognizer.PointerUp(100, 100, 0.5);
        Assert.Equal(GestureKind.Hold, Single(recognizer).Kind);
    }

    [Fact]
    public void DragBeforeHold_StaysASwipeEvenWhenTheButtonIsHeld()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.0);
        recognizer.PointerMove(30, 0, 0.1); // drag threshold first
        recognizer.AdvanceTo(0.6);          // hold threshold later
        recognizer.PointerUp(30, 0, 0.7);

        var gesture = Single(recognizer);

        Assert.Equal(GestureKind.Swipe, gesture.Kind);
        Assert.Equal(30.0, gesture.TravelPixels, 9);

        // The coin followed the cursor; it never glowed, because first threshold wins.
        var feedback = recognizer.ReadFeedback();
        Assert.Contains(CoinFeedback.FollowCursor, feedback);
        Assert.DoesNotContain(CoinFeedback.Glow, feedback);
    }

    [Fact]
    public void HoldBeforeDrag_StaysAHoldEvenWhenThePlayerDragsLater()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.0);
        recognizer.AdvanceTo(0.45);       // hold threshold first
        recognizer.PointerMove(50, 0, 0.5);
        recognizer.PointerUp(50, 0, 0.6);

        var gesture = Single(recognizer);

        Assert.Equal(GestureKind.Hold, gesture.Kind);
        Assert.Equal(50.0, gesture.TravelPixels, 9);
        Assert.Contains(CoinFeedback.Glow, recognizer.ReadFeedback());
    }

    // ── Swipe: the 24 px threshold ───────────────────────────────────────────────
    [Fact]
    public void MovementPastTheDragThreshold_IsASwipe_OnlyOnceTheButtonComesUp()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.0);
        recognizer.PointerMove(10, 0, 0.1);
        Assert.False(recognizer.IsDragging);

        recognizer.PointerMove(24, 0, 0.2);
        Assert.True(recognizer.IsDragging);
        Assert.Empty(recognizer.ReadGestures()); // release-evaluated

        recognizer.PointerUp(24, 0, 0.3);
        var swipe = Single(recognizer);

        Assert.Equal(GestureKind.Swipe, swipe.Kind);
        Assert.Equal(24.0, swipe.TravelPixels, 9);
        Assert.Equal(CoinAction.Swipe, swipe.ToCoinAction());
    }

    [Fact]
    public void MovementBelowTheDragThreshold_StaysAClick()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.0);
        recognizer.PointerMove(23, 0, 0.1);
        recognizer.PointerUp(23, 0, 0.15);
        recognizer.AdvanceTo(0.45);

        var click = Single(recognizer);
        Assert.Equal(GestureKind.Click, click.Kind);
        Assert.Equal(0, click.TravelPixels);
    }

    [Fact]
    public void DragDistanceIsMeasuredFromThePressPoint_NotFromTheLastMove()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.0);
        recognizer.PointerMove(6, 0, 0.05);
        recognizer.PointerMove(12, 0, 0.10);
        recognizer.PointerMove(18, 0, 0.15);
        recognizer.PointerMove(24, 0, 0.20); // 4 px since the last move, 24 px total
        recognizer.PointerUp(24, 0, 0.25);

        Assert.Equal(GestureKind.Swipe, Single(recognizer).Kind);
    }

    // ── NoInput: a first-class action (R-1) ──────────────────────────────────────
    [Fact]
    public void NoInput_IsAFirstClassGesture_NotTheAbsenceOfOne()
    {
        var recognizer = new GestureRecognizer();

        recognizer.DeclareNoInput(1.0);
        var gesture = Single(recognizer);

        Assert.Equal(GestureKind.NoInput, gesture.Kind);
        Assert.Equal(CoinAction.None, gesture.ToCoinAction());
        Assert.Equal(1.0, gesture.PressedAtSeconds, 9);
        Assert.Equal(0.0, gesture.DurationSeconds, 9); // no press to measure

        // GDD "Ignore Coin": no reaction.
        Assert.Empty(recognizer.ReadFeedback());
    }

    [Fact]
    public void NoInput_IsRejectedWhileInputIsStillInFlight()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.0);
        Assert.Throws<InvalidOperationException>(() => recognizer.DeclareNoInput(0.1));
        recognizer.PointerUp(0, 0, 0.2);

        // The tap is still awaiting its double-click window.
        Assert.Throws<InvalidOperationException>(() => recognizer.DeclareNoInput(0.3));

        recognizer.AdvanceTo(0.6);
        recognizer.DeclareNoInput(0.6); // legal once the Click has committed

        var gestures = recognizer.ReadGestures();
        Assert.Equal(2, gestures.Length);
        Assert.Equal(GestureKind.Click, gestures[0].Kind);
        Assert.Equal(GestureKind.NoInput, gestures[1].Kind);
    }

    // ── Ambiguous timing: a second press that turns out not to be a tap ──────────
    [Fact]
    public void ASecondPressThatBecomesAHold_LeavesTheFirstTapAsAClick()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.00);
        recognizer.PointerUp(0, 0, 0.05);
        recognizer.PointerDown(0, 0, 0.20); // inside the window: candidate double click
        recognizer.PointerUp(0, 0, 0.70);   // but this press was held

        var gestures = recognizer.ReadGestures();

        Assert.Equal(2, gestures.Length);
        Assert.Equal(GestureKind.Click, gestures[0].Kind);
        Assert.Equal(0.05, gestures[0].ReleasedAtSeconds, 9);
        Assert.Equal(GestureKind.Hold, gestures[1].Kind);
        Assert.Equal(0.70, gestures[1].ReleasedAtSeconds, 9);
    }

    [Fact]
    public void ASecondPressThatBecomesASwipe_LeavesTheFirstTapAsAClick()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.00);
        recognizer.PointerUp(0, 0, 0.05);
        recognizer.PointerDown(0, 0, 0.20);
        recognizer.PointerMove(40, 0, 0.25);
        recognizer.PointerUp(40, 0, 0.30);

        var gestures = recognizer.ReadGestures();

        Assert.Equal(2, gestures.Length);
        Assert.Equal(GestureKind.Click, gestures[0].Kind);
        Assert.Equal(GestureKind.Swipe, gestures[1].Kind);
        Assert.Equal(40.0, gestures[1].TravelPixels, 9);
    }

    // ── Live provisional gesture (what R-1 accumulates mid-press) ────────────────
    [Fact]
    public void LiveGesture_IsNullWhenNoPressIsInFlight()
    {
        Assert.Null(new GestureRecognizer().LiveGesture);
    }

    [Fact]
    public void LiveGesture_FollowsTheFirstThresholdThatWasCrossed()
    {
        var dragFirst = new GestureRecognizer();
        dragFirst.PointerDown(0, 0, 0.0);
        Assert.Equal(GestureKind.Click, dragFirst.LiveGesture);
        dragFirst.PointerMove(30, 0, 0.1);
        Assert.Equal(GestureKind.Swipe, dragFirst.LiveGesture);
        dragFirst.AdvanceTo(0.9); // later hold does not reclassify the drag
        Assert.Equal(GestureKind.Swipe, dragFirst.LiveGesture);

        var holdFirst = new GestureRecognizer();
        holdFirst.PointerDown(0, 0, 0.0);
        holdFirst.AdvanceTo(0.4);
        Assert.Equal(GestureKind.Hold, holdFirst.LiveGesture);
        holdFirst.PointerMove(30, 0, 0.5);
        Assert.Equal(GestureKind.Hold, holdFirst.LiveGesture);
    }

    // ── Coin feedback hooks ──────────────────────────────────────────────────────
    [Fact]
    public void CoinFeedback_MatchesTheGddTable()
    {
        var swipe = new GestureRecognizer();
        swipe.PointerDown(0, 0, 0.0);
        swipe.PointerMove(30, 0, 0.05);
        swipe.PointerUp(30, 0, 0.1);
        Assert.Equal(new[] { CoinFeedback.Depress, CoinFeedback.FollowCursor }, swipe.ReadFeedback());

        var flip = new GestureRecognizer();
        flip.PointerDown(0, 0, 0.0);
        flip.PointerUp(0, 0, 0.05);
        flip.PointerDown(0, 0, 0.15);
        flip.PointerUp(0, 0, 0.20);
        Assert.Equal(
            new[] { CoinFeedback.Depress, CoinFeedback.Depress, CoinFeedback.Flip },
            flip.ReadFeedback());
        Assert.Equal(GestureKind.DoubleClick, Single(flip).Kind);
    }

    // ── Config: one tunable place ────────────────────────────────────────────────
    [Fact]
    public void DefaultThresholds_AreThePinnedValues()
    {
        var config = GestureConfig.Default;

        Assert.Equal(0.300, config.DoubleClickWindowSeconds);
        Assert.Equal(0.400, config.HoldThresholdSeconds);
        Assert.Equal(24.0, config.DragThresholdPixels);
    }

    [Fact]
    public void CustomThresholds_ChangeRecognition()
    {
        var config = new GestureConfig(
            doubleClickWindowSeconds: 0.5,
            holdThresholdSeconds: 1.0,
            dragThresholdPixels: 50);

        var recognizer = new GestureRecognizer(config);

        recognizer.PointerDown(0, 0, 0.0);
        recognizer.PointerMove(40, 0, 0.1);  // below the custom drag threshold
        recognizer.PointerUp(40, 0, 0.9);    // below the custom hold threshold
        recognizer.AdvanceTo(1.4);           // 0.9 + 0.5 s window

        Assert.Equal(GestureKind.Click, Single(recognizer).Kind);
    }

    [Theory]
    [InlineData(0.0, 0.4, 24.0)]
    [InlineData(0.3, 0.0, 24.0)]
    [InlineData(0.3, 0.4, 0.0)]
    public void NonPositiveThresholds_AreRejected(double window, double hold, double drag)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GestureConfig(window, hold, drag));
    }

    // ── Malformed pointer streams are loud, not silently guessed ─────────────────
    [Fact]
    public void ReleaseWithoutAPress_IsRejected()
    {
        var recognizer = new GestureRecognizer();
        Assert.Throws<InvalidOperationException>(() => recognizer.PointerUp(0, 0, 0.1));
    }

    [Fact]
    public void TwoPressesWithoutARelease_AreRejected()
    {
        var recognizer = new GestureRecognizer();
        recognizer.PointerDown(0, 0, 0.0);
        Assert.Throws<InvalidOperationException>(() => recognizer.PointerDown(0, 0, 0.1));
    }

    [Fact]
    public void TimeOnlyMovesForward()
    {
        var recognizer = new GestureRecognizer();
        recognizer.AdvanceTo(1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => recognizer.AdvanceTo(0.5));
    }

    [Fact]
    public void Reset_ClearsPendingInputAndQueues()
    {
        var recognizer = new GestureRecognizer();

        recognizer.PointerDown(0, 0, 0.0);
        recognizer.PointerUp(0, 0, 0.1);
        recognizer.Reset();

        Assert.Empty(recognizer.ReadGestures());
        Assert.Empty(recognizer.ReadFeedback());
        Assert.False(recognizer.IsPressed);

        recognizer.PointerDown(0, 0, 0.0); // clock restarted with the level
        recognizer.PointerUp(0, 0, 0.05);
        recognizer.AdvanceTo(0.35);
        Assert.Equal(GestureKind.Click, Single(recognizer).Kind);
    }

    [Theory]
    [InlineData(GestureKind.Click, CoinAction.Click)]
    [InlineData(GestureKind.DoubleClick, CoinAction.DoubleClick)]
    [InlineData(GestureKind.Hold, CoinAction.Hold)]
    [InlineData(GestureKind.Swipe, CoinAction.Swipe)]
    [InlineData(GestureKind.NoInput, CoinAction.None)]
    public void EveryGesture_MapsToItsGddCoinAction(GestureKind kind, CoinAction expected)
    {
        var gesture = new Gesture(kind, 0, 0);
        Assert.Equal(expected, gesture.ToCoinAction());
    }
}
