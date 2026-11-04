namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// The run state Phase 5 adds: lives, game over, auto-card absorption, the
/// one-settlement-per-turn contract, the HUD snapshot, and the session-level
/// acceptance cases (principle 24, Easy/Medium/Life penalty rows).
/// </summary>
public class RunStateTests
{
    // ── helpers ────────────────────────────────────────────────────────────────
    private static TurnOutcome Correct(int turn) => new(
        TurnOutcomeKind.Correct, turn, "ACT_CLICK", CoinAction.Click, CoinAction.Click,
        SourceTrace.Real, AutoCardCheckRequired: false);

    private static TurnOutcome Wrong(int turn, SourceTrace source = SourceTrace.Real) => new(
        TurnOutcomeKind.IncorrectAction, turn, "ACT_CLICK", CoinAction.Click, CoinAction.Swipe,
        source, AutoCardCheckRequired: true);

    private static TurnOutcome Expired(int turn) => new(
        TurnOutcomeKind.TimerExpiry, turn, "ACT_CLICK", CoinAction.Click, null,
        SourceTrace.Real, AutoCardCheckRequired: true);

    private static RunState NewRun(Difficulty difficulty = Difficulty.Medium, int startingLives = RunState.DefaultStartingLives)
        => new(difficulty, startingLives);

    private static Instruction ActionInstruction(string id, CoinAction action, VisualMetadata? visual = null)
        => new(id, RuleCategory.Action, "Act", new Directive(action), Visual: visual);

    private static Instruction IdentifierInstruction()
        => new("MOD_IGNORE_FLASHING", RuleCategory.Modifier, "Ignore Flashing Instructions",
            BaseDirective: null, ModifierSubType: ModifierSubType.Identifier,
            IdentifierMode: IdentifierMode.Deny, Visual: new VisualMetadata(Flashing: true));

    private static ActiveRule DenyFlashingRule(int activatedAt)
        => ActiveRule.ForTurns(IdentifierInstruction(), activatedAt, 5);

    private static GameSession NewSession(Difficulty difficulty = Difficulty.Medium)
        => new(new SessionConfig(difficulty));

    private static void PlayTurn(
        GameSession session,
        Instruction instruction,
        CoinAction? input,
        string? absorbedByCardId = null)
    {
        session.ShowInstruction(instruction);
        session.OpenResponseWindow();
        if (input is { } action) session.SubmitInput(action);
        else session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck(absorbedByCardId);
        session.CloseActivationWindow();
    }

    /// <summary>Turn 1: an "Ignore Flashing Instructions" identifier is activated and the player does nothing.</summary>
    private static void PlayIdentifierTurn(GameSession session)
    {
        session.ShowInstruction(IdentifierInstruction());
        session.ActivateRule(DenyFlashingRule(session.TurnNumber));
        session.OpenResponseWindow();
        session.ConfirmNoInput();
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();
        session.CloseActivationWindow();
    }

    // ── the penalty matrix's effect on lives and combo ─────────────────────────
    [Theory]
    [InlineData(Difficulty.Easy, 3, 1)]      // no penalty, no reset
    [InlineData(Difficulty.Medium, 3, 0)]    // combo reset only
    [InlineData(Difficulty.Hard, 2, 0)]      // full penalty
    [InlineData(Difficulty.Nightmare, 2, 0)] // full penalty
    public void FakeInstructionMistake_CostsExactlyWhatItsTierSays(Difficulty difficulty, int lives, int combo)
    {
        var run = NewRun(difficulty);
        run.Settle(Correct(1));
        Assert.Equal(1, run.ConsecutiveCorrect);

        var settlement = run.Settle(Wrong(2, SourceTrace.FakeOrigin));

        Assert.Equal(lives, run.Lives);
        Assert.Equal(combo, run.ConsecutiveCorrect);
        Assert.Equal(PenaltyRules.FakeInstructionMistake(difficulty), settlement.Penalty);
    }

    [Fact]
    public void RealInstructionMistake_CostsALifeAndTheCombo_OnEveryTier()
    {
        foreach (var difficulty in DifficultyRules.All)
        {
            var run = NewRun(difficulty);
            run.Settle(Correct(1));

            var settlement = run.Settle(Wrong(2));

            Assert.Equal(PenaltyKind.Full, settlement.Penalty);
            Assert.Equal(2, run.Lives);
            Assert.Equal(0, run.ConsecutiveCorrect);
        }
    }

