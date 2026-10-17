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
/// The three Modifier Rule sub-types (GDD "CORE RULE TYPES → Modifier Rules"):
/// A = Inversion (Lie/Reverse), B = Ignore, C = Identifier (fake classifiers).
/// </summary>
public enum ModifierSubType
{
    Inversion,
    Ignore,
    Identifier,
}

/// <summary>
/// Which precise inversion sub-type a Modifier-Inversion rule implements.
/// Lie and Reverse are processed together by one counting rule (GDD), but they
/// remain distinct authored rules — e.g. "Next Instruction Is A Lie" vs "Reverse
/// Next Instruction" — and Phase 4 references them by separate IDs.
/// </summary>
public enum InversionKind
{
    Lie,
    Reverse,

    /// <summary>
    /// "Previous Instruction Was A Lie" — a backwards-looking inversion
    /// (GDD retroactive-lie rule, principle 3). It inverts its target going
    /// forward while that target is still active in the rule set, and has zero
    /// mechanical effect once the target has resolved/expired.
    /// </summary>
    RetroactiveLie,
}

/// <summary>
/// What a Modifier-Identifier rule asserts about one visual predicate.
/// "Ignore Flashing" maps to <see cref="Deny"/> (category stays fake);
/// "Only Trust Blue" maps to <see cref="TrustOnly"/> (bordered cards are
/// mechanically real; principle 66 — the color IS the mechanical signal).
/// Inversion of a TrustOnly identifier produces "Distrust [category]"
/// (GDD identifier inversion table, "Distrust Blue" row).
/// </summary>
public enum IdentifierMode
{
    /// <summary>Matching category cards are fake ("Ignore Flashing Instructions").</summary>
    Deny,

    /// <summary>Only matching category cards are real ("Only Trust Blue Instruction Cards").</summary>
    TrustOnly,
}

/// <summary>
/// The four visual instruction-card states any instruction may carry
/// (plan §5.1 params.visual; ruling R-5: Identifier Rules classify ALL
/// instruction types, so metadata is defined on every instruction).
/// Metadata is orthogonal to real/fake truth (R-6): both real and trap cards
/// may carry any combination, so metadata alone never reveals fakes without
/// an active Identifier Rule.
/// </summary>
public sealed record VisualMetadata(
    bool Animated = false,
    bool Flashing = false,
    bool RedText = false,
    bool BorderBlue = false)
{
    public static readonly VisualMetadata None = new();
}

/// <summary>
/// The recall task a Memory Rule asks about the value stored earlier (GDD Memory
/// System: "Click If Number Was Odd", "Click If Color Was Blue", "Click If Symbol
/// Was Not Star"). Values live in <see cref="MemoryBank"/>; a task with no stored
/// value is never satisfied, so an untaught memory can never fire.
/// </summary>
public enum MemoryTask
{
    NumberIsOdd,
    NumberIsEven,
    ColorIsBlue,
    SymbolIsStar,
    SymbolIsNotStar,
    WordIsRate,
}

/// <summary>
/// Data-facing definition of one base rule (JSON: camelCase). This is the
/// 100-rule library schema (plan §5.1): directives, visual metadata,
/// progression depth, plus the Phase 4 params — trigger word, memory store key /
/// recall task, and the category an Ignore modifier is scoped to.
/// </summary>
public sealed record RuleDefinition(
    string Id,
    RuleCategory Category,
    string Text,
    Directive? BaseDirective = null,
    bool IsFakeByOrigin = false,
    int IntroDepth = 1,
    ModifierSubType? ModifierSubType = null,
    InversionKind? InversionKind = null,
    IdentifierMode? IdentifierMode = null,
    VisualMetadata? Visual = null,
    int? SelfDurationTurns = null,
    string? TriggerWord = null,
    string? MemoryStoreKey = null,
    MemoryTask? MemoryTask = null,
    RuleCategory? IgnoreCategory = null,
    RuleScope? DefaultScope = null)
{
    /// <summary>
    /// Runtime view of this rule as a displayed instruction. Scope/duration
    /// behavior is activation state, resolved by the engine's rule-set model —
    /// not carried on the displayed card.
    /// </summary>
    public Instruction ToInstruction() => new(
        Id,
        Category,
        Text,
        BaseDirective,
        IsFakeByOrigin,
        ModifierSubType,
        InversionKind,
        IdentifierMode,
        Visual,
        TriggerWord,
        MemoryStoreKey,
        MemoryTask,
        IgnoreCategory);
}

/// <summary>
/// One instruction displayed on screen. One instruction = one turn (GDD locked principle 9).
/// Phase 2 (turn system) is the caller that decides when instructions appear;
/// Phase 1 consumes an already-displayed instruction plus the active rule set.
/// </summary>
public sealed record Instruction(
    string RuleId,
    RuleCategory Category,
    string Text,
    Directive? BaseDirective,
    bool IsFakeByOrigin = false,
    ModifierSubType? ModifierSubType = null,
    InversionKind? InversionKind = null,
    IdentifierMode? IdentifierMode = null,
    VisualMetadata? Visual = null,
    string? TriggerWord = null,
    string? MemoryStoreKey = null,
    MemoryTask? MemoryTask = null,
    RuleCategory? IgnoreCategory = null);
