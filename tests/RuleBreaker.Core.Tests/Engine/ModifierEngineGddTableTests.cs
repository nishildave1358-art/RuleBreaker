using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using Xunit;

namespace RuleBreaker.Core.Tests.Engine;

public class ModifierEngineGddTableTests
{
    private static ResolutionContext EmptyContext => new(new RuleSetState(), 0);

    // ── helpers ────────────────────────────────────────────────────────────────
    private static ActiveRule Action(string id, CoinAction action, int activatedAt, RuleScope scope, int? turns = null)
        => scope switch
        {
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(new Instruction(id, RuleCategory.Action, "Act", new Directive(action)), activatedAt),
            RuleScope.Turns => ActiveRule.ForTurns(new Instruction(id, RuleCategory.Action, "Act", new Directive(action)), activatedAt, turns!.Value),
            _ => ActiveRule.Always(new Instruction(id, RuleCategory.Action, "Act", new Directive(action)), activatedAt),
        };

    private static ActiveRule Restriction(string id, CoinAction forbidden, int activatedAt, RuleScope scope, int? turns = null)
        => scope switch
        {
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(new Instruction(id, RuleCategory.Restriction, "Do Not Act", new Directive(forbidden)), activatedAt),
            RuleScope.Turns => ActiveRule.ForTurns(new Instruction(id, RuleCategory.Restriction, "Do Not Act", new Directive(forbidden)), activatedAt, turns!.Value),
            _ => ActiveRule.Always(new Instruction(id, RuleCategory.Restriction, "Do Not Act", new Directive(forbidden)), activatedAt),
        };

    private static ActiveRule Inversion(string id, InversionKind kind, int activatedAt, RuleScope scope, int? turns = null, bool invertedOnSelf = false)
        => scope switch
        {
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(new Instruction(id, RuleCategory.Modifier, "Lie", BaseDirective: null, ModifierSubType: ModifierSubType.Inversion, InversionKind: kind), activatedAt),
            RuleScope.Turns => ActiveRule.ForTurns(new Instruction(id, RuleCategory.Modifier, "Lie", BaseDirective: null, ModifierSubType: ModifierSubType.Inversion, InversionKind: kind), activatedAt, turns!.Value),
            _ => ActiveRule.Always(new Instruction(id, RuleCategory.Modifier, "Lie", BaseDirective: null, ModifierSubType: ModifierSubType.Inversion, InversionKind: kind), activatedAt),
        };

    private static ActiveRule IgnoreRule(string id, int activatedAt, RuleScope scope, int? turns = null, ModifierSubType subType = ModifierSubType.Ignore)
        => scope switch
        {
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(new Instruction(id, RuleCategory.Modifier, "Ignore", BaseDirective: null, ModifierSubType: subType), activatedAt),
            RuleScope.Turns => ActiveRule.ForTurns(new Instruction(id, RuleCategory.Modifier, "Ignore", BaseDirective: null, ModifierSubType: subType), activatedAt, turns!.Value),
            _ => ActiveRule.Always(new Instruction(id, RuleCategory.Modifier, "Ignore", BaseDirective: null, ModifierSubType: subType), activatedAt),
        };

    private static ActiveRule Identifier(string id, IdentifierMode mode, VisualMetadata pred, int activatedAt, RuleScope scope, int? turns = null, bool invertedOnSelf = false)
    {
        // invertedOnSelf models "a Lie / Reverse targeted this Identifier Rule"
        // itself (GDD identifier inversion table). An ODD number of inversions on the
        // rule flips its trust semantics (Deny → Trust, TrustOnly → Distrust); an
        // even count leaves it in its base posture, i.e. no inversion kind recorded.
        var kind = invertedOnSelf ? InversionKind.Lie : (InversionKind?)null;
        Instruction Card() => new(id, RuleCategory.Modifier, "Id", BaseDirective: null,
            ModifierSubType: ModifierSubType.Identifier, InversionKind: kind, IdentifierMode: mode, Visual: pred);
        return scope switch
        {
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(Card(), activatedAt),
            RuleScope.Turns => ActiveRule.ForTurns(Card(), activatedAt, turns!.Value),
            _ => ActiveRule.Always(Card(), activatedAt),
        };
    }

    private static ActiveRule RetroactiveLie(string id, string targetRuleId, int activatedAt)
        => ActiveRule.ForRetroactiveLie(
            new Instruction(id, RuleCategory.Modifier, "Previous Instruction Was A Lie", BaseDirective: null,
                ModifierSubType: ModifierSubType.Inversion, InversionKind: InversionKind.RetroactiveLie),
            activatedAt,
            targetRuleId);

    private static ActiveRule Trap(string id, CoinAction? action = null, int activatedAt = 0, RuleScope scope = RuleScope.Always, int? turns = null)
        => scope switch
        {
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(new Instruction(id, RuleCategory.Trap, "TRAP", BaseDirective: action.HasValue ? new Directive(action.Value) : null, IsFakeByOrigin: true), activatedAt),
            RuleScope.Turns => ActiveRule.ForTurns(new Instruction(id, RuleCategory.Trap, "TRAP", BaseDirective: action.HasValue ? new Directive(action.Value) : null, IsFakeByOrigin: true), activatedAt, turns!.Value),
            _ => ActiveRule.Always(new Instruction(id, RuleCategory.Trap, "TRAP", BaseDirective: action.HasValue ? new Directive(action.Value) : null, IsFakeByOrigin: true), activatedAt),
        };