    [Fact]
    public void TimerExpiry_CostsALifeAndTheCombo_OnEveryTier()
    {
        foreach (var difficulty in DifficultyRules.All)
        {
            var run = NewRun(difficulty);
            run.Settle(Correct(1));

            var settlement = run.Settle(Expired(2));

            Assert.Equal(PenaltyKind.Full, settlement.Penalty);
            Assert.Equal(2, run.Lives);
            Assert.Equal(0, run.ConsecutiveCorrect);
        }
    }

    // ── auto-card absorption (principles 41–42, R-8, 46) ───────────────────────
    [Fact]
    public void AbsorbedFailure_LeavesLivesAndComboUntouched_AndPaysNothing()
    {
        var run = NewRun(Difficulty.Hard);
        run.Settle(Correct(1));
        var scoreBefore = run.Score;

        var settlement = run.Settle(Wrong(2), absorbedByCardId: "CARD_SHIELD");

        Assert.True(settlement.AbsorbedByAutoCard);
        Assert.False(settlement.AbsorbedRedundantPenalty);
        Assert.Equal("CARD_SHIELD", settlement.AbsorbedByCardId);
        Assert.Equal(PenaltyKind.None, settlement.Penalty);
        Assert.Equal(PenaltyKind.Full, settlement.PenaltyBeforeAbsorption);
        Assert.Equal(3, run.Lives);
        Assert.Equal(1, run.ConsecutiveCorrect);
        Assert.Equal(scoreBefore, run.Score);
        Assert.Equal(0, settlement.ScoreAwarded);
    }

    [Fact]
    public void AbsorptionOnAnEasyFakeMistake_IsAWastedActivation()
    {
        // Principle 46: on Easy there was no penalty to absorb, but the card is
        // still consumed — the settlement records exactly that.
        var run = NewRun(Difficulty.Easy);
        run.Settle(Correct(1));

        var settlement = run.Settle(Wrong(2, SourceTrace.FakeOrigin), absorbedByCardId: "CARD_JOKER");

        Assert.True(settlement.AbsorbedByAutoCard);
        Assert.True(settlement.AbsorbedRedundantPenalty);
        Assert.Equal(PenaltyKind.None, settlement.Penalty);
        Assert.Equal(PenaltyKind.None, settlement.PenaltyBeforeAbsorption);
        Assert.Equal(3, run.Lives);
        Assert.Equal(1, run.ConsecutiveCorrect);
    }

    [Fact]
    public void UnabsorbedFailure_RecordsNoCard()
    {
        var run = NewRun();

        var settlement = run.Settle(Wrong(1));

        Assert.False(settlement.AbsorbedByAutoCard);
        Assert.False(settlement.AbsorbedRedundantPenalty);
        Assert.Null(settlement.AbsorbedByCardId);
    }

    // ── lives and game over ────────────────────────────────────────────────────
    [Fact]
    public void ThreeFullPenalties_EndTheRun()
    {
        var run = NewRun();

        run.Settle(Wrong(1));
        run.Settle(Wrong(2));
        Assert.False(run.IsGameOver);
        Assert.Equal(1, run.Lives);

        var third = run.Settle(Wrong(3));

        Assert.True(third.IsGameOver);
        Assert.True(third.EndedRun);
        Assert.True(run.IsGameOver);
        Assert.Equal(0, run.Lives);
        Assert.Equal(3, run.SpentLives);

        // A further settlement can still be recorded (the shell owns stopping the
        // run), but lives never go negative.
        run.Settle(Wrong(4));
        Assert.Equal(0, run.Lives);
        Assert.Equal(3, run.SpentLives);
    }

    [Fact]
    public void AOneLifeRun_EndsOnItsFirstFullPenalty()
    {
        // Hardcore is Phase 7, but the run state already supports its 1-life rule.
        var run = NewRun(Difficulty.Nightmare, startingLives: 1);

        var settlement = run.Settle(Expired(1));

        Assert.True(run.IsGameOver);
        Assert.True(settlement.EndedRun);
        Assert.Equal(0, run.Lives);
        Assert.Equal(1, run.MaxLives);
    }

