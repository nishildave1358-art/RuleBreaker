namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// GDD principles 35–43 and 47–48: the eight Special Cards, the hybrid activation
/// model, and the rule that a manual card only works in the between-turns window.
/// </summary>
public class CardEffectTests
{
    private static readonly IReadOnlyList<CardDefinition> Library = TestPaths.LoadCardLibrary(out _);
    private static readonly IReadOnlyList<RuleDefinition> Rules = TestPaths.LoadLibrary(out _);

    private static string IdOf(CardEffectKind effect) => Library.Single(c => c.Effect == effect).Id;

    private static Instruction ClickInstruction() =>
        new("ACT_CLICK", RuleCategory.Action, "Click the coin", new Directive(CoinAction.Click));

    private static ActiveRule HoldRule(int activatedAt, int turns) =>
        ActiveRule.ForTurns(
            new Instruction("ACT_HOLD", RuleCategory.Action, "Hold the coin", new Directive(CoinAction.Hold)),
            activatedAt, turns);

    private static ActiveRule IdentifierRule(int activatedAt) =>
        ActiveRule.ForTurns(
            new Instruction("MOD_TRUST_BLUE", RuleCategory.Modifier, "Only trust blue cards",
                BaseDirective: null, ModifierSubType: ModifierSubType.Identifier,
                IdentifierMode: IdentifierMode.TrustOnly, Visual: new VisualMetadata(BorderBlue: true)),
            activatedAt, 5);

    private sealed record Rig(GameSession Session, CardSession Cards, CardCollection Collection);

    private static Rig NewRig(
        Difficulty difficulty = Difficulty.Medium,
        double baseSeconds = 6.0,
        int maxIntroDepth = 5,
        params CardEffectKind[] loadout)
    {
        var collection = new CardCollection();
        foreach (var card in Library) collection.Add(card);

        var session = new GameSession(new SessionConfig(difficulty, baseSeconds));
        var effects = new CardEffectEngine(Rules, new Random(1234), maxIntroDepth);
        var cards = CardSession.Begin(
            session, collection, loadout.Select(IdOf).ToArray(), effects);

        return new Rig(session, cards, collection);
    }

    /// <summary>
    /// Play one failing turn (no input against a click instruction) through the
    /// auto-card check, leaving the session in the between-turns window.
    /// </summary>
    private static void PlayToActivationWindow(GameSession session, CardSession cards, Instruction? instruction = null)
    {
        session.ShowInstruction(instruction ?? ClickInstruction());
        session.OpenResponseWindow();
        session.ConfirmNoInput();
        session.ResolveEvaluation();
        cards.ResolveAutoCardCheck();
    }

    /// <summary>
    /// Finish the turn whose instruction is already on screen (used by tests that
    /// activate a rule first), stopping in the between-turns window.
    /// </summary>
    private static void FinishTurnToActivationWindow(GameSession session, CardSession cards)
    {
        session.OpenResponseWindow();
        session.ConfirmNoInput();
        session.ResolveEvaluation();
        cards.ResolveAutoCardCheck();
    }

    // ── Auto cards (principles 41, 42) ──────────────────────────────────────────
    [Fact]
    public void Joker_AbsorbsAnIncorrectAction_AndIsConsumed()
    {
        var rig = NewRig(loadout: CardEffectKind.Joker);

        PlayToActivationWindow(rig.Session, rig.Cards);

        var settlement = rig.Session.LastSettlement!;
        Assert.Equal(IdOf(CardEffectKind.Joker), settlement.AbsorbedByCardId);
        Assert.Equal(PenaltyKind.None, settlement.Penalty);
        Assert.Equal(PenaltyKind.Full, settlement.PenaltyBeforeAbsorption);
        Assert.Equal(3, settlement.Lives);
        Assert.True(settlement.AbsorbedByAutoCard);

        // Principle 38: the copy is gone for good, not just for the session.
        Assert.Empty(rig.Cards.Loadout.Cards);
        rig.Cards.EndSession();
        Assert.Equal(0, rig.Collection.CountOf(IdOf(CardEffectKind.Joker)));
    }

    [Fact]
    public void Joker_DoesNotFireOnTimerExpiry()
    {
        var rig = NewRig(loadout: CardEffectKind.Joker);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.OpenResponseWindow();
        rig.Session.Advance(60);
        rig.Session.ResolveEvaluation();
        var fired = rig.Cards.ResolveAutoCardCheck();

        Assert.Null(fired);
        Assert.Equal(PenaltyKind.Full, rig.Session.LastSettlement!.Penalty);
        Assert.Equal(2, rig.Session.LastSettlement.Lives);
        Assert.Single(rig.Cards.Loadout.Cards); // kept for a real mistake
    }

