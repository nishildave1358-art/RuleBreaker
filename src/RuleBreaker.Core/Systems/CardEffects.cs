namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Model;

/// <summary>
/// What one card activation did (or why it did nothing). <see cref="Applied"/>
/// is the consumption gate: a card whose effect cannot apply is never consumed,
/// so a mistimed or mistargeted click never costs the player a card.
/// </summary>
public sealed record CardEffectResult(
    CardDefinition Card,
    bool Applied,
    string Message,
    ActiveRule? RuleActivated = null,
    ActiveRule? RuleRemoved = null)
{
    public static CardEffectResult Failed(CardDefinition card, string message) =>
        new(card, Applied: false, message);
}

/// <summary>
/// The eight Special Card effect handlers (plan §6 Phase 6; GDD "FULL CARD
/// CLASSIFICATION TABLE"). One handler per <see cref="CardEffectKind"/>; the cards
/// themselves are data, so this class never mentions a card name.
///
/// Joker and Shield are deliberately <em>not</em> handled here: they fire on the
/// failure path (loop step 15) and their only job is to supply the absorbing card
/// id to <see cref="RunState.Settle(TurnOutcome, string?)"/>, which is
/// <see cref="CardSession.ResolveAutoCardCheck"/>'s work.
///
/// Effect rulings recorded here:
///  • Chaos draws from the <see cref="RuleCategory.Modifier"/> pool only — the
///    rule set only holds rules that act on other instructions (inversions,
///    ignores, identifiers). Activating an Action/Restriction/Trigger/Memory
///    instruction would add a member that never resolves. Retroactive lies are
///    excluded because they need a target rule id the card cannot supply.
///  • Mirror and Echo restore with the original scope but a fresh duration
///    anchored at the current turn (R-10: "fresh duration").
///  • Rule Bomb's target is uniformly random across the live set, identifiers
///    included (principle 47).
///  • Echo resurrects only <em>expired</em> rules (principle 4's graveyard); a
///    rule destroyed by a Rule Bomb did not expire and is not offered.
/// </summary>
public sealed class CardEffectEngine
{
    private readonly Random _random;

    public CardEffectEngine(
        IReadOnlyList<RuleDefinition> rulePool,
        Random? random = null,
        int maxIntroDepth = 5)
    {
        RulePool = rulePool ?? throw new ArgumentNullException(nameof(rulePool));

        if (maxIntroDepth is < 1 or > 5)
            throw new ArgumentOutOfRangeException(
                nameof(maxIntroDepth), maxIntroDepth,
                "Progression depth is 1–5 (Learning → Mastery); the generator/replay layer supplies it (principle 32).");

        MaxIntroDepth = maxIntroDepth;
        _random = random ?? new Random();
    }

    public IReadOnlyList<RuleDefinition> RulePool { get; }

    /// <summary>
    /// Deepest progression tier allowed right now. Phase 7's modes and the level
    /// scripts supply it; defaulting to 5 keeps a session with no level context
    /// playable. Identifiers stay gated at depth 5 inside the pool itself.
    /// </summary>
    public int MaxIntroDepth { get; }

    /// <summary>
    /// Apply a Manual card's effect. The caller (<see cref="CardSession"/>) has
    /// already checked the activation window and that the card is in the loadout.
    /// </summary>
    public CardEffectResult Apply(CardDefinition card, GameSession session, string? targetRuleId = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(session);

        if (card.Activation == CardActivation.Auto)
            throw new InvalidOperationException(
                $"'{card.Name}' is an Auto card: it fires during the failure check (loop step 15) and cannot be played manually (principle 40).");

        return card.Effect switch
        {
            CardEffectKind.Freeze => ApplyFreeze(card, session),
            CardEffectKind.Chaos => ApplyChaos(card, session),
            CardEffectKind.Mirror => ApplyMirror(card, session),
            CardEffectKind.Echo => ApplyEcho(card, session, targetRuleId),
            CardEffectKind.TimeWarp => ApplyTimeWarp(card, session),
            CardEffectKind.RuleBomb => ApplyRuleBomb(card, session),
            _ => throw new ArgumentOutOfRangeException(
                nameof(card), card.Effect, "That effect is not a Manual Card effect."),
        };
    }

    private static CardEffectResult ApplyFreeze(CardDefinition card, GameSession session)
    {
        var turns = card.DurationTurns
            ?? throw new InvalidOperationException($"'{card.Name}' has no duration; the schema should have rejected it.");

        session.FreezeNewRules(turns);
        return new CardEffectResult(
            card, true, $"Freeze: no new rules for {turns} turns (turns keep running).");
    }

