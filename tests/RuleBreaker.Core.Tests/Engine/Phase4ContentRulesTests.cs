namespace RuleBreaker.Core.Tests.Engine;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// Phase 4 content systems: exact-word Trigger evaluation against on-screen text,
/// category-scoped Ignore modifiers, and Memory store → recall.
/// </summary>
public class Phase4ContentRulesTests
{
    private static readonly ModifierEngine Engine = new();

    private static Instruction Trigger(string word, CoinAction action) =>
        new($"TRG_{word}", RuleCategory.Trigger, $"Click When You See {word}",
            new Directive(action, word), TriggerWord: word);

    private static Instruction Action(string id, CoinAction action) =>
        new(id, RuleCategory.Action, "Act", new Directive(action));

    private static Instruction Restriction(string id, CoinAction forbidden) =>
        new(id, RuleCategory.Restriction, "Do Not Act", new Directive(forbidden));

    private static ResolutionContext Context(
        RuleSetState? state = null,
        string? screenText = null,
        MemoryBank? memory = null,
        string? screenValue = null) =>
        new(state ?? new RuleSetState(), 2, screenText, memory, screenValue);

    // ── Trigger: exact word on screen ────────────────────────────────────────────
    [Theory]
    [InlineData("The word RATE is glowing", CoinAction.Click)]
    [InlineData("RATE", CoinAction.Click)]
    [InlineData("RATED", CoinAction.None)]
    [InlineData("PIRATE", CoinAction.None)]
    [InlineData("RATER", CoinAction.None)]
    [InlineData("", CoinAction.None)]
    public void Trigger_FiresOnlyForTheExactWordOnScreen(string screenText, CoinAction expected)
    {
        var result = Engine.Resolve(Trigger("RATE", CoinAction.Click), Context(screenText: screenText));

        Assert.Equal(expected, result.FinalDirective!.Action);
    }

    [Fact]
    public void Trigger_IgnoresItsOwnCardWording()
    {
        Assert.Equal(CoinAction.None, Engine.Resolve(Trigger("RATE", CoinAction.Click), Context()).FinalDirective!.Action);
    }

    [Fact]
    public void ActiveTriggerRule_JoinsConflictResolution_OnlyWhileItsWordIsOnScreen()
    {
        var triggerRule = ActiveRule.Always(
            new Instruction("TRG_GOLD", RuleCategory.Trigger, "Click When You See GOLD",
                new Directive(CoinAction.Click, "GOLD"), TriggerWord: "GOLD"),
            activatedAtTurn: 1);

        var state = new RuleSetState().Activate(triggerRule);

        var withWord = Engine.Resolve(Action("ACT_HOLD", CoinAction.Hold), Context(state, screenText: "GOLD"));
        Assert.Equal(CoinAction.Click, withWord.FinalDirective!.Action);

        var withoutWord = Engine.Resolve(Action("ACT_HOLD", CoinAction.Hold), Context(state, screenText: "SILVER"));
        Assert.Equal(CoinAction.Hold, withoutWord.FinalDirective!.Action);
    }

    // ── Category-scoped ignore ───────────────────────────────────────────────────
    [Fact]
    public void CategoryScopedIgnore_VoidsItsCategory_AndLeavesOtherCategoriesAlone()
    {
        var ignoreActions = ActiveRule.ForTurns(
            new Instruction("MOD_IGNORE_ACTIONS_TWO", RuleCategory.Modifier, "Ignore Action Rules For 2 Turns",
                BaseDirective: null, ModifierSubType: ModifierSubType.Ignore, IgnoreCategory: RuleCategory.Action),
            activatedAtTurn: 1, turns: 2);
        var state = new RuleSetState().Activate(ignoreActions);

        var action = Engine.Resolve(Action("ACT_CLICK", CoinAction.Click), new ResolutionContext(state, 2));
        var restriction = Engine.Resolve(Restriction("RST_NO_CLICK", CoinAction.Click), new ResolutionContext(state, 2));

        Assert.True(action.IsIgnored);
        Assert.Equal("MOD_IGNORE_ACTIONS_TWO", action.IgnoreByRuleId);
        Assert.False(restriction.IsIgnored);
        Assert.Equal(CoinAction.None, restriction.FinalDirective!.Action);
    }