    [Fact]
    public void Shield_CoversTimerExpiry()
    {
        var rig = NewRig(loadout: CardEffectKind.Shield);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.OpenResponseWindow();
        rig.Session.Advance(60);
        rig.Session.ResolveEvaluation();
        var fired = rig.Cards.ResolveAutoCardCheck();

        Assert.Equal(IdOf(CardEffectKind.Shield), fired);
        Assert.Equal(PenaltyKind.None, rig.Session.LastSettlement!.Penalty);
        Assert.Equal(3, rig.Session.LastSettlement.Lives);
    }

    [Fact]
    public void R8_JokerFiresBeforeShield_AndOnlyOneCardIsSpent()
    {
        var rig = NewRig(loadout: [CardEffectKind.Shield, CardEffectKind.Joker]);

        PlayToActivationWindow(rig.Session, rig.Cards);

        Assert.Equal(IdOf(CardEffectKind.Joker), rig.Session.LastSettlement!.AbsorbedByCardId);
        Assert.False(rig.Cards.Loadout.Contains(IdOf(CardEffectKind.Joker)));
        Assert.True(rig.Cards.Loadout.Contains(IdOf(CardEffectKind.Shield)));
    }

    [Fact]
    public void Joker_OnAnEasyFakeMistake_IsAWastedActivation()
    {
        // Principle 26 + 46: an Easy fake mistake already costs nothing, so the
        // Joker is spent covering a penalty that did not exist — by design.
        var rig = NewRig(Difficulty.Easy, loadout: CardEffectKind.Joker);
        var trap = new Instruction(
            "TRP_CLICK_NOW", RuleCategory.Trap, "You won 50 coins", new Directive(CoinAction.Click),
            IsFakeByOrigin: true);

        // Reacting to the fake is the Source B mistake: its resolved directive is
        // "no input", so clicking is what makes this a failure at all.
        rig.Session.ShowInstruction(trap);
        rig.Session.OpenResponseWindow();
        rig.Session.SubmitInput(CoinAction.Click);
        rig.Session.ResolveEvaluation();
        rig.Cards.ResolveAutoCardCheck();

        var settlement = rig.Session.LastSettlement!;
        Assert.Equal(SourceTrace.FakeOrigin, settlement.SourceTrace);
        Assert.Equal(PenaltyKind.None, settlement.PenaltyBeforeAbsorption);
        Assert.True(settlement.AbsorbedRedundantPenalty);
        Assert.Equal(IdOf(CardEffectKind.Joker), settlement.AbsorbedByCardId);
        Assert.Equal(3, settlement.Lives);
    }

    [Fact]
    public void PeekAutoCard_ShowsWhatWouldFire_WithoutConsuming()
    {
        var rig = NewRig(loadout: [CardEffectKind.Joker, CardEffectKind.Shield]);

        Assert.Equal(IdOf(CardEffectKind.Joker), rig.Cards.PeekAutoCard(TurnOutcomeKind.IncorrectAction)!.Id);
        Assert.Equal(IdOf(CardEffectKind.Shield), rig.Cards.PeekAutoCard(TurnOutcomeKind.TimerExpiry)!.Id);
        Assert.Null(rig.Cards.PeekAutoCard(TurnOutcomeKind.Correct));

        Assert.Equal(2, rig.Cards.Loadout.Count);
    }

