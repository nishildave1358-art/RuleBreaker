namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// The core game loop as a deterministic state machine — GDD principles 9–18,
/// 30–31, 43–45, and the R-1 no-input ruling.
/// </summary>
public class GameSessionLoopTests
{
    // ── helpers ────────────────────────────────────────────────────────────────
    private static Instruction ActionInstruction(string id, CoinAction action, VisualMetadata? visual = null)
        => new(id, RuleCategory.Action, "Act", new Directive(action), Visual: visual);

    private static Instruction RestrictionInstruction(string id, CoinAction forbidden)
        => new(id, RuleCategory.Restriction, "Do Not Act", new Directive(forbidden));

    private static ActiveRule TurnsRule(string id, int activatedAt, int turns)
        => ActiveRule.ForTurns(new Instruction(id, RuleCategory.Action, "Act", new Directive(CoinAction.Hold)), activatedAt, turns);

    private static ActiveRule IdentifierRule(string id, VisualMetadata predicate, int activatedAt)
        => ActiveRule.ForTurns(
            new Instruction(id, RuleCategory.Modifier, "Id", BaseDirective: null,
                ModifierSubType: ModifierSubType.Identifier, IdentifierMode: IdentifierMode.Deny, Visual: predicate),
            activatedAt, 5);

    /// <summary>Drive a whole turn with no input at all.</summary>
    private static void PlayTurn(GameSession session, Instruction instruction, CoinAction? input = null)
    {
        session.ShowInstruction(instruction);
        session.OpenResponseWindow();
        if (input is { } action) session.SubmitInput(action);
        else session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();
        session.CloseActivationWindow();
    }

    private static GameSession NewSession(
        Difficulty difficulty = Difficulty.Medium,
        double levelBase = 6.0,
        PacingMode mode = PacingMode.TurnBased,
        int turnsPerTier = SessionConfig.DefaultTurnsPerTier)
        => new(new SessionConfig(difficulty, levelBase, mode, SessionConfig.DefaultActivationWindowSeconds, turnsPerTier));

