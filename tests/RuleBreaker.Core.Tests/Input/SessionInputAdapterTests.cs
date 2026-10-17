namespace RuleBreaker.Core.Tests.Input;

using RuleBreaker.Core.Input;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// Mouse-only gameplay end to end: recognised gestures drive the Phase 2 loop,
/// and R-1 sees an in-flight press through the accumulated-input path.
/// </summary>
public class SessionInputAdapterTests
{
    private static Instruction InstructionFor(CoinAction action) =>
        new($"act_{action}", RuleCategory.Action, "Act", new Directive(action));

    private static void BeginTurn(GameSession session, CoinAction action)
    {
        session.ShowInstruction(InstructionFor(action));
        session.OpenResponseWindow();
    }

    private static void FinishTurn(GameSession session)
    {
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();
        session.CloseActivationWindow();
    }

    [Fact]
    public void Click_IsRecordedWhileInFlight_ThenSubmittedWhenItIsUnambiguouslyAClick()
    {
        var session = new GameSession();
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Click);
        adapter.PointerDown(100, 100, 0.0);

        // In flight: recorded so an expiry still sees it (R-1), but the turn is open.
        Assert.Equal(TurnPhase.ResponseWindow, session.Phase);
        Assert.Equal(CoinAction.Click, session.AccumulatedInput);

        adapter.PointerUp(100, 100, 0.05);
        Assert.Equal(TurnPhase.ResponseWindow, session.Phase);

        adapter.AdvanceTo(0.36); // 0.05 + 300 ms
        Assert.Equal(TurnPhase.Evaluation, session.Phase);