    // ── Freeze ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Freeze_BlocksNewRulesForThreeTurns_WhileTurnsContinue()
    {
        var rig = NewRig(loadout: CardEffectKind.Freeze);

        PlayToActivationWindow(rig.Session, rig.Cards);
        var played = rig.Cards.PlayManual(IdOf(CardEffectKind.Freeze));

        Assert.True(played.Applied);
        Assert.Equal(3, rig.Session.FrozenTurnsRemaining);
        rig.Session.CloseActivationWindow();

        // Turn 2: the instruction still arrives (the counter continues), but no
        // rule may be activated.
        rig.Session.ShowInstruction(ClickInstruction());
        Assert.Equal(2, rig.Session.TurnNumber);
        Assert.True(rig.Session.NewRulesFrozen);
        Assert.Throws<InvalidOperationException>(() => rig.Session.ActivateRule(HoldRule(2, 1)));

        rig.Session.OpenResponseWindow();
        rig.Session.SubmitInput(CoinAction.Click);
        rig.Session.ResolveEvaluation();
        rig.Cards.ResolveAutoCardCheck();
        rig.Session.CloseActivationWindow();

        // Turns 3 and 4 stay frozen.
        foreach (var turn in new[] { 3, 4 })
        {
            rig.Session.ShowInstruction(ClickInstruction());
            Assert.Equal(turn, rig.Session.TurnNumber);
            Assert.True(rig.Session.NewRulesFrozen);
            rig.Session.OpenResponseWindow();
            rig.Session.SubmitInput(CoinAction.Click);
            rig.Session.ResolveEvaluation();
            rig.Cards.ResolveAutoCardCheck();
            rig.Session.CloseActivationWindow();
        }

        // Turn 5 is free again.
        rig.Session.ShowInstruction(ClickInstruction());
        Assert.False(rig.Session.NewRulesFrozen);
        rig.Session.ActivateRule(HoldRule(5, 1));
        Assert.Single(rig.Session.Rules.OrderedRules);
    }

    // ── Time Warp (principle 17) ───────────────────────────────────────────────
    [Fact]
    public void TimeWarp_ShortensTheNextFiveWindows_AndThenExpires()
    {
        var rig = NewRig(loadout: CardEffectKind.TimeWarp);

        PlayToActivationWindow(rig.Session, rig.Cards);
        Assert.True(rig.Cards.PlayManual(IdOf(CardEffectKind.TimeWarp)).Applied);
        rig.Session.CloseActivationWindow();

        var windows = new List<double>();
        for (var turn = 2; turn <= 7; turn++)
        {
            rig.Session.ShowInstruction(ClickInstruction());
            rig.Session.OpenResponseWindow();
            windows.Add(rig.Session.Timer.TotalSeconds);
            rig.Session.SubmitInput(CoinAction.Click);
            rig.Session.ResolveEvaluation();
            rig.Cards.ResolveAutoCardCheck();
            rig.Session.CloseActivationWindow();
        }

        // Turn base 6.0 at Medium, −40% for the five turns after activation.
        Assert.Equal(new[] { 3.6, 3.6, 3.6, 3.6, 3.6, 6.0 }, windows.Select(w => Math.Round(w, 3)).ToArray());
    }

    [Fact]
    public void TimeWarp_StillMultipliesByDifficulty()
    {
        var rig = NewRig(Difficulty.Easy, loadout: CardEffectKind.TimeWarp);

        PlayToActivationWindow(rig.Session, rig.Cards);
        rig.Cards.PlayManual(IdOf(CardEffectKind.TimeWarp));
        rig.Session.CloseActivationWindow();

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.OpenResponseWindow();

        // Easy: 6.0 × 1.5 = 9.0, then Time Warp's 0.6 → 5.4 (principal 17: the
        // multiplier is not replaced, the window is only compressed).
        Assert.Equal(5.4, Math.Round(rig.Session.Timer.TotalSeconds, 3));
    }

    // ── Rule Bomb (principle 47) ───────────────────────────────────────────────
    [Fact]
    public void RuleBomb_RemovesOneRandomActiveRule_IncludingAnIdentifier()
    {
        var rig = NewRig(loadout: CardEffectKind.RuleBomb);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.ActivateRule(IdentifierRule(1));

        FinishTurnToActivationWindow(rig.Session, rig.Cards);
        var result = rig.Cards.PlayManual(IdOf(CardEffectKind.RuleBomb));

        Assert.True(result.Applied);
        Assert.Equal("MOD_TRUST_BLUE", result.RuleRemoved!.Instruction.RuleId);
        Assert.Empty(rig.Session.Rules.OrderedRules);
        Assert.Single(rig.Session.RemovedRules);
    }

    [Fact]
    public void RuleBomb_DoesNothingWhenTheRuleSetIsEmpty_AndIsNotConsumed()
    {
        var rig = NewRig(loadout: CardEffectKind.RuleBomb);

        PlayToActivationWindow(rig.Session, rig.Cards);
        var result = rig.Cards.PlayManual(IdOf(CardEffectKind.RuleBomb));

        Assert.False(result.Applied);
        Assert.True(rig.Cards.Loadout.Contains(IdOf(CardEffectKind.RuleBomb)));
    }