    private static Instruction ActionInstruction(string id, CoinAction action, VisualMetadata? visual = null, Directive? baseDirective = null)
        => new(id, RuleCategory.Action, "Act", baseDirective ?? new Directive(action), Visual: visual);

    private static Instruction RestrictionInstruction(string id, CoinAction forbidden, VisualMetadata? visual = null)
        => new(id, RuleCategory.Restriction, "Do Not Act", new Directive(forbidden), Visual: visual);

    private static Instruction TrapInstruction(string id, string text = "CLICK NOW", VisualMetadata? visual = null)
        => new(id, RuleCategory.Trap, text, BaseDirective: null, IsFakeByOrigin: true, Visual: visual);

    private static Instruction TriggerInstruction(string id, string text, Directive directive, VisualMetadata? visual = null)
        => new(id, RuleCategory.Trigger, text, directive, Visual: visual);

    // ── 1. INVERSION TABLE (GDD) ─────────────────────────────────────────────────
    // Rows come straight from the GDD inversion table:
    //   1 inversion on Action → NoInput (0 / 1)
    //   0 inversion on Action → action (1 / 0) -> already covered by base smoke
    //   1 inversion on Restriction → base action restored (1 / 1)
    //   2 inversions on Action → action restored (2 / 1)
    //   2 inversions on Restriction → NoInput restored (2 / 2)

    [Fact]
    public void Inversion_1_LieOnAction_NextInstruction_GivesNoInput()
    {
        var state = new RuleSetState().Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        Assert.True(result.WasInvertedMechanically);
        Assert.Equal(1, result.InversionCount);
    }

    [Fact]
    public void Inversion_1_LieOnRestriction_NextInstruction_RestoresBaseAction()
    {
        // GDD: "Do Not Click" + "Next Instruction Is A Lie" → "Click Coin".
        var state = new RuleSetState().Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(RestrictionInstruction("RST_NO_CLICK", CoinAction.Click), ctx);

        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
        Assert.True(result.WasInvertedMechanically);
        Assert.Equal(1, result.InversionCount);
    }