    [Fact]
    public void StartingLives_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunState(Difficulty.Medium, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunState(Difficulty.Medium, -1));
    }

    [Fact]
    public void ATurn_IsSettledExactlyOnce()
    {
        var run = NewRun();
        run.Settle(Expired(1));

        Assert.Throws<InvalidOperationException>(() => run.Settle(Expired(1)));
    }

    // ── session integration: the phase-5 penalty flow ──────────────────────────
    [Fact]
    public void Session_SettlesOnlyAfterTheAutoCardCheck()
    {
        var session = NewSession();
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Swipe);

        var outcome = session.ResolveEvaluation();

        Assert.Equal(TurnOutcomeKind.IncorrectAction, outcome.Kind);
        Assert.Null(session.LastSettlement); // step 15 has not run yet
        Assert.Equal(3, session.Run.Lives);

        session.ResolveAutoCardCheck();

        var settlement = session.LastSettlement!;
        Assert.Equal(PenaltyKind.Full, settlement.Penalty);
        Assert.Equal(2, session.Run.Lives);
        Assert.Equal(0, session.Run.ConsecutiveCorrect);
        Assert.Equal(TurnOutcomeKind.IncorrectAction, settlement.Kind);
        Assert.Equal(SourceTrace.Real, settlement.SourceTrace);
    }

    [Fact]
    public void Session_SettlesAFailureAbsorbedByAnAutoCard()
    {
        var session = NewSession(Difficulty.Hard);
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click));
        session.OpenResponseWindow();
        session.Advance(100.0); // timer expiry — Shield is the only card that may fire
        session.ResolveEvaluation();

        Assert.Equal(AutoCardTrigger.Shield, AutoCardRules.FirstEligible(session.Outcome!.Kind));

        session.ResolveAutoCardCheck(absorbedByCardId: "CARD_SHIELD");

        Assert.Equal(PenaltyKind.None, session.LastSettlement!.Penalty);
        Assert.Equal(PenaltyKind.Full, session.LastSettlement.PenaltyBeforeAbsorption);
        Assert.True(session.LastSettlement.AbsorbedByAutoCard);
        Assert.Equal(3, session.Run.Lives);
        Assert.Equal("CARD_SHIELD", session.Outcome!.AbsorbedByCardId);
    }

    [Fact]
    public void Session_FiveCorrectDecisions_PayTheTwoTimesCombo()
    {
        var session = NewSession();

        for (var i = 0; i < 5; i++)
            PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click), CoinAction.Click);

        Assert.Equal(600, session.Run.Score);
        Assert.Equal(5, session.Run.ConsecutiveCorrect);
        Assert.Equal(2, session.Run.Multiplier);
        Assert.Equal(3, session.Run.Lives);
    }

    [Fact]
    public void Session_HudSnapshot_CarriesThePhase5State()
    {
        var session = NewSession(Difficulty.Medium);
        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click), CoinAction.Click);

        var hud = session.Hud;

        Assert.Equal(100, hud.Score);
        Assert.Equal(3, hud.Lives);
        Assert.Equal(3, hud.MaxLives);
        Assert.Equal(0, hud.SpentLives);
        Assert.Equal(1, hud.ComboCount);
        Assert.Equal(1, hud.Multiplier);
        Assert.Equal(ComboMilestone.None, hud.LastMilestone);
        Assert.Equal(PenaltyKind.None, hud.LastPenalty);
        Assert.False(hud.LastPenaltyAbsorbed);
        Assert.Equal(1, hud.TurnNumber);
        Assert.Equal(Difficulty.Medium, hud.Difficulty);
        Assert.Equal(PacingMode.TurnBased, hud.Mode);
        Assert.Null(hud.StreamingSpeed);
        Assert.Equal(6.0, hud.TimerRemainingSeconds);
        Assert.Equal(6.0, hud.TimerTotalSeconds);
        Assert.Equal(1.0, hud.TimerFractionRemaining);
        Assert.Equal(0, hud.ActiveRuleCount);
        Assert.False(hud.IsGameOver);
        Assert.False(hud.IsPaused);
    }

    [Fact]
    public void Session_HudSnapshot_IsAnImmutableRead()
    {
        var session = NewSession();
        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click), CoinAction.Click);
        var firstTurn = session.Hud;

        PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click), CoinAction.Click);

        Assert.Equal(1, firstTurn.TurnNumber); // the old snapshot did not move
        Assert.Equal(100, firstTurn.Score);
        Assert.Equal(2, session.Hud.TurnNumber);
        Assert.Equal(200, session.Hud.Score);
    }

    [Fact]
    public void Session_HudSnapshot_CountsActiveRulesForTheLeftPanel()
    {
        var session = NewSession();
        PlayIdentifierTurn(session);

        Assert.Equal(1, session.Hud.ActiveRuleCount);
    }

    [Fact]
    public void Session_EasyFakeMistake_CostsNothing_AndKeepsTheCombo()
    {
        var session = NewSession(Difficulty.Easy);
        PlayIdentifierTurn(session); // turn 1 correct, combo 1, score 100
        Assert.Equal(100, session.Run.Score);

        // Turn 2: a flashing real-looking card — the active identifier makes it fake.
        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(Flashing: true)));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Swipe);

        var outcome = session.ResolveEvaluation();
        Assert.True(session.Resolution!.IsFake);
        Assert.Equal(SourceTrace.FakeOrigin, outcome.SourceTrace);

        session.ResolveAutoCardCheck();

        Assert.Equal(PenaltyKind.None, session.LastSettlement!.Penalty);
        Assert.Equal(3, session.Run.Lives);
        Assert.Equal(1, session.Run.ConsecutiveCorrect);
        Assert.Equal(100, session.Run.Score);
        Assert.False(session.Hud.LastPenaltyAbsorbed);
    }

    [Fact]
    public void Session_MediumFakeMistake_ResetsTheComboOnly()
    {
        var session = NewSession(Difficulty.Medium);
        PlayIdentifierTurn(session);

        session.ShowInstruction(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(Flashing: true)));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Swipe);
        session.ResolveEvaluation();
        session.ResolveAutoCardCheck();

        Assert.Equal(PenaltyKind.ComboResetOnly, session.LastSettlement!.Penalty);
        Assert.Equal(3, session.Run.Lives);
        Assert.Equal(0, session.Run.ConsecutiveCorrect);
        Assert.Equal(100, session.Run.Score);
    }

    [Fact]
    public void Session_AccidentalCorrectAction_IsRewardedNormally()
    {
        // Principle 24: the final action decides. Here the player clicks because a
        // fake trap told them to; the correct directive happens to be Click (an
        // always-on Action rule), so the turn is correct and pays in full.
        var session = NewSession(Difficulty.Medium);
        PlayIdentifierTurn(session);

        session.ShowInstruction(new Instruction(
            "TRP_CLICK_NOW", RuleCategory.Trap, "CLICK NOW", new Directive(CoinAction.Click),
            IsFakeByOrigin: true, Visual: new VisualMetadata(Flashing: true)));
        session.ActivateRule(ActiveRule.Always(
            new Instruction("ACT_CLICK_ALWAYS", RuleCategory.Action, "Click Coin", new Directive(CoinAction.Click)),
            session.TurnNumber));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Click);

        var outcome = session.ResolveEvaluation();

        Assert.True(session.Resolution!.IsFake);
        Assert.Equal(CoinAction.Click, session.Resolution.FinalDirective.Action);
        Assert.Equal(TurnOutcomeKind.Correct, outcome.Kind);
        Assert.Equal(SourceTrace.Real, outcome.SourceTrace);
        Assert.False(outcome.AutoCardCheckRequired);

        session.ResolveAutoCardCheck();

        Assert.Equal(100, session.LastSettlement!.ScoreAwarded);
        Assert.Equal(200, session.Run.Score);
        Assert.Equal(2, session.Run.ConsecutiveCorrect);
        Assert.Equal(3, session.Run.Lives);
    }

    [Fact]
    public void Session_GameOver_IsReportedThroughTheRunAndTheHud()
    {
        var session = NewSession();

        for (var turn = 0; turn < 3; turn++)
            PlayTurn(session, ActionInstruction("ACT_CLICK", CoinAction.Click), CoinAction.Swipe);

        Assert.True(session.IsGameOver);
        Assert.True(session.Hud.IsGameOver);
        Assert.Equal(0, session.Hud.Lives);
        Assert.Equal(3, session.Hud.SpentLives);
        Assert.Equal(PenaltyKind.Full, session.Hud.LastPenalty);
        Assert.False(session.Hud.LastPenaltyAbsorbed);
    }
}
