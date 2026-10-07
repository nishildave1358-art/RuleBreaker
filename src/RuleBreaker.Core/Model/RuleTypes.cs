namespace RuleBreaker.Core.Model;

/// <summary>
/// The six rule categories from the GDD Rule Library.
/// Distribution is fixed at launch: Action 30, Modifier 25, Restriction 15,
/// Trigger 15, Memory 10, Trap 5 (= 100 base rules).
/// </summary>
public enum RuleCategory
{
    Action,
    Modifier,
    Restriction,
    Trigger,
    Memory,
    Trap,
}

/// <summary>
/// Mouse actions the player can perform on the coin (GDD Input System).
/// <see cref="None"/> is the deliberate no-input action — a first-class decision
/// (implementation ruling R-1: expiry resolves no-input).
/// </summary>
public enum CoinAction
{
    None,
    Click,
    DoubleClick,
    Hold,
    Swipe,
}

/// <summary>
/// Data-facing definition of one base rule (JSON: camelCase). This is the seed of
/// the 100-rule library schema; Phase 4 expands params (scope, triggers, memory,
/// visual metadata) without breaking this contract.
/// </summary>
public sealed record RuleDefinition(
    string Id,
    RuleCategory Category,
    string Text,
    Directive? BaseDirective = null,
    bool IsFakeByOrigin = false,
    int IntroDepth = 1)
{
    /// <summary>Runtime view of this rule as a displayed instruction (1 instruction = 1 turn).</summary>
    public Instruction ToInstruction() => new(Id, Category, Text, BaseDirective, IsFakeByOrigin);
}

/// <summary>
/// One instruction displayed on screen. One instruction = one turn (GDD locked principle 9).
/// </summary>
public sealed record Instruction(
    string RuleId,
    RuleCategory Category,
    string Text,
    Directive? BaseDirective,
    bool IsFakeByOrigin = false);