    // ── principles 9, 10: one instruction = one turn; absolute counter ──────────
    [Fact]
    public void TurnCounter_StartsAtZero_ThenCountsOnePerDisplayedInstruction()
    {
        var session = NewSession();

        Assert.Equal(TurnPhase.NotStarted, session.Phase);
        Assert.Equal(0, session.TurnNumber);

        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click));
        Assert.Equal(1, session.TurnNumber);

        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click));
        Assert.Equal(2, session.TurnNumber);

        Assert.Equal(10, PlayTenMoreTurns(session));
    }

    private static int PlayTenMoreTurns(GameSession session)
    {
        for (var i = 0; i < 8; i++)
            PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click));
        return session.TurnNumber;
    }

    [Fact]
    public void StreamingMode_KeepsOneInstructionOneTurn( )
    {
        var session = NewSession(mode: PacingMode.Streaming);

        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click));
        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click));
        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click));

        Assert.Equal(3, session.TurnNumber); // principle 9/15: turns are unchanged
        Assert.True(session.Config.Mode == PacingMode.Streaming);
    }

    // ── principle 12: turn-specific references must point at a future turn ────
    [Fact]
    public void TurnSpecificReferences_MustPointAtAFutureTurn()
    {
        var session = NewSession();
        PlayToTurn(session, 8);

        var context = session.Context!;
        Assert.Equal(8, context.TurnNumber);

        // GDD example: at Turn 8, a reference to Turn 5 is dead on arrival.
        Assert.False(context.IsFutureTurnReference(5));
        Assert.False(context.IsFutureTurnReference(8));
        Assert.True(context.IsFutureTurnReference(9));

        Assert.Throws<RuleContentException>(
            () => context.ValidateFutureTurnReference(5, "RST_NO_CLICK_3TURNS"));

        // An achievable future reference is accepted.
        context.ValidateFutureTurnReference(9, "RST_NO_CLICK_3TURNS");
    }

    private static void PlayToTurn(GameSession session, int targetTurn)
    {
        while (session.TurnNumber < targetTurn)
            PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click));
    }

    // ── the loop's phase order (plan §6 Phase 2) ───────────────────────────────
    [Fact]
    public void LoopPhases_FollowTheGddOrder()
    {
        var session = NewSession();

        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        Assert.Equal(TurnPhase.InstructionShown, session.Phase);

        session.OpenResponseWindow();
        Assert.Equal(TurnPhase.ResponseWindow, session.Phase);

        session.SubmitInput(CoinAction.Click);
        Assert.Equal(TurnPhase.Evaluation, session.Phase);

        var outcome = session.ResolveEvaluation();
        Assert.Equal(TurnPhase.AutoCardCheck, session.Phase);
        Assert.True(outcome.IsCorrect);

        session.ResolveAutoCardCheck();
        Assert.Equal(TurnPhase.ActivationWindow, session.Phase);

        session.CloseActivationWindow();
        Assert.Equal(TurnPhase.TurnComplete, session.Phase);
    }

    // ── principles 18, 44: no new instruction until both windows close ──────────
    [Fact]
    public void NewInstruction_CannotAppearWhileEitherWindowIsOpen()
    {
        var session = NewSession();

        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();

        Assert.Throws<InvalidOperationException>(() =>
            session.ShowInstruction(ActionInstruction("ACT_HOLD", CoinAction.Hold)));

        session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();

        Assert.Equal(TurnPhase.ActivationWindow, session.Phase);
        Assert.Throws<InvalidOperationException>(() =>
            session.ShowInstruction(ActionInstruction("ACT_HOLD", CoinAction.Hold)));

        session.CloseActivationWindow();
        session.ShowInstruction(ActionInstruction("ACT_HOLD", CoinAction.Hold));
        Assert.Equal(2, session.TurnNumber);
    }

    // ── principles 43, 45: the window is always present, never skipped ──────────
    [Fact]
    public void ActivationWindow_AlwaysOpens_ForEveryOutcome()
    {
        var session = NewSession();

        // Correct decision: "Do Not Click" obeyed, deliberately, by doing nothing.
        session.ShowInstruction(RestrictionInstruction("RST_NO_CLICK", CoinAction.Click));
        session.OpenResponseWindow();
        session.ConfirmNoInput();
        Assert.Equal(TurnOutcomeKind.Correct, session.ResolveEvaluation().Kind);
        session.ResolveAutoCardCheck();
        AssertWindowIsOpen(session);
        session.CloseActivationWindow();

        // Incorrect action: the wrong gesture on a turn that required Click.
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Swipe);
        Assert.Equal(TurnOutcomeKind.IncorrectAction, session.ResolveEvaluation().Kind);
        session.ResolveAutoCardCheck();
        AssertWindowIsOpen(session);
        session.CloseActivationWindow();

        // Timer expiry with a required action (R-1).
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();
        session.Advance(6.0);
        Assert.Equal(TurnOutcomeKind.TimerExpiry, session.ResolveEvaluation().Kind);
        session.ResolveAutoCardCheck();
        AssertWindowIsOpen(session);
    }

    private static void AssertWindowIsOpen(GameSession session)
    {
        Assert.Equal(TurnPhase.ActivationWindow, session.Phase);
        Assert.True(session.ActivationWindowTotalSeconds > 0, "the between-turns window is never zero-length");
        Assert.True(session.ActivationWindowRemainingSeconds > 0, "the between-turns window is never skipped");
    }

    [Fact]
    public void ManualRuleActivation_OnlyInsideItsWindowOrWithTheInstruction()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));

        // Allowed: the instruction enters the rule set (loop step 4).
        session.ActivateRule(TurnsRule("ACT_HOLD_3", 1, 3));

        session.OpenResponseWindow();

        // Forbidden: principle 43 — manual timing is the activation window only.
        Assert.Throws<InvalidOperationException>(() => session.ActivateRule(TurnsRule("ACT_HOLD_4", 1, 3)));

        session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();

        // Allowed again: the between-turns activation window is open.
        session.ActivateRule(TurnsRule("ACT_HOLD_5", 1, 3));
        Assert.True(session.Rules.IsInstructionActive("ACT_HOLD_5"));
    }

    // ── principles 9, 11: the countdown ticks once per displayed instruction ────
    [Fact]
    public void RuleCountdown_TicksOncePerInstruction_ThenExpiresPermanently()
    {
        var session = NewSession();

        // Turns 1-2 pass with nothing active.
        ShowAndFinishTurn(session);
        ShowAndFinishTurn(session);

        // Turn 3: "Do Not Click For 3 Turns" is displayed and activates.
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        Assert.Equal(3, session.TurnNumber);
        session.ActivateRule(TurnsRule("RST_NO_CLICK_3TURNS", activatedAt: session.TurnNumber, turns: 3));
        Assert.Equal(3, RemainingTurnsOf(session, session.TurnNumber)); // GDD: 3 remaining
        FinishTurn(session);

        // Turn 4: 2 remaining.
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        Assert.Equal(4, session.TurnNumber);
        Assert.Equal(2, RemainingTurnsOf(session, session.TurnNumber));
        FinishTurn(session);

        // Turn 5: 1 remaining and still live.
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        Assert.Equal(5, session.TurnNumber);
        Assert.Equal(1, RemainingTurnsOf(session, session.TurnNumber));
        Assert.Empty(session.LastTick!.JustExpired);
        FinishTurn(session);

        // Turn 6: expired and permanently removed (principles 4, 11).
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        Assert.Equal(6, session.TurnNumber);
        Assert.Empty(session.Rules.OrderedRules);
        Assert.Single(session.LastTick!.JustExpired);
    }

    private static void ShowAndFinishTurn(GameSession session)
    {
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        FinishTurn(session);
    }

    private static void FinishTurn(GameSession session)
    {
        session.OpenResponseWindow();
        session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();
        session.CloseActivationWindow();
    }

    private static int RemainingTurnsOf(GameSession session, int turn) =>
        session.Rules.OrderedRules.Single().RemainingTurns(turn)!.Value;

    // ── principles 30, 31: the timer is level base × difficulty ────────────────
    [Theory]
    [InlineData(Difficulty.Easy, 9.0)]
    [InlineData(Difficulty.Medium, 6.0)]
    [InlineData(Difficulty.Hard, 4.2)]
    [InlineData(Difficulty.Nightmare, 3.0)]
    public void ResponseWindow_UsesLevelBaseTimesDifficulty(Difficulty difficulty, double expected)
    {
        var session = NewSession(difficulty, levelBase: 6.0);
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();

        Assert.Equal(expected, session.Timer.TotalSeconds);
        Assert.Equal(expected, session.Timer.RemainingSeconds);
    }

    // ── principles 14, 15: streaming compresses; escalation raises the tier ────
    [Fact]
    public void StreamingTimers_CompressAsTheEscalationTierRises()
    {
        var session = NewSession(mode: PacingMode.Streaming, turnsPerTier: 20);

        Assert.Equal(5.0, TimerOnTurn(session, 1));   // Slow
        Assert.Equal(5.0, TimerOnTurn(session, 20));  // still Slow
        Assert.Equal(3.0, TimerOnTurn(session, 21));  // Medium
        Assert.Equal(3.0, TimerOnTurn(session, 40));
        Assert.Equal(2.0, TimerOnTurn(session, 41));  // Fast
        Assert.Equal(2.0, TimerOnTurn(session, 60));
        Assert.Equal(1.0, TimerOnTurn(session, 61));  // Maximum
    }

    /// <summary>Play turns until the given turn, returning that turn's response-window length.</summary>
    private static double TimerOnTurn(GameSession session, int targetTurn)
    {
        double total = 0;
        while (session.TurnNumber < targetTurn)
        {
            session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
            session.OpenResponseWindow();
            total = session.Timer.TotalSeconds;
            session.ConfirmNoInput();
            session.ResolveEvaluation();
            session.ResolveAutoCardCheck();
            session.CloseActivationWindow();
        }

        return total;
    }

    [Fact]
    public void StreamingActivationWindow_CompressesProportionally_AndIsNeverZero()
    {
        // Turn-based play does not compress: the window keeps its designed base.
        var turnBased = NewSession(levelBase: 6.0);
        Assert.Equal(SessionConfig.DefaultActivationWindowSeconds, WindowOnTurn(turnBased, 1));

        // Streaming: a 6 s level base on Fast (turn 41, 2 s compressed base)
        // compresses the window by the same 2/6 ratio as the response window.
        var fast = NewSession(mode: PacingMode.Streaming, levelBase: 6.0, turnsPerTier: 20);
        Assert.Equal(
            TimerTable.Round(SessionConfig.DefaultActivationWindowSeconds * (2.0 / 6.0)),
            WindowOnTurn(fast, 41));

        // Even a pathological level base leaves a non-zero window (principles 14, 45).
        var hugeBase = NewSession(mode: PacingMode.Streaming, levelBase: 1000.0, turnsPerTier: 20);
        Assert.True(WindowOnTurn(hugeBase, 41) > 0);
    }

    /// <summary>Play turns until the given turn, returning that turn's activation-window length.</summary>
    private static double WindowOnTurn(GameSession session, int targetTurn)
    {
        double total = 0;
        while (session.TurnNumber < targetTurn)
        {
            session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
            session.OpenResponseWindow();
            session.ConfirmNoInput();
            session.ResolveEvaluation();
            session.ResolveAutoCardCheck();
            total = session.ActivationWindowTotalSeconds;
            session.CloseActivationWindow();
        }

        return total;
    }

    [Fact]
    public void ActivationWindow_ClosesWhenItsTimeRunsOut()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();
        session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();

        Assert.Equal(TurnPhase.ActivationWindow, session.Phase);

        session.Advance(SessionConfig.DefaultActivationWindowSeconds);

        Assert.Equal(TurnPhase.TurnComplete, session.Phase);
        Assert.Equal(0, session.ActivationWindowRemainingSeconds);
    }

    // ── R-1: expiry resolves the accumulated input ─────────────────────────────
    [Fact]
    public void Expiry_WithNoActDirectiveAndNoInput_IsACorrectDecision()
    {
        var session = NewSession();
        session.ShowInstruction(RestrictionInstruction("RST_NO_CLICK", CoinAction.Click));
        session.OpenResponseWindow();

        session.Advance(6.0); // the full window elapses — intended tension (R-1)

        var outcome = session.ResolveEvaluation();

        Assert.Equal(CoinAction.None, outcome.RequiredAction);
        Assert.Equal(TurnOutcomeKind.Correct, outcome.Kind);
        Assert.False(outcome.AutoCardCheckRequired);
        Assert.False(session.AutoCardCheckPending);
    }

    [Fact]
    public void Expiry_WithARequiredUnperformedAction_IsATimerExpiryFailure()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();

        session.Advance(6.0);

        var outcome = session.ResolveEvaluation();

        Assert.Equal(CoinAction.Click, outcome.RequiredAction);
        Assert.Equal(TurnOutcomeKind.TimerExpiry, outcome.Kind);
        Assert.True(outcome.AutoCardCheckRequired);
        Assert.True(session.AutoCardCheckPending);
    }

    [Fact]
    public void Expiry_WithAPartialInputThatNoLongerMatches_IsATimerExpiryFailure()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_HOLD", CoinAction.Hold));
        session.OpenResponseWindow();

        session.RecordInput(CoinAction.Click); // recognised input, never committed
        session.Advance(6.0);

        var outcome = session.ResolveEvaluation();

        Assert.Equal(TurnOutcomeKind.TimerExpiry, outcome.Kind);
        Assert.Equal(CoinAction.Click, outcome.FinalAction);
    }

    [Fact]
    public void WrongAction_IsAnIncorrectActionFailure()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_HOLD", CoinAction.Hold));
        session.OpenResponseWindow();

        session.SubmitInput(CoinAction.Click);

        var outcome = session.ResolveEvaluation();

        Assert.Equal(TurnOutcomeKind.IncorrectAction, outcome.Kind);
        Assert.Equal(CoinAction.Hold, outcome.RequiredAction);
        Assert.True(outcome.AutoCardCheckRequired);
    }

    [Fact]
    public void CorrectAction_DoesNotRequestAnAutoCardCheck()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();

        session.SubmitInput(CoinAction.Click);

        var outcome = session.ResolveEvaluation();

        Assert.Equal(TurnOutcomeKind.Correct, outcome.Kind);
        Assert.False(outcome.AutoCardCheckRequired);
        Assert.Equal(SourceTrace.Real, outcome.SourceTrace);
    }

    // ── principle 16: expiry is penalised identically on every tier ────────────
    [Fact]
    public void TimerExpiry_IsIdenticalOnEveryDifficultyTier()
    {
        foreach (var difficulty in DifficultyRules.All)
        {
            var session = NewSession(difficulty);
            session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
            session.OpenResponseWindow();

            session.Advance(100.0); // any amount past the window

            var outcome = session.ResolveEvaluation();

            Assert.Equal(TurnOutcomeKind.TimerExpiry, outcome.Kind);
            Assert.True(outcome.AutoCardCheckRequired);
        }
    }

    // ── principle 25: source tracing flows through the turn outcome ────────────
    [Fact]
    public void WrongAction_OnAVisibleFake_TracesToFakeOrigin()
    {
        var session = NewSession();

        // Turn 1: an "Ignore Flashing Instructions" identifier card activates.
        session.ShowInstruction(new Instruction("MOD_IGNORE_FLASHING", RuleCategory.Modifier, "Id",
            BaseDirective: null, ModifierSubType: ModifierSubType.Identifier,
            IdentifierMode: IdentifierMode.Deny, Visual: new VisualMetadata(Flashing: true)));
        session.ActivateRule(IdentifierRule("MOD_IGNORE_FLASHING", new VisualMetadata(Flashing: true), 1));
        session.OpenResponseWindow();
        session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();
        session.CloseActivationWindow();

        // Turn 2: a flashing card is fake — clicking it is a Source B mistake.
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(Flashing: true)));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Click);

        var outcome = session.ResolveEvaluation();

        Assert.True(session.Resolution!.IsFake);
        Assert.Equal(TurnOutcomeKind.IncorrectAction, outcome.Kind);
        Assert.Equal(SourceTrace.FakeOrigin, outcome.SourceTrace);
    }

    [Fact]
    public void WrongAction_OnARealInstruction_TracesToReal()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_HOLD", CoinAction.Hold));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Swipe);

        var outcome = session.ResolveEvaluation();

        Assert.False(session.Resolution!.IsMechanicallyVisibleFake);
        Assert.Equal(SourceTrace.Real, outcome.SourceTrace);
    }

    // ── step 15 hook: the absorbing card is recorded for Phase 5/6 ─────────────
    [Fact]
    public void AutoCardCheck_RecordsAnAbsorbingCard()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();
        session.Advance(6.0);
        session.ResolveEvaluation();

        Assert.True(session.AutoCardCheckPending);

        session.ResolveAutoCardCheck(absorbedByCardId: "CARD_SHIELD");

        Assert.False(session.AutoCardCheckPending);
        Assert.Equal("CARD_SHIELD", session.Outcome!.AbsorbedByCardId);
        Assert.True(session.Outcome.IsFailure); // the penalty decision itself belongs to Phase 5
    }

    // ── R-2: pause freezes timers, windows and countdowns ─────────────────────
    [Fact]
    public void Pause_FreezesTheResponseTimerAndTheActivationWindow()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();

        session.Advance(1.0);
        var remaining = session.Timer.RemainingSeconds;

        session.Pause();
        session.Advance(5.0);
        Assert.Equal(remaining, session.Timer.RemainingSeconds);
        Assert.Equal(TurnPhase.ResponseWindow, session.Phase);

        session.Resume();
        session.Advance(1.0);
        Assert.Equal(remaining - 1.0, session.Timer.RemainingSeconds);

        // Countdowns are turn-driven, so pausing cannot skip a turn either.
        Assert.Equal(1, session.TurnNumber);
    }

    // ── guards: windows never overlap, steps stay in order ────────────────────
    [Fact]
    public void Windows_CannotOverlapOrBeDrivenOutOfOrder()
    {
        var session = NewSession();

        Assert.Throws<InvalidOperationException>(() => session.OpenResponseWindow());

        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));

        Assert.Throws<InvalidOperationException>(() => session.SubmitInput(CoinAction.Click));
        Assert.Throws<InvalidOperationException>(() => session.ResolveEvaluation());
        Assert.Throws<InvalidOperationException>(() => session.CloseActivationWindow());

        session.OpenResponseWindow();
        Assert.Throws<InvalidOperationException>(() => session.OpenResponseWindow());

        session.ConfirmNoInput();
        Assert.Throws<InvalidOperationException>(() => session.ResolveAutoCardCheck());
    }

    // ── configuration guards ──────────────────────────────────────────────────
    [Fact]
    public void SessionConfig_RejectsValuesThatBreakGddGuarantees()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionConfig(levelBaseSeconds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionConfig(activationWindowSeconds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionConfig(turnsPerTier: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamingPacing(turnsPerTier: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamingPacing().SpeedForTurn(0));
    }

    // ── difficulty table sanity through the session ───────────────────────────
    [Fact]
    public void TurnContext_CarriesTheTurnItsDifficultyAndItsPacing()
    {
        var session = NewSession(Difficulty.Hard, levelBase: 4.0, mode: PacingMode.TurnBased);
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));

        Assert.Equal(1, session.Context!.TurnNumber);
        Assert.Equal(Difficulty.Hard, session.Context.Difficulty);
        Assert.Equal(PacingMode.TurnBased, session.Context.Mode);
        Assert.Null(session.Context.StreamingSpeed);

        var streaming = NewSession(mode: PacingMode.Streaming);
        PlayTurn(streaming, ActionInstruction("ACT_CLICK", CoinAction.Click));
        streaming.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));

        Assert.Equal(StreamingSpeed.Slow, streaming.Context!.StreamingSpeed);
        Assert.True(streaming.Context.IsStreaming);
    }
}
