namespace RuleBreaker.Core.Model;

/// <summary>
/// How an activated rule's lifetime is bounded (Phase 1 model).
/// Turn-based rules count down once per displayed instruction (GDD: real-world
/// seconds are NEVER used for turn-based rule countdowns — "For N Turns"
/// includes the activation turn per the GDD's own countdown table:
/// "Do Not Click For 3 Turns" @Turn 3 → 3/2/1 remaining at turns 3/4/5).
/// </summary>
public enum RuleScope
{
    /// <summary>Lives only until the end of the very next displayed instruction ("Next Instruction Is A Lie").</summary>
    NextInstruction,

    /// <summary>Lives for N total displayed instructions including the turn it activated ("Do Not Click For 3 Turns").</summary>
    Turns,

    /// <summary>Lives until acted against by future mechanics ("Click When You See RATE" persists until conflict/expiry data says otherwise).</summary>
    Always,
}

/// <summary>
/// One rule that has been activated into the live rule set. Immutable: expiry
/// and destruction create new state. Carries its activation turn so conflict
/// resolution can obey "most recently activated wins" (GDD principle 7) and so
/// Retroactive Lie can check whether its target is still active (principle 3).
/// </summary>
public sealed record ActiveRule(
    Instruction Instruction,
    int ActivatedAtTurn,
    RuleScope Scope,
    int? DurationTurns = null,
    string? RetroactiveTargetRuleId = null)
{
    /// <summary>
    /// True when this is a backwards-looking "Previous Instruction Was A Lie" rule
    /// (GDD retroactive-lie rule, principle 3). Such a rule has no forward-scoped
    /// target of its own: it only bites while the rule it looks back at is still
    /// alive in the rule set.
    /// </summary>
    public bool IsRetroactiveLie =>
        Instruction.ModifierSubType == ModifierSubType.Inversion
        && Instruction.InversionKind == InversionKind.RetroactiveLie;

    /// <summary>Rule lifecycle (principles 4, 11): expired rules are permanently removed. Dead rules stay dead.</summary>
    /// <remarks>
    /// NextInstruction scope spans exactly the ONE instruction AFTER activation
    /// (GDD "Next Instruction Is A Lie" activated on turn 1 must still be in force
    /// when turn 2 resolves, and must be gone by turn 3). Using
    /// <c>currentTurn &gt; ActivatedAtTurn</c> would expire it before its own target
    /// turn ever resolves, so the bound is <c>ActivatedAtTurn + 1</c>.
    /// </remarks>
    public bool HasExpired(int currentTurn) => Scope switch
    {
        RuleScope.Turns when DurationTurns is int d => currentTurn > ActivatedAtTurn + d - 1,
        RuleScope.NextInstruction => currentTurn > ActivatedAtTurn + 1,
        _ => false,
    };

    /// <summary>
    /// Remaining displayed instructions for a turn-scoped rule (the HUD's amber
    /// countdown). Null for rules with no countdown. \"Do Not Click For 3 Turns\"
    /// activated on turn 3 reads 3 / 2 / 1 on turns 3 / 4 / 5 and 0 from turn 6,
    /// matching the GDD's own countdown table (principles 10, 11).
    /// </summary>
    public int? RemainingTurns(int currentTurn) => Scope switch
    {
        RuleScope.Turns when DurationTurns is int d => Math.Max(0, ActivatedAtTurn + d - currentTurn),
        RuleScope.NextInstruction => currentTurn <= ActivatedAtTurn + 1 ? 1 : 0,
        _ => null,
    };

    public static ActiveRule ForNextInstruction(Instruction instruction, int activatedAtTurn) =>
        new(instruction, activatedAtTurn, RuleScope.NextInstruction);

    public static ActiveRule ForTurns(Instruction instruction, int activatedAtTurn, int turns) =>
        new(instruction, activatedAtTurn, RuleScope.Turns, turns);

    public static ActiveRule Always(Instruction instruction, int activatedAtTurn) =>
        new(instruction, activatedAtTurn, RuleScope.Always);

    /// <summary>
    /// Activate "Previous Instruction Was A Lie" (principle 3) against the rule it
    /// looks back at. The target is recorded by rule id so the engine can ask
    /// <see cref="RuleSetState.IsInstructionActive"/> whether it is still standing:
    /// forward-only inversion while it is, zero mechanical effect once it is gone.
    /// </summary>
    public static ActiveRule ForRetroactiveLie(
        Instruction instruction,
        int activatedAtTurn,
        string targetRuleId) =>
        new(instruction, activatedAtTurn, RuleScope.Always, null, targetRuleId);
}

/// <summary>
/// Output of one RuleSet tick: which rules survive into the next turn.
/// Checked on every turn close — countdowns decrement once per instruction
/// displayed and never by seconds.
/// </summary>
public sealed record RuleSetTickResult(
    IReadOnlyList<ActiveRule> SurvivingRules,
    IReadOnlyList<ActiveRule> JustExpired);
