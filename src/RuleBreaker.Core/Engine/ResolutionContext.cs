namespace RuleBreaker.Core.Engine;

using RuleBreaker.Core.Model;

/// <summary>
/// Everything the Modifier Engine needs to resolve one displayed instruction:
/// the live rule set (ordered oldest → newest, GDD principle 7) plus the Global
/// Turn Counter value for the instruction being resolved (GDD principle 10).
///
/// Phase 4 adds the content-facing inputs:
///  • <see cref="ScreenText"/> — the ambient text visible this turn, used by
///    Trigger Rules for exact word matching. It deliberately does NOT include the
///    trigger card's own text: a card reading "Click When You See RATE" must not
///    satisfy its own condition.
///  • <see cref="Memory"/> — the short-term memory bank recall tasks read.
///  • <see cref="ScreenValue"/> — the value displayed this turn that a Memory
///    store rule records (number/color/symbol/word).
/// </summary>
public sealed record ResolutionContext(
    RuleSetState State,
    int CurrentTurn,
    string? ScreenText = null,
    MemoryBank? Memory = null,
    string? ScreenValue = null)
{
    /// <summary>Empty rule set at turn 0 — Phase 0 compatibility for standalone resolution.</summary>
    public static ResolutionContext Empty { get; } = new(new RuleSetState(), 0);
}