        var outcome = session.ResolveEvaluation();
        Assert.True(outcome.IsCorrect);
        Assert.Equal(CoinAction.Click, outcome.FinalAction);
    }

    [Fact]
    public void DoubleClick_SubmitsDoubleClick()
    {
        var session = new GameSession();
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.DoubleClick);
        adapter.PointerDown(100, 100, 0.0);
        adapter.PointerUp(100, 100, 0.05);
        adapter.PointerDown(100, 100, 0.20);
        adapter.PointerUp(100, 100, 0.25);

        Assert.Equal(TurnPhase.Evaluation, session.Phase);
        Assert.True(session.ResolveEvaluation().IsCorrect);
        Assert.Equal(CoinAction.DoubleClick, session.AccumulatedInput);
    }

    [Fact]
    public void Hold_SubmitsHold()
    {
        var session = new GameSession();
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Hold);
        adapter.PointerDown(100, 100, 0.0);
        adapter.PointerUp(100, 100, 0.5);

        Assert.Equal(TurnPhase.Evaluation, session.Phase);
        Assert.True(session.ResolveEvaluation().IsCorrect);
        Assert.Equal(CoinAction.Hold, session.AccumulatedInput);
    }

    [Fact]
    public void Swipe_SubmitsSwipe()
    {
        var session = new GameSession();
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Swipe);
        adapter.PointerDown(0, 0, 0.0);
        adapter.PointerMove(40, 0, 0.1);
        adapter.PointerUp(40, 0, 0.15);

        Assert.Equal(TurnPhase.Evaluation, session.Phase);
        Assert.True(session.ResolveEvaluation().IsCorrect);
    }

    [Fact]
    public void DeliberateNoInput_SubmitsTheNoInputAction()
    {
        var session = new GameSession();
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.None);
        adapter.DeclareNoInput(0.2);

        Assert.Equal(TurnPhase.Evaluation, session.Phase);
        var outcome = session.ResolveEvaluation();
        Assert.True(outcome.IsCorrect);
        Assert.Equal(CoinAction.None, outcome.FinalAction);
    }

    [Fact]
    public void ExpiryDuringAnInFlightClick_EvaluatesTheAccumulatedInput()
    {
        // Short level base so the window closes while the press is still down.
        var session = new GameSession(new SessionConfig(levelBaseSeconds: 0.3));
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Click);
        adapter.PointerDown(100, 100, 0.0);

        session.Advance(0.31);
        Assert.Equal(TurnPhase.Evaluation, session.Phase);

        var outcome = session.ResolveEvaluation();
        Assert.Equal(TurnOutcomeKind.Correct, outcome.Kind);
        Assert.Equal(CoinAction.Click, outcome.FinalAction);
    }

    [Fact]
    public void ExpiryDuringAHold_EvaluatesTheAccumulatedHold()
    {
        var session = new GameSession(new SessionConfig(levelBaseSeconds: 0.6));
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Hold);
        adapter.PointerDown(100, 100, 0.0);
        session.Advance(0.45);
        adapter.AdvanceTo(0.45); // hold threshold crossed while still down

        session.Advance(0.2); // window expires mid-press
        Assert.Equal(TurnPhase.Evaluation, session.Phase);

        var outcome = session.ResolveEvaluation();
        Assert.Equal(TurnOutcomeKind.Correct, outcome.Kind);
        Assert.Equal(CoinAction.Hold, outcome.FinalAction);
    }

    [Fact]
    public void ExpiryDuringADrag_EvaluatesTheAccumulatedSwipe()
    {
        var session = new GameSession(new SessionConfig(levelBaseSeconds: 0.6));
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Swipe);
        adapter.PointerDown(0, 0, 0.0);
        adapter.PointerMove(40, 0, 0.1);

        session.Advance(0.65);
        Assert.Equal(CoinAction.Swipe, session.AccumulatedInput);

        var outcome = session.ResolveEvaluation();
        Assert.Equal(TurnOutcomeKind.Correct, outcome.Kind);
        Assert.Equal(CoinAction.Swipe, outcome.FinalAction);
    }

    [Fact]
    public void ExpiryWithARequiredActionAndNoInput_IsATimerExpiryFailure()
    {
        var session = new GameSession(new SessionConfig(levelBaseSeconds: 0.3));
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Click);

        session.Advance(0.31);
        var outcome = session.ResolveEvaluation();

        Assert.Equal(TurnOutcomeKind.TimerExpiry, outcome.Kind);
        Assert.Null(outcome.FinalAction);
        Assert.False(adapter.AppliedGestures.Any());
    }

    [Fact]
    public void GesturesOutsideTheResponseWindow_AreNotSubmitted()
    {
        var session = new GameSession();
        var adapter = new SessionInputAdapter(session);

        BeginTurn(session, CoinAction.Click);
        adapter.PointerDown(100, 100, 0.0);
        adapter.PointerUp(100, 100, 0.05);
        adapter.AdvanceTo(0.36);
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();

        Assert.Equal(TurnPhase.ActivationWindow, session.Phase);

        // A click on a card slot belongs to the card UI (Phase 6), not the loop.
        adapter.PointerDown(300, 300, 1.0);
        adapter.PointerUp(300, 300, 1.05);
        adapter.AdvanceTo(1.4);

        Assert.Equal(1, adapter.DroppedGestureCount);
        Assert.Equal(TurnPhase.ActivationWindow, session.Phase);
        Assert.Single(adapter.AppliedGestures);
    }

    [Fact]
    public void CoreGameplayRunsOnMouseAlone_ForATurnOfEveryGestureType()
    {
        var session = new GameSession();
        var adapter = new SessionInputAdapter(session);

        // Turn 1 — click.
        BeginTurn(session, CoinAction.Click);
        adapter.PointerDown(100, 100, 0.00);
        adapter.PointerUp(100, 100, 0.05);
        adapter.AdvanceTo(0.36);
        FinishTurn(session);

        // Turn 2 — double click.
        BeginTurn(session, CoinAction.DoubleClick);
        adapter.PointerDown(100, 100, 0.50);
        adapter.PointerUp(100, 100, 0.55);
        adapter.PointerDown(100, 100, 0.65);
        adapter.PointerUp(100, 100, 0.70);
        FinishTurn(session);

        // Turn 3 — hold.
        BeginTurn(session, CoinAction.Hold);
        adapter.PointerDown(100, 100, 1.00);
        adapter.PointerUp(100, 100, 1.50);
        FinishTurn(session);

        // Turn 4 — swipe.
        BeginTurn(session, CoinAction.Swipe);
        adapter.PointerDown(0, 0, 1.60);
        adapter.PointerMove(30, 0, 1.70);
        adapter.PointerUp(30, 0, 1.75);
        FinishTurn(session);

        Assert.Equal(4, session.TurnNumber);
        Assert.Equal(4, adapter.AppliedGestures.Count);
        Assert.Equal(
            new[] { GestureKind.Click, GestureKind.DoubleClick, GestureKind.Hold, GestureKind.Swipe },
            adapter.AppliedGestures.Select(g => g.Kind).ToArray());

        // Coin feedback hooks fired for presentation to consume.
        var feedback = adapter.ReadFeedback();
        Assert.Contains(CoinFeedback.Depress, feedback);
        Assert.Contains(CoinFeedback.Flip, feedback);
        Assert.Contains(CoinFeedback.Glow, feedback);
        Assert.Contains(CoinFeedback.FollowCursor, feedback);
    }

    [Fact]
    public void SessionsCanBeDrivenFromTheRecognizerAlone_WhenTheCallerOwnsPumping()
    {
        var session = new GameSession();
        var recognizer = new GestureRecognizer();
        var adapter = new SessionInputAdapter(session, recognizer);

        Assert.Same(recognizer, adapter.Recognizer);

        BeginTurn(session, CoinAction.Hold);
        adapter.PointerDown(10, 10, 0.0);
        adapter.PointerUp(10, 10, 0.45);

        Assert.Equal(TurnPhase.Evaluation, session.Phase);
        Assert.Equal(GestureKind.Hold, adapter.AppliedGestures[0].Kind);
    }
}