    private CardEffectResult ApplyChaos(CardDefinition card, GameSession session)
    {
        var candidates = RulePool
            .Where(r => r.Category == RuleCategory.Modifier)
            .Where(r => r.IntroDepth <= MaxIntroDepth)
            .Where(r => r.InversionKind != InversionKind.RetroactiveLie)
            .ToList();

        if (candidates.Count == 0)
            return CardEffectResult.Failed(card, $"Chaos found no Modifier rule at depth ≤ {MaxIntroDepth}.");

        var rule = candidates[_random.Next(candidates.Count)];
        var active = ActivateFresh(session, rule);

        return new CardEffectResult(
            card, true, $"Chaos: '{rule.Text}' became active.", RuleActivated: active);
    }

    private static CardEffectResult ApplyMirror(CardDefinition card, GameSession session)
    {
        var previous = session.PreviousActivatedRule;
        if (previous is null)
            return CardEffectResult.Failed(card, "Mirror had no previous rule to repeat.");

        var copy = Restore(session, previous);
        return new CardEffectResult(
            card, true, $"Mirror: '{copy.Instruction.Text}' activated again.", RuleActivated: copy);
    }

    private static CardEffectResult ApplyEcho(CardDefinition card, GameSession session, string? targetRuleId)
    {
        var dead = session.ExpiredRules;
        if (dead.Count == 0)
            return CardEffectResult.Failed(card, "Echo had no expired rule to restore.");

        if (string.IsNullOrWhiteSpace(targetRuleId))
            return CardEffectResult.Failed(
                card,
                "Echo needs the rule the player chose; expired rules: " +
                string.Join(", ", dead.Select(r => r.Instruction.RuleId).Distinct()));

        var target = dead.LastOrDefault(r =>
            string.Equals(r.Instruction.RuleId, targetRuleId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
            return CardEffectResult.Failed(
                card, $"Echo cannot restore '{targetRuleId}': it is not in the expired-rule list.");

        var restored = Restore(session, target);
        return new CardEffectResult(
            card, true, $"Echo: '{restored.Instruction.Text}' restored.", RuleActivated: restored);
    }

    private static CardEffectResult ApplyTimeWarp(CardDefinition card, GameSession session)
    {
        var turns = card.DurationTurns
            ?? throw new InvalidOperationException($"'{card.Name}' has no duration; the schema should have rejected it.");

        session.ApplyTimerPressure(turns, card.TimerFactor);

        var percent = (int)Math.Round((1 - card.TimerFactor) * 100);
        return new CardEffectResult(
            card, true, $"Time Warp: response windows {percent}% shorter for {turns} turns.");
    }

    private CardEffectResult ApplyRuleBomb(CardDefinition card, GameSession session)
    {
        var active = session.Rules.OrderedRules;
        if (active.Count == 0)
            return CardEffectResult.Failed(card, "Rule Bomb had no active rule to destroy.");

        var removed = session.RemoveRuleAt(_random.Next(active.Count));
        return new CardEffectResult(
            card, true, $"Rule Bomb: '{removed.Instruction.Text}' destroyed.", RuleRemoved: removed);
    }

    /// <summary>
    /// Re-activate a rule with its original scope but a duration starting now
    /// (R-10 "fresh duration"). NextInstruction rules therefore cover the next
    /// instruction, not the one they originally covered.
    /// </summary>
    private static ActiveRule Restore(GameSession session, ActiveRule rule)
    {
        var restored = rule.Scope switch
        {
            RuleScope.Turns => ActiveRule.ForTurns(
                rule.Instruction, session.TurnNumber, rule.DurationTurns ?? 1),
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(rule.Instruction, session.TurnNumber),
            _ => ActiveRule.Always(rule.Instruction, session.TurnNumber),
        };

        session.ActivateRule(restored);
        return restored;
    }

    private static ActiveRule ActivateFresh(GameSession session, RuleDefinition rule)
    {
        var instruction = rule.ToInstruction();
        var scope = rule.DefaultScope ?? DefaultScopeFor(rule);

        var active = scope switch
        {
            RuleScope.Turns => ActiveRule.ForTurns(instruction, session.TurnNumber, rule.SelfDurationTurns ?? 1),
            RuleScope.NextInstruction => ActiveRule.ForNextInstruction(instruction, session.TurnNumber),
            _ => ActiveRule.Always(instruction, session.TurnNumber),
        };

        session.ActivateRule(active);
        return active;
    }

    /// <summary>
    /// Default lifetime for a rule activated by a card, when the definition does not
    /// author its own scope: an inversion or an ignore covers exactly one next
    /// instruction (principles 3, 6), while an identifier classifies for as long as
    /// it stands (principle 21).
    /// </summary>
    private static RuleScope DefaultScopeFor(RuleDefinition rule) =>
        rule.Category == RuleCategory.Modifier && rule.ModifierSubType == ModifierSubType.Identifier
            ? RuleScope.Always
            : RuleScope.NextInstruction;
}