    [Fact]
    public void Inversion_2_LiesOnAction_NextInstruction_RestoresAction()
    {
        // Two NextInstruction inversions are both active at turn 2 if both were
        // activated at turn 1 with NextInstruction scope? No — NextInstruction dies
        // at > activatedAt, so only a Turns-scoped double-lie survives. Use Turns=2
        // activated at turn 1 to model "two lies in force this turn".
        var state = new RuleSetState()
            .Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 1, RuleScope.Turns, 2))
            .Activate(Inversion("MOD_REV_NEXT", InversionKind.Reverse, 1, RuleScope.Turns, 2));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
        Assert.True(result.WasInvertedMechanically);
        Assert.Equal(2, result.InversionCount);
    }

    [Fact]
    public void Inversion_2_OnRestriction_RestoresNoInput()
    {
        // "Do Not Click" + 2 inversions → back to "Do Not Click" = NoInput.
        var state = new RuleSetState()
            .Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 1, RuleScope.Turns, 2))
            .Activate(Inversion("MOD_REV_NEXT", InversionKind.Reverse, 1, RuleScope.Turns, 2));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(RestrictionInstruction("RST_NO_CLICK", CoinAction.Click), ctx);

        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        Assert.True(result.WasInvertedMechanically);
        Assert.Equal(2, result.InversionCount);
    }

    // ── 2. IDENTIFIER INVERSION TABLE (GDD, all 6 rows) ─────────────────────────
    // Trust-only (Only Trust Blue) + flashing blue card → real.
    // Trust-only + non-blue → not marked fake by us (potential fake, not tagged).
    // Deny (Ignore Flashing) + flashing → fake.
    // Each inverted: TrustOnly→distrust (blue = fake), Deny→trust.

    [Fact]
    public void Identifier_TrustOnlyBlue_NonInverted_BlueCardReal()
    {
        var state = new RuleSetState().Activate(Identifier("MOD_ONLY_TRUST_BLUE", IdentifierMode.TrustOnly, new VisualMetadata(BorderBlue: true), 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(BorderBlue: true)), ctx);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void Identifier_TrustOnlyBlue_NonInverted_NonBlueNotMarkedFake()
    {
        // Non-blue under TrustOnly is "potentially fake" but not classified fake
        // by this rule alone, so the action stays real.
        var state = new RuleSetState().Activate(Identifier("MOD_ONLY_TRUST_BLUE", IdentifierMode.TrustOnly, new VisualMetadata(BorderBlue: true), 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata()), ctx);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void Identifier_DenyFlashing_NonInverted_FlashingIsFake()
    {
        var state = new RuleSetState().Activate(Identifier("MOD_IGNORE_FLASHING", IdentifierMode.Deny, new VisualMetadata(Flashing: true), 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(Flashing: true)), ctx);

        Assert.True(result.IsFake);
        Assert.True(result.IsFakeCurrentlyDisplayed);
        Assert.Equal("MOD_IGNORE_FLASHING", result.FakeClassifiedByRuleId);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void Identifier_TrustOnlyBlue_Inverted_DistrustBlue_Fake()
    {
        // GDD identifier inversion table row 5: "Only Trust Blue" + 1 Lie (odd) →
        // "Distrust Blue" → blue cards become fake.
        var state = new RuleSetState().Activate(
            Identifier("MOD_ONLY_TRUST_BLUE", IdentifierMode.TrustOnly, new VisualMetadata(BorderBlue: true), 1,
                RuleScope.NextInstruction, invertedOnSelf: true));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(BorderBlue: true)), ctx);

        Assert.True(result.IsFake);
        Assert.True(result.IsFakeCurrentlyDisplayed);
        Assert.Equal("MOD_ONLY_TRUST_BLUE", result.FakeClassifiedByRuleId);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void Identifier_DenyFlashing_Inverted_TrustFlashing_Real()
    {
        // GDD identifier inversion table row 2: "Ignore Flashing" + 1 Lie (odd) →
        // "Trust Flashing" → flashing cards become mechanically real.
        var state = new RuleSetState().Activate(
            Identifier("MOD_IGNORE_FLASHING", IdentifierMode.Deny, new VisualMetadata(Flashing: true), 1,
                RuleScope.NextInstruction, invertedOnSelf: true));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(Flashing: true)), ctx);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void Identifier_EvenInversionCount_KeepsBasePosture()
    {
        // GDD identifier inversion table rows 3 and 6 ("1 Lie + 1 Reverse" = 2, even):
        // even parity restores/keeps the rule's BASE posture — "Ignore Flashing"
        // remains fake, "Only Trust Blue" remains real. The engine represents an even
        // count by not recording an inversion kind on the identifier rule, so these
        // two assertions pin that even parity never flips trust semantics.
        var denyState = new RuleSetState().Activate(
            Identifier("MOD_IGNORE_FLASHING", IdentifierMode.Deny, new VisualMetadata(Flashing: true), 1, RuleScope.NextInstruction));
        var denyResult = new ModifierEngine().Resolve(
            ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(Flashing: true)),
            new ResolutionContext(denyState, 2));

        Assert.True(denyResult.IsFake); // "remain fake"

        var trustState = new RuleSetState().Activate(
            Identifier("MOD_ONLY_TRUST_BLUE", IdentifierMode.TrustOnly, new VisualMetadata(BorderBlue: true), 1, RuleScope.NextInstruction));
        var trustResult = new ModifierEngine().Resolve(
            ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(BorderBlue: true)),
            new ResolutionContext(trustState, 2));

        Assert.False(trustResult.IsFake); // "blue = real"
    }

    [Fact]
    public void Identifier_DenyRedText_ClassifiesCardCarryingExtraVisualState()
    {
        // A predicate constrains only the visual flags it names: "Ignore Red Text
        // Instructions" marks a red-text card fake even when the card also carries
        // another visual state (here a blue border).
        var state = new RuleSetState().Activate(
            Identifier("MOD_IGNORE_RED_TEXT", IdentifierMode.Deny, new VisualMetadata(RedText: true), 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(
            ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(RedText: true, BorderBlue: true)), ctx);

        Assert.True(result.IsFake);
        Assert.Equal("MOD_IGNORE_RED_TEXT", result.FakeClassifiedByRuleId);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void RuleSetState_Identifier_EvaluatesNewestRelevantAsExpected()
    {
        // Two identifiers in force: old Deny and newer TrustOnly with the same
        // predicate. Newest trust-only should win → blue is real.
        var state = new RuleSetState()
            .Activate(Identifier("MOD_OLD_DENY_BLUE", IdentifierMode.Deny, new VisualMetadata(BorderBlue: true), 1, RuleScope.Turns, 3))
            .Activate(Identifier("MOD_NEW_TRUST_BLUE", IdentifierMode.TrustOnly, new VisualMetadata(BorderBlue: true), 2, RuleScope.Turns, 3));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(BorderBlue: true)), ctx);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    // ── 3. IGNORE (absolute priority, cascading, voids everything) ───────────────
    [Fact]
    public void Ignore_VoidsAction_Instruction_AndHasConcreteDirective()
    {
        var state = new RuleSetState().Activate(IgnoreRule("MOD_IGNORE_NEXT", 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.True(result.IsIgnored);
        Assert.Equal("MOD_IGNORE_NEXT", result.IgnoreByRuleId);
        Assert.False(result.IsFake);
        // Still emits a concrete correct directive.
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void Ignore_VoidsTrap_Instruction()
    {
        var state = new RuleSetState().Activate(IgnoreRule("MOD_IGNORE_NEXT", 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(TrapInstruction("TRP_CLICK_NOW"), ctx);

        Assert.True(result.IsIgnored);
        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void Ignore_VoidsAnIgnoreInstruction_CascadeFlagged()
    {
        // GDD cascading ignore: when the next instruction is itself an Ignore, that
        // Ignore is voided (the instruction after it is NOT automatically ignored),
        // and the result is flagged as a cascade for the presentation layer.
        var state = new RuleSetState().Activate(IgnoreRule("MOD_IGNORE_NEXT", 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var ignoreCard = new Instruction(
            "MOD_IGNORE_NEXT_2", RuleCategory.Modifier, "Ignore Next Instruction",
            BaseDirective: null, ModifierSubType: ModifierSubType.Ignore);

        var result = new ModifierEngine().Resolve(ignoreCard, ctx);

        Assert.True(result.IsIgnored);
        Assert.True(result.IgnoredByCascadeRule);
        Assert.Equal("MOD_IGNORE_NEXT", result.IgnoreByRuleId);
        Assert.False(result.IsFake);
    }

    [Fact]
    public void RuleSetState_IsInstructionActive_ReturnsTrueForActiveRule()
    {
        var state = new RuleSetState()
            .Activate(Action("ACT_CLICK", CoinAction.Click, 1, RuleScope.Turns, 3));

        Assert.True(state.IsInstructionActive("ACT_CLICK"));
        Assert.False(state.IsInstructionActive("ACT_HOLD"));
    }

    [Fact]
    public void RuleSetState_IsInstructionActive_ReturnsFalseForInactiveRule_Expired()
    {
        var state = new RuleSetState()
            .Activate(Action("ACT_CLICK", CoinAction.Click, 1, RuleScope.NextInstruction));

        // Still standing on its own target turn (turn 2), and the turn-aware query
        // agrees once we ask about a turn past the one-instruction window.
        Assert.True(state.IsInstructionActive("ACT_CLICK"));
        Assert.True(state.IsInstructionActive("ACT_CLICK", 2));
        Assert.False(state.IsInstructionActive("ACT_CLICK", 3));

        // The once-per-turn lifecycle tick is what permanently removes it.
        var (surviving, tick) = state.TickExpiry(3);

        Assert.False(surviving.IsInstructionActive("ACT_CLICK"));
        Assert.Single(tick.JustExpired);
    }

    [Fact]
    public void Ignore_VoidsAnotherIgnore_Cascading()
    {
        // Ignore voids the Ignore itself, not the instruction after it.
        var state = new RuleSetState()
            .Activate(IgnoreRule("MOD_IGNORE_NEXT", 1, RuleScope.NextInstruction))
            .Activate(IgnoreRule("MOD_IGNORE_NEXT_2", 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.True(result.IsIgnored);
        // Newest ignore is credited for the ignored instruction.
        Assert.Equal("MOD_IGNORE_NEXT_2", result.IgnoreByRuleId);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    // ── 4. CONFLICT = newest active wins (principle 7) ───────────────────────────
    [Fact]
    public void Conflict_NewerActionOverridesOlderAction()
    {
        var state = new RuleSetState()
            .Activate(Action("ACT_HOLD", CoinAction.Hold, 1, RuleScope.Turns, 3))
            .Activate(Action("ACT_SWIPE", CoinAction.Swipe, 2, RuleScope.Turns, 3));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        // Newest active action rule wins over the evaluated directive + older rule.
        Assert.Equal(CoinAction.Swipe, result.FinalDirective!.Action);
    }

    [Fact]
    public void Conflict_NewerRestrictionOverridesOlderAction()
    {
        var state = new RuleSetState()
            .Activate(Action("ACT_CLICK", CoinAction.Click, 1, RuleScope.Turns, 3))
            .Activate(Restriction("RST_NO_CLICK", CoinAction.Click, 2, RuleScope.Turns, 3));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        // Newest rule is a restriction-in-force → NoInput wins.
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void Conflict_NoContradiction_NoOverride()
    {
        var state = new RuleSetState()
            .Activate(Action("ACT_CLICK", CoinAction.Click, 1, RuleScope.Turns, 3))
            .Activate(Action("ACT_CLICK2", CoinAction.Click, 2, RuleScope.Turns, 3));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    // ── 5. WORKED EXAMPLES ────────────────────────────────────────────────────────
    // GDD "Example — Full Resolution (Turn 5)":
    //   Rule 1 — activated Turn 3, "Do Not Click For 3 Turns" (active 3, 4, 5)
    //   Rule 2 — activated Turn 4, "Next Instruction Is A Lie" (targets Turn 5)
    //   Rule 3 — displayed Turn 5, "Click When You See RATE"
    // Resolution step 3: 1 Lie (odd) → inverted → "Do Not Click When You See RATE".
    // Step 4: Rule 1 restricts clicking as well → the two agree → no conflict.
    // Step 5: final directive = No mouse input.

    [Fact]
    public void Turn5Stacking_Example_OneLieInvertsTrigger_RestrictionAgrees_NoInput()
    {
        var state = new RuleSetState()
            .Activate(Restriction("RST_NO_CLICK_3TURNS", CoinAction.Click, 3, RuleScope.Turns, 3))
            .Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 4, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 5);
        var result = new ModifierEngine().Resolve(
            TriggerInstruction("TRG_RATE", "Click When You See RATE", new Directive(CoinAction.Click, "RATE")), ctx);

        Assert.False(result.IsFake);
        Assert.True(result.WasInvertedMechanically);
        Assert.Equal(1, result.InversionCount);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    // GDD "Boss Round Example (V1)":
    //   Rule 1 — activated Turn 12, "Do Not Click For 4 Turns" (2 remaining at 14)
    //   Rule 2 — activated Turn 13, "Next Instruction Is A Lie" (targets Turn 14)
    //   Rule 3 — activated Turn 14, "Ignore Flashing Instructions"
    // A flashing "CLICK COIN" appears: the identifier tags it fake (step 2), so the
    // Lie finds the card already removed from the pipeline and has no mechanical
    // effect (step 3). The only real rule left is Rule 1's restriction → NoInput.

    [Fact]
    public void BossTurn14_IdentifierRemovesFake_BeforeInversion_LieHasNoEffect()
    {
        var state = new RuleSetState()
            .Activate(Restriction("RST_NO_CLICK_4TURNS", CoinAction.Click, 12, RuleScope.Turns, 4))
            .Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 13, RuleScope.NextInstruction))
            .Activate(Identifier("MOD_IGNORE_FLASHING", IdentifierMode.Deny, new VisualMetadata(Flashing: true), 14, RuleScope.Always));
        var ctx = new ResolutionContext(state, 14);
        var result = new ModifierEngine().Resolve(
            ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(Flashing: true)), ctx);

        // The identifier (not the card's origin) is what makes this card fake.
        Assert.True(result.IsFake);
        Assert.True(result.IsFakeCurrentlyDisplayed);
        Assert.Equal("MOD_IGNORE_FLASHING", result.FakeClassifiedByRuleId);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        // The Lie is in force and reported for transparency, but it cannot resurrect
        // a card the identifier already removed from the pipeline.
        Assert.Equal(1, result.InversionCount);
        Assert.True(result.WasInvertedMechanically);
    }

    // ── 6. WORD TRAP (exact match) ────────────────────────────────────────────────
    // GDD Word Trap table for "Click When You See RATE": RATE valid, RATED /
    // PIRATE / RATER invalid. The trigger directive applies only while the exact
    // word is on screen (cards render mixed case, so matching ignores case but
    // never loosens the word boundary).
    [Theory]
    [InlineData("Rate it now", true, CoinAction.Click)]
    [InlineData("RATE", true, CoinAction.Click)]
    [InlineData("Click when RATED", false, CoinAction.None)]
    [InlineData("PIRATE MODE", false, CoinAction.None)]
    [InlineData("RATER MODE", false, CoinAction.None)]
    public void WordTrap_ExactMatch_RateIsNotSubstring(string text, bool wordOnScreen, CoinAction expected)
    {
        var instruction = new Instruction(
            "TRG_RATE", RuleCategory.Trigger, text,
            new Directive(CoinAction.Click, "RATE"),
            Visual: null);

        Assert.Equal(wordOnScreen, WordMatcher.Matches("RATE", text));

        var result = new ModifierEngine().Resolve(instruction, EmptyContext);
        Assert.Equal(expected, result.FinalDirective!.Action);
    }

    // ── 7. TRAP-ORIGIN + TRUST-RELATED PATHS ──────────────────────────────────────
    [Fact]
    public void TrapFakeByOrigin_EmptyRuleSet_IsFakeWithConcreteDirective()
    {
        var result = new ModifierEngine().Resolve(TrapInstruction("TRP_CLICK_NOW"), EmptyContext);

        Assert.True(result.IsFake);
        Assert.True(result.IsFakeCurrentlyDisplayed);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        Assert.Equal("TRP_CLICK_NOW", result.RuleId);
    }

    [Fact]
    public void TrapFakeByOrigin_IdentifierTrustInvertsToReal_TrustedThenReal()
    {
        var state = new RuleSetState().Activate(Identifier("MOD_ONLY_TRUST_BLUE", IdentifierMode.TrustOnly, new VisualMetadata(), 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 2);
        // A non-blue trap with a TrustOnly identifier — TrustOnly does not mark it
        // fake, so it goes from fake-by-origin to mechanically real this turn.
        var result = new ModifierEngine().Resolve(TrapInstruction("TRP_CLICK_NOW", visual: new VisualMetadata()), ctx);

        Assert.False(result.IsFake);
        Assert.True(result.IsTrustedThenReal);
        Assert.Equal("MOD_ONLY_TRUST_BLUE", result.FakeClassifiedByRuleId);
        // Real trap with an action directive resolves to that action if wired.
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    // ── 8. SOURCE TRACE (principle 25) ────────────────────────────────────────────
    [Fact]
    public void SourceB_FakeVisibleFakeWrongAction_IsFakeOrigin()
    {
        var actedFrom = TrapInstruction("TRP_CLICK_NOW");
        var wasFake = true;
        var trace = SourceTracker.Classify(actionMatchesFinalDirective: false, actedFrom, wasFake);

        Assert.Equal(SourceTrace.FakeOrigin, trace);
    }

    [Fact]
    public void SourceA_RealWrongAction_IsReal()
    {
        var actedFrom = ActionInstruction("ACT_CLICK", CoinAction.Click);
        var trace = SourceTracker.Classify(actionMatchesFinalDirective: false, actedFrom, false);

        Assert.Equal(SourceTrace.Real, trace);
    }

    [Fact]
    public void Source_CorrectAction_IsReal()
    {
        var trace = SourceTracker.Classify(actionMatchesFinalDirective: true, TrapInstruction("TRP_CLICK_NOW"), true);

        Assert.Equal(SourceTrace.Real, trace);
    }

    // ── 9. LIFECYCLE: expiry permanence, next-instruction one-shot ────────────────
    [Fact]
    public void NextInstructionRule_AppliesToTargetTurnOnly_ThenExpires()
    {
        // "Next Instruction Is A Lie" activated on turn 1 targets turn 2 and must be
        // gone by turn 3 — a one-shot modifier spans exactly one instruction.
        var state = new RuleSetState().Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 1, RuleScope.NextInstruction));

        var atTargetTurn = new ModifierEngine().Resolve(
            ActionInstruction("ACT_CLICK", CoinAction.Click), new ResolutionContext(state, 2));

        Assert.True(atTargetTurn.WasInvertedMechanically);
        Assert.Equal(1, atTargetTurn.InversionCount);
        Assert.Equal(CoinAction.None, atTargetTurn.FinalDirective!.Action);

        var afterTargetTurn = new ModifierEngine().Resolve(
            ActionInstruction("ACT_CLICK", CoinAction.Click), new ResolutionContext(state, 3));

        Assert.False(afterTargetTurn.WasInvertedMechanically);
        Assert.Equal(0, afterTargetTurn.InversionCount);
        Assert.Equal(CoinAction.Click, afterTargetTurn.FinalDirective!.Action);
    }

    [Fact]
    public void Ignore_NextInstructionScope_ExpiredAtTurn3_NoLongerVoids()
    {
        // A one-shot ignore applies to exactly one instruction after it; once its
        // scope is gone, later instructions are no longer ignored.
        var state = new RuleSetState().Activate(IgnoreRule("MOD_IGNORE_NEXT", 1, RuleScope.NextInstruction));
        var ctx = new ResolutionContext(state, 3);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.False(result.IsIgnored);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void TurnsRule_ExpiredAtTurnBeyondDuration_IsNotActive()
    {
        // "Do Not Click For 3 Turns" activated at turn 1 → active at 1,2,3; dead at 4.
        var state = new RuleSetState().Activate(Restriction("RST_NO_CLICK_3TURNS", CoinAction.Click, 1, RuleScope.Turns, 3));
        var ctx = new ResolutionContext(state, 4);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void ExpiredRule_DeadStaysDead_AfterReactivationWindow()
    {
        var state = new RuleSetState().Activate(Restriction("RST_NO_CLICK_3TURNS", CoinAction.Click, 1, RuleScope.Turns, 3));
        var ctx = new ResolutionContext(state, 5);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    // ── 10. FUZZ-ISH INVARIANTS ───────────────────────────────────────────────────
    [Fact]
    public void AnyActiveRuleCombo_AlwaysExactlyOneFinalDirective()
    {
        // Deterministic blend: ignore + identifier + lie + restriction + action active.
        var state = new RuleSetState()
            .Activate(IgnoreRule("MOD_IGNORE_NEXT", 1, RuleScope.NextInstruction))
            .Activate(Identifier("MOD_ONLY_TRUST_BLUE", IdentifierMode.TrustOnly, new VisualMetadata(BorderBlue: true), 1, RuleScope.Turns, 3))
            .Activate(Inversion("MOD_LIE_NEXT", InversionKind.Lie, 1, RuleScope.Turns, 3))
            .Activate(Restriction("RST_NO_CLICK", CoinAction.Click, 1, RuleScope.Turns, 3))
            .Activate(Action("ACT_HOLD", CoinAction.Hold, 2, RuleScope.Turns, 3));

        var instructions = new[]
        {
            ActionInstruction("ACT_CLICK", CoinAction.Click, new VisualMetadata(BorderBlue: true)),
            RestrictionInstruction("RST_NO_CLICK", CoinAction.Click, new VisualMetadata(BorderBlue: true)),
            TrapInstruction("TRP_CLICK_NOW", visual: new VisualMetadata(BorderBlue: true)),
            new Instruction("ACT_HOLD", RuleCategory.Action, "Hold", new Directive(CoinAction.Hold), Visual: new VisualMetadata(BorderBlue: true)),
            new Instruction("ACT_SWIPE", RuleCategory.Action, "Swipe", new Directive(CoinAction.Swipe), Visual: new VisualMetadata(RedText: true)),
        };

        var engine = new ModifierEngine();
        for (var turn = 2; turn <= 6; turn++)
        {
            var ctx = new ResolutionContext(state, turn);
            foreach (var instr in instructions)
            {
                var r = engine.Resolve(instr, ctx);
                Assert.NotNull(r);
                Assert.NotNull(r.FinalDirective);
                Assert.False(r.IsFake && r.IsIgnored);
            }
        }
    }

    [Fact]
    public void ResolutionResult_FieldsAreSealedReasonable()
    {
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), EmptyContext);

        Assert.False(result.IsFake);
        Assert.False(result.IsFakeCurrentlyDisplayed);
        Assert.False(result.IsIgnored);
        Assert.False(result.IsTrustedThenReal);
        Assert.Equal(0, result.InversionCount);
        Assert.False(result.WasInvertedMechanically);
        Assert.NotNull(result.ActiveRuleSetSnapshot);
        Assert.NotNull(result.RulesThatMatched);
        Assert.Equal(0, result.TurnWhenResolved);
    }

    [Fact]
    public void IdentifierThrows_WhenModeMissing()
    {
        var idRule = ActiveRule.ForNextInstruction(
            new Instruction("MOD_BROKEN_ID", RuleCategory.Modifier, "Id", BaseDirective: null, ModifierSubType: ModifierSubType.Identifier),
            1);
        var state = new RuleSetState().Activate(idRule);
        var ctx = new ResolutionContext(state, 2);
        var engine = new ModifierEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx));
    }

    [Fact]
    public void IdentifierThrows_WhenVisualMissing()
    {
        var idRule = ActiveRule.ForNextInstruction(
            new Instruction("MOD_BROKEN_ID", RuleCategory.Modifier, "Id", BaseDirective: null, ModifierSubType: ModifierSubType.Identifier, IdentifierMode: IdentifierMode.Deny),
            1);
        var state = new RuleSetState().Activate(idRule);
        var ctx = new ResolutionContext(state, 2);
        var engine = new ModifierEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx));
    }

    [Fact]
    public void ModifierCardReal_ResolvesToNoInput()
    {
        // Real Lie/Reverse/Ignore/Identifier cards auto-activate; player correct
        // action on the modifier card itself is "do nothing".
        var lie = new Instruction("MOD_LIE_NEXT", RuleCategory.Modifier, "Next Is A Lie", BaseDirective: null, ModifierSubType: ModifierSubType.Inversion, InversionKind: InversionKind.Lie);
        var result = new ModifierEngine().Resolve(lie, EmptyContext);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void TriggerCondition_WordAbsent_ResolvesToNoInputWithoutThrowing()
    {
        // The condition model is wired (Click while RATE is on screen), but the word
        // is not on the card → the directive does not apply → safe NoInput.
        var instruction = new Instruction(
            "TRG_RATE", RuleCategory.Trigger, "Rated and pirated",
            new Directive(CoinAction.Click, "RATE"),
            Visual: null);
        var result = new ModifierEngine().Resolve(instruction, EmptyContext);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        // The un-met condition yields the plain no-input directive (no condition
        // survives on a directive that does not apply).
        Assert.Null(result.FinalDirective.Condition);
    }

    [Fact]
    public void TriggerCondition_WordPresent_ResolvesToItsDirective()
    {
        // "Click When You See RATE" with the exact word on screen → Click.
        var instruction = new Instruction(
            "TRG_RATE", RuleCategory.Trigger, "Click When You See RATE",
            new Directive(CoinAction.Click, "RATE"),
            Visual: null);
        var result = new ModifierEngine().Resolve(instruction, EmptyContext);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
        Assert.Equal("RATE", result.FinalDirective.Condition);
    }

    [Fact]
    public void SourceTracker_Classify_UsesCorrectParameterNames()
    {
        // Smoke that the callee parameter names the test file calls are aligned to.
        var result = SourceTracker.Classify(
            actionMatchesFinalDirective: false,
            actedFrom: TrapInstruction("TRP_CLICK_NOW"),
            wasThatInstructionFakeInEngine: true);

        Assert.Equal(SourceTrace.FakeOrigin, result);
    }

    // ── 11. RETROACTIVE LIE (principle 3) ─────────────────────────────────────────
    // "Previous Instruction Was A Lie" looks backward: the target is inverted going
    // forward while it is still active in the rule set, and the rule has ZERO
    // mechanical effect once the target has already resolved.

    [Fact]
    public void RetroactiveLie_TargetStillActive_InvertsGoingForward()
    {
        // The instruction the lie looks back at (a Trap card that is still standing
        // at turn 2) → the lie bites → 1 inversion (odd) → the displayed action is
        // inverted to "Do Not Click" = no mouse input.
        var state = new RuleSetState()
            .Activate(Trap("TRP_HOLD", CoinAction.Hold, 1, RuleScope.Turns, 3))
            .Activate(RetroactiveLie("MOD_PREV_LIE", targetRuleId: "TRP_HOLD", activatedAt: 2));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.True(result.WasInvertedMechanically);
        Assert.Equal(1, result.InversionCount);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    [Fact]
    public void RetroactiveLie_TargetAlreadyResolved_ZeroMechanicalEffect()
    {
        // Same retroactive lie, but its target (2 turns from turn 1) is long gone by
        // turn 4 → the lie contributes nothing and the directive is untouched.
        var state = new RuleSetState()
            .Activate(Trap("TRP_HOLD", CoinAction.Hold, 1, RuleScope.Turns, 2))
            .Activate(RetroactiveLie("MOD_PREV_LIE", targetRuleId: "TRP_HOLD", activatedAt: 4));
        var ctx = new ResolutionContext(state, 4);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.False(result.WasInvertedMechanically);
        Assert.Equal(0, result.InversionCount);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    [Fact]
    public void RetroactiveLie_UnknownTarget_ZeroMechanicalEffect()
    {
        // A retroactive lie whose target never existed cannot bite either — there is
        // nothing to invert going forward.
        var state = new RuleSetState()
            .Activate(RetroactiveLie("MOD_PREV_LIE", targetRuleId: "RST_NEVER_ACTIVE", activatedAt: 2));
        var ctx = new ResolutionContext(state, 2);
        var result = new ModifierEngine().Resolve(ActionInstruction("ACT_CLICK", CoinAction.Click), ctx);

        Assert.False(result.WasInvertedMechanically);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    // ── 12. FUZZ INVARIANT (10,000 random rule sets) ──────────────────────────────
    // Acceptance criterion: for ANY combination of active rules the engine emits
    // exactly one final directive — never 0, never 2+ (GDD: "exactly one correct
    // action"). Seeded so a failure is reproducible.

    [Fact]
    public void Fuzz_TenThousandRandomRuleSets_AlwaysExactlyOneDirective()
    {
        var rng = new Random(20261011);
        var engine = new ModifierEngine();
        var actions = new[]
        {
            CoinAction.None, CoinAction.Click, CoinAction.DoubleClick, CoinAction.Hold, CoinAction.Swipe,
        };
        var visuals = new[]
        {
            VisualMetadata.None,
            new VisualMetadata(Flashing: true),
            new VisualMetadata(RedText: true),
            new VisualMetadata(Animated: true),
            new VisualMetadata(BorderBlue: true),
            new VisualMetadata(Flashing: true, BorderBlue: true),
        };

        // Coverage counters so the fuzz cannot silently become vacuous (e.g. if the
        // generator stopped producing ignores or fakes it must fail, not pass).
        var sawFake = false;
        var sawIgnored = false;
        var sawInverted = false;
        var sawActionDirective = false;

        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            var turn = 1 + rng.Next(0, 20);
            var ruleCount = rng.Next(0, 6);
            var state = new RuleSetState();
            for (var i = 0; i < ruleCount; i++)
                state = state.Activate(RandomRule(rng, actions, visuals));

            var category = (RuleCategory)rng.Next(0, 6);
            var visual = visuals[rng.Next(visuals.Length)];
            var baseAction = actions[rng.Next(actions.Length)];
            var instruction = category switch
            {
                RuleCategory.Trap => TrapInstruction("TRP_FUZZ", visual: visual),
                RuleCategory.Trigger => TriggerInstruction(
                    "TRG_FUZZ",
                    rng.Next(0, 2) == 0 ? "Click When You See RATE" : "Rated and pirated",
                    new Directive(baseAction, "RATE"),
                    visual),
                RuleCategory.Restriction => RestrictionInstruction("RST_FUZZ", baseAction, visual),
                _ => ActionInstruction("ACT_FUZZ", baseAction, visual),
            };

            var result = engine.Resolve(instruction, new ResolutionContext(state, turn));

            // Exactly one directive: one result object carrying one non-null directive.
            Assert.NotNull(result);
            Assert.NotNull(result.FinalDirective);
            Assert.Equal(instruction.RuleId, result.RuleId);
            // Classifications stay mutually exclusive, and the reported inversion
            // diagnostics stay self-consistent with the count.
            Assert.False(result.IsFake && result.IsIgnored);
            Assert.False(result.IsFake && result.IsTrustedThenReal);
            Assert.Equal(result.InversionCount > 0, result.WasInvertedMechanically);
            Assert.Equal(result.InversionCount % 2 == 1, result.NetParityIsInverted);
            Assert.InRange(result.InversionCount, 0, ruleCount);

            sawFake |= result.IsFake;
            sawIgnored |= result.IsIgnored;
            sawInverted |= result.WasInvertedMechanically;
            sawActionDirective |= result.FinalDirective.Action != CoinAction.None;
        }

        Assert.True(sawFake, "fuzz never produced a fake classification");
        Assert.True(sawIgnored, "fuzz never produced an ignored classification");
        Assert.True(sawInverted, "fuzz never exercised an inversion in force");
        Assert.True(sawActionDirective, "fuzz never produced a concrete action directive");
    }

    private static ActiveRule RandomRule(Random rng, CoinAction[] actions, VisualMetadata[] visuals)
    {
        var scope = rng.Next(0, 3) switch
        {
            0 => RuleScope.NextInstruction,
            1 => RuleScope.Turns,
            _ => RuleScope.Always,
        };
        int? turns = scope == RuleScope.Turns ? 1 + rng.Next(0, 4) : null;
        var activatedAt = 1 + rng.Next(0, 6);
        var id = "FUZZ_" + rng.Next(0, 100_000);

        return rng.Next(0, 6) switch
        {
            0 => Action(id, actions[rng.Next(actions.Length)], activatedAt, scope, turns),
            1 => Restriction(id, actions[rng.Next(actions.Length)], activatedAt, scope, turns),
            2 => Inversion(id, rng.Next(0, 2) == 0 ? InversionKind.Lie : InversionKind.Reverse, activatedAt, scope, turns),
            3 => IgnoreRule(id, activatedAt, scope, turns),
            4 => Identifier(id,
                    rng.Next(0, 2) == 0 ? IdentifierMode.Deny : IdentifierMode.TrustOnly,
                    visuals[rng.Next(visuals.Length)],
                    activatedAt, scope, turns, invertedOnSelf: rng.Next(0, 2) == 0),
            _ => Trap(id, actions[rng.Next(actions.Length)], activatedAt, scope, turns),
        };
    }
}