    [Fact]
    public void CategoryScopedIgnore_ExpiresWithItsTurns()
    {
        var state = new RuleSetState().Activate(ActiveRule.ForTurns(
            new Instruction("MOD_IGNORE_ACTIONS_TWO", RuleCategory.Modifier, "Ignore Action Rules For 2 Turns",
                BaseDirective: null, ModifierSubType: ModifierSubType.Ignore, IgnoreCategory: RuleCategory.Action),
            activatedAtTurn: 1, turns: 2));

        var result = Engine.Resolve(Action("ACT_CLICK", CoinAction.Click), new ResolutionContext(state, 3));

        Assert.False(result.IsIgnored);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void UnscopedIgnore_StillVoidsEveryCategory()
    {
        var state = new RuleSetState().Activate(ActiveRule.ForNextInstruction(
            new Instruction("MOD_IGNORE_NEXT", RuleCategory.Modifier, "Ignore Next Instruction",
                BaseDirective: null, ModifierSubType: ModifierSubType.Ignore),
            activatedAtTurn: 1));

        var result = Engine.Resolve(Restriction("RST_NO_CLICK", CoinAction.Click), new ResolutionContext(state, 2));

        Assert.True(result.IsIgnored);
    }

    // ── Memory: store → recall ───────────────────────────────────────────────────
    [Fact]
    public void MemoryStore_RecordsTheDisplayedValue_AndDoesNothingElse()
    {
        var store = new Instruction("MEM_REMEMBER_NUMBER", RuleCategory.Memory, "Remember The Number",
            BaseDirective: null, MemoryStoreKey: MemoryKeys.Number);

        var result = Engine.Resolve(store, Context(screenValue: "7"));

        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        Assert.Equal(new MemoryWrite(MemoryKeys.Number, "7"), result.MemoryWrite);
    }

    [Fact]
    public void MemoryStore_WithoutAValue_WritesNothing()
    {
        var store = new Instruction("MEM_REMEMBER_COLOR", RuleCategory.Memory, "Remember The Color",
            BaseDirective: null, MemoryStoreKey: MemoryKeys.Color);

        var result = Engine.Resolve(store, Context());

        Assert.Null(result.MemoryWrite);
    }

    [Fact]
    public void MemoryRecall_ActsOnlyWhenTheStoredValueSatisfiesTheTask()
    {
        var bank = new MemoryBank();
        bank.Store(MemoryKeys.Number, "7");

        var odd = new Instruction("MEM_CLICK_ODD", RuleCategory.Memory, "Click If The Number Was Odd",
            new Directive(CoinAction.Click), MemoryTask: MemoryTask.NumberIsOdd);
        var even = new Instruction("MEM_HOLD_EVEN", RuleCategory.Memory, "Hold If The Number Was Even",
            new Directive(CoinAction.Hold), MemoryTask: MemoryTask.NumberIsEven);

        Assert.Equal(CoinAction.Click, Engine.Resolve(odd, Context(memory: bank)).FinalDirective!.Action);
        Assert.Equal(CoinAction.None, Engine.Resolve(even, Context(memory: bank)).FinalDirective!.Action);

        bank.Store(MemoryKeys.Number, "8");
        Assert.Equal(CoinAction.None, Engine.Resolve(odd, Context(memory: bank)).FinalDirective!.Action);
        Assert.Equal(CoinAction.Hold, Engine.Resolve(even, Context(memory: bank)).FinalDirective!.Action);
    }

    [Fact]
    public void MemoryRecall_FailsClosedWhenNothingWasStored()
    {
        var recall = new Instruction("MEM_CLICK_ODD", RuleCategory.Memory, "Click If The Number Was Odd",
            new Directive(CoinAction.Click), MemoryTask: MemoryTask.NumberIsOdd);

        var result = Engine.Resolve(recall, Context());

        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void MemorySymbolNotStar_IsARealNegation()
    {
        var recall = new Instruction("MEM_CLICK_NOT_STAR", RuleCategory.Memory, "Click If The Symbol Was Not Star",
            new Directive(CoinAction.Click), MemoryTask: MemoryTask.SymbolIsNotStar);

        var star = new MemoryBank();
        star.Store(MemoryKeys.Symbol, "Star");
        Assert.Equal(CoinAction.None, Engine.Resolve(recall, Context(memory: star)).FinalDirective!.Action);

        var moon = new MemoryBank();
        moon.Store(MemoryKeys.Symbol, "Moon");
        Assert.Equal(CoinAction.Click, Engine.Resolve(recall, Context(memory: moon)).FinalDirective!.Action);
    }

    [Fact]
    public void AnIgnoredMemoryStore_DoesNotWrite()
    {
        var state = new RuleSetState().Activate(ActiveRule.ForNextInstruction(
            new Instruction("MOD_IGNORE_NEXT", RuleCategory.Modifier, "Ignore Next Instruction",
                BaseDirective: null, ModifierSubType: ModifierSubType.Ignore),
            activatedAtTurn: 1));

        var store = new Instruction("MEM_REMEMBER_NUMBER", RuleCategory.Memory, "Remember The Number",
            BaseDirective: null, MemoryStoreKey: MemoryKeys.Number);

        var result = Engine.Resolve(store, new ResolutionContext(state, 2, ScreenValue: "7"));

        Assert.True(result.IsIgnored);
        Assert.Null(result.MemoryWrite);
    }

    [Fact]
    public void Apply_CommitsAWholeStoreThenRecallCycle()
    {
        var bank = new MemoryBank();
        var store = new Instruction("MEM_REMEMBER_WORD", RuleCategory.Memory, "Remember The Word",
            BaseDirective: null, MemoryStoreKey: MemoryKeys.Word);
        var recall = new Instruction("MEM_CLICK_RATE", RuleCategory.Memory, "Click If The Word Was RATE",
            new Directive(CoinAction.Click), MemoryTask: MemoryTask.WordIsRate);

        bank.Apply(Engine.Resolve(store, Context(screenValue: "RATE")).MemoryWrite!);

        Assert.Equal(CoinAction.Click, Engine.Resolve(recall, Context(memory: bank)).FinalDirective!.Action);
    }

    // ── R-6: metadata is orthogonal to truth ─────────────────────────────────────
    [Fact]
    public void TrapMetadata_IsOrthogonalToTruth_AndIdentifierTrustCanMakeItReal()
    {
        var flashingTrap = new Instruction(
            "TRP_TAP_FAST", RuleCategory.Trap, "TAP FAST", new Directive(CoinAction.Click),
            IsFakeByOrigin: true, Visual: new VisualMetadata(Flashing: true));

        // Without an identifier the flashing trap is simply fake.
        var alone = Engine.Resolve(flashingTrap, Context());
        Assert.True(alone.IsFake);
        Assert.Equal(CoinAction.None, alone.FinalDirective!.Action);

        // A Deny identifier agrees with the trap's origin: still fake.
        var deny = new RuleSetState().Activate(ActiveRule.Always(
            new Instruction("MOD_IGNORE_FLASHING", RuleCategory.Modifier, "Ignore Flashing Instructions",
                BaseDirective: null, ModifierSubType: ModifierSubType.Identifier,
                IdentifierMode: IdentifierMode.Deny, Visual: new VisualMetadata(Flashing: true)),
            activatedAtTurn: 1));
        Assert.True(Engine.Resolve(flashingTrap, new ResolutionContext(deny, 2)).IsFake);

        // An inverted Deny is "Trust Flashing" (GDD identifier inversion table):
        // the same trap becomes mechanically real and its bait action applies.
        var trust = new RuleSetState().Activate(ActiveRule.Always(
            new Instruction("MOD_TRUST_FLASHING", RuleCategory.Modifier, "Only Trust Flashing Instructions",
                BaseDirective: null, ModifierSubType: ModifierSubType.Identifier,
                InversionKind: InversionKind.Lie, IdentifierMode: IdentifierMode.Deny,
                Visual: new VisualMetadata(Flashing: true)),
            activatedAtTurn: 1));
        var trusted = Engine.Resolve(flashingTrap, new ResolutionContext(trust, 2));

        Assert.True(trusted.IsTrustedThenReal);
        Assert.Equal(CoinAction.Click, trusted.FinalDirective!.Action);
    }

    // ── Session integration (GDD loop) ───────────────────────────────────────────
    [Fact]
    public void Session_StoresAndRecallsThroughTheTurnLoop()
    {
        var session = new GameSession();

        session.ShowInstruction(
            new Instruction("MEM_REMEMBER_NUMBER", RuleCategory.Memory, "Remember The Number",
                BaseDirective: null, MemoryStoreKey: MemoryKeys.Number),
            screenValue: "7");
        session.OpenResponseWindow();
        session.ConfirmNoInput();

        Assert.True(session.ResolveEvaluation().IsCorrect);
        Assert.Equal("7", session.Memory.Recall(MemoryKeys.Number));

        session.ResolveAutoCardCheck();
        session.CloseActivationWindow();

        session.ShowInstruction(
            new Instruction("MEM_CLICK_ODD", RuleCategory.Memory, "Click If The Number Was Odd",
                new Directive(CoinAction.Click), MemoryTask: MemoryTask.NumberIsOdd));
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Click);

        Assert.True(session.ResolveEvaluation().IsCorrect);
    }

    [Fact]
    public void Session_TriggerUsesTheTurnsScreenText_NotTheCardWording()
    {
        var session = new GameSession();

        session.ShowInstruction(
            new Instruction("TRG_CLICK_RATE", RuleCategory.Trigger, "Click When You See RATE",
                new Directive(CoinAction.Click, "RATE"), TriggerWord: "RATE"),
            screenText: "The word RATE is on screen");
        session.OpenResponseWindow();
        session.SubmitInput(CoinAction.Click);

        Assert.True(session.ResolveEvaluation().IsCorrect);

        session.ResolveAutoCardCheck();
        session.CloseActivationWindow();

        // Same card, no ambient word: the correct action is deliberate no-input.
        session.ShowInstruction(
            new Instruction("TRG_CLICK_RATE", RuleCategory.Trigger, "Click When You See RATE",
                new Directive(CoinAction.Click, "RATE"), TriggerWord: "RATE"));
        session.OpenResponseWindow();
        session.ConfirmNoInput();

        Assert.True(session.ResolveEvaluation().IsCorrect);
    }

    [Fact]
    public void MemoryBank_KeysAreCaseInsensitive_AndCloneIsIndependent()
    {
        var bank = new MemoryBank();
        bank.Store("Number", "7");

        Assert.Equal("7", bank.Recall("number"));

        var clone = bank.Clone();
        clone.Store("number", "8");

        Assert.Equal("7", bank.Recall("number"));
        Assert.Equal("8", clone.Recall("number"));
    }
}