    // ── Mirror (R-10) ──────────────────────────────────────────────────────────
    [Fact]
    public void Mirror_ReactivatesThePreviousRule_WithAFreshDuration()
    {
        var rig = NewRig(loadout: CardEffectKind.Mirror);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.ActivateRule(HoldRule(1, 3));

        FinishTurnToActivationWindow(rig.Session, rig.Cards);
        var result = rig.Cards.PlayManual(IdOf(CardEffectKind.Mirror));

        Assert.True(result.Applied);
        Assert.Equal(2, rig.Session.Rules.OrderedRules.Count);

        var copy = result.RuleActivated!;
        Assert.Equal("ACT_HOLD", copy.Instruction.RuleId);
        Assert.Equal(1, copy.ActivatedAtTurn);      // anchored at the play turn
        Assert.Equal(3, copy.DurationTurns);        // duration restarts
    }

    [Fact]
    public void Mirror_DoesNothingWithoutAPreviousRule()
    {
        var rig = NewRig(loadout: CardEffectKind.Mirror);

        PlayToActivationWindow(rig.Session, rig.Cards);
        var result = rig.Cards.PlayManual(IdOf(CardEffectKind.Mirror));

        Assert.False(result.Applied);
        Assert.Empty(rig.Session.Rules.OrderedRules);
    }

    // ── Echo (principle 8) ─────────────────────────────────────────────────────
    [Fact]
    public void Echo_RestoresAChosenExpiredRule()
    {
        var rig = NewRig(loadout: CardEffectKind.Echo);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.ActivateRule(HoldRule(1, 1));   // expires when turn 2 starts
        rig.Session.OpenResponseWindow();
        rig.Session.SubmitInput(CoinAction.Click);
        rig.Session.ResolveEvaluation();
        rig.Cards.ResolveAutoCardCheck();
        rig.Session.CloseActivationWindow();

        rig.Session.ShowInstruction(ClickInstruction());
        Assert.Single(rig.Session.ExpiredRules);
        rig.Session.OpenResponseWindow();
        rig.Session.SubmitInput(CoinAction.Click);
        rig.Session.ResolveEvaluation();
        rig.Cards.ResolveAutoCardCheck();

        var result = rig.Cards.PlayManual(IdOf(CardEffectKind.Echo), "ACT_HOLD");

        Assert.True(result.Applied);
        Assert.Equal("ACT_HOLD", Assert.Single(rig.Session.Rules.OrderedRules).Instruction.RuleId);
        Assert.Equal(2, result.RuleActivated!.ActivatedAtTurn);
    }

    [Fact]
    public void Echo_RequiresThePlayerToChooseARule_AndIsNotConsumedWithoutOne()
    {
        var rig = NewRig(loadout: CardEffectKind.Echo);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.ActivateRule(HoldRule(1, 1));
        rig.Session.OpenResponseWindow();
        rig.Session.SubmitInput(CoinAction.Click);
        rig.Session.ResolveEvaluation();
        rig.Cards.ResolveAutoCardCheck();
        rig.Session.CloseActivationWindow();
        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.OpenResponseWindow();
        rig.Session.SubmitInput(CoinAction.Click);
        rig.Session.ResolveEvaluation();
        rig.Cards.ResolveAutoCardCheck();

        var noTarget = rig.Cards.PlayManual(IdOf(CardEffectKind.Echo));
        Assert.False(noTarget.Applied);
        Assert.Contains("ACT_HOLD", noTarget.Message);
        Assert.True(rig.Cards.Loadout.Contains(IdOf(CardEffectKind.Echo)));

        // A rule that never expired is not a legal Echo target.
        var wrongTarget = rig.Cards.PlayManual(IdOf(CardEffectKind.Echo), "ACT_RUNNING");
        Assert.False(wrongTarget.Applied);
        Assert.True(rig.Cards.Loadout.Contains(IdOf(CardEffectKind.Echo)));
    }

    [Fact]
    public void Echo_IsTheOnlyResurrectionPath_SoARuleBombedRuleIsNotOffered()
    {
        var rig = NewRig(loadout: [CardEffectKind.RuleBomb, CardEffectKind.Echo]);
        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.ActivateRule(HoldRule(1, 5));

        FinishTurnToActivationWindow(rig.Session, rig.Cards);
        var bomb = rig.Cards.PlayManual(IdOf(CardEffectKind.RuleBomb));
        Assert.True(bomb.Applied);

        Assert.Empty(rig.Session.ExpiredRules);      // destroyed, not expired
        Assert.Single(rig.Session.RemovedRules);

        var echo = rig.Cards.PlayManual(IdOf(CardEffectKind.Echo), "ACT_HOLD");
        Assert.False(echo.Applied);
    }

    // ── Chaos ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Chaos_ActivatesAModifierRule_WithinTheDepthGate()
    {
        var rig = NewRig(maxIntroDepth: 2, loadout: CardEffectKind.Chaos);

        PlayToActivationWindow(rig.Session, rig.Cards);
        var result = rig.Cards.PlayManual(IdOf(CardEffectKind.Chaos));

        Assert.True(result.Applied);
        var activated = Assert.Single(rig.Session.Rules.OrderedRules);
        Assert.Equal(RuleCategory.Modifier, activated.Instruction.Category);

        var definition = Rules.Single(r => r.Id == activated.Instruction.RuleId);
        Assert.InRange(definition.IntroDepth, 1, 2);
        Assert.NotEqual(InversionKind.RetroactiveLie, definition.InversionKind);
    }

    // ── principle 43: manual cards live in the between-turns window only ──────
    [Fact]
    public void ManualCards_CannotBePlayedDuringTheResponseWindow()
    {
        var rig = NewRig(loadout: CardEffectKind.Freeze);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.OpenResponseWindow();

        Assert.Throws<InvalidOperationException>(() => rig.Cards.PlayManual(IdOf(CardEffectKind.Freeze)));
        Assert.True(rig.Cards.Loadout.Contains(IdOf(CardEffectKind.Freeze)));
    }

    [Fact]
    public void AutoCards_CannotBeClickedInTheActivationWindow()
    {
        var rig = NewRig(loadout: CardEffectKind.Joker);

        rig.Session.ShowInstruction(ClickInstruction());
        rig.Session.OpenResponseWindow();
        rig.Session.SubmitInput(CoinAction.Click);   // correct: the Joker stays
        rig.Session.ResolveEvaluation();
        rig.Cards.ResolveAutoCardCheck();

        Assert.True(rig.Cards.IsActivationWindowOpen);
        Assert.Throws<InvalidOperationException>(() => rig.Cards.PlayManual(IdOf(CardEffectKind.Joker)));
        Assert.True(rig.Cards.Loadout.Contains(IdOf(CardEffectKind.Joker)));
    }

    [Fact]
    public void CardsOutsideTheLoadout_CannotBePlayed()
    {
        var rig = NewRig(loadout: CardEffectKind.Freeze);

        PlayToActivationWindow(rig.Session, rig.Cards);

        Assert.Throws<InvalidOperationException>(
            () => rig.Cards.PlayManual(IdOf(CardEffectKind.RuleBomb)));
    }

    // ── principle 39: unused cards come home ───────────────────────────────────
    [Fact]
    public void EndSession_ReturnsUnusedCards_AndClosesTheSession()
    {
        var rig = NewRig(loadout: [CardEffectKind.Freeze, CardEffectKind.Chaos]);

        Assert.Equal(0, rig.Collection.CountOf(IdOf(CardEffectKind.Freeze)));

        rig.Cards.EndSession();

        Assert.Equal(1, rig.Collection.CountOf(IdOf(CardEffectKind.Freeze)));
        Assert.Equal(1, rig.Collection.CountOf(IdOf(CardEffectKind.Chaos)));
        Assert.True(rig.Cards.Loadout.IsEmpty);
        Assert.False(rig.Cards.IsActive);

        // Idempotent: a second call must not duplicate the refund.
        rig.Cards.EndSession();
        Assert.Equal(1, rig.Collection.CountOf(IdOf(CardEffectKind.Freeze)));
    }

    [Fact]
    public void ActivatedCards_AreNotReturned_OnlyTheRemainingOnes()
    {
        var rig = NewRig(loadout: [CardEffectKind.Freeze, CardEffectKind.Chaos]);

        PlayToActivationWindow(rig.Session, rig.Cards);
        Assert.True(rig.Cards.PlayManual(IdOf(CardEffectKind.Freeze)).Applied);

        rig.Cards.EndSession();

        Assert.Equal(0, rig.Collection.CountOf(IdOf(CardEffectKind.Freeze))); // consumed
        Assert.Equal(1, rig.Collection.CountOf(IdOf(CardEffectKind.Chaos)));  // returned
    }
}
