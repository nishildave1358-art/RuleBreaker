namespace RuleBreaker.Core.Engine;

using RuleBreaker.Core.Model;

/// <summary>
/// One engine resolution output per instruction (GDD: "outputs exactly one
/// final directive per instruction"). Phase 1 follows the plan's ResolutionResult
/// contract: there is always exactly one FinalDirective — never 0 (incl. fake/
/// ignored results still emit a correct-action directive), never 2+. The contract
/// replaces the Phase 0 "fake = null directive" reading; fakes/ignored still have
/// a real correct directive for source tracing and the visual treatment.
///
/// Classification explanation (plan §5.2):
///   Real       — instruction has valid standing; follow its resolved directive.
///   Fake       — classified fake by an active Identifier Rule (IdTagsFakes).
///   Ignored    — voided by an active Ignore rule (IgnoredStamp visual, no penalty
///                for "reacting" — principle: Ignore voids the instruction; the
///                player sees it but it is mechanically non-existent). Still emits
///                a FinalDirective so the presentation and source systems have
///                something concrete.
///   TrustedThenReal — an identifier-inverted Trust rule made a fake instruction
///                mechanically real (principle 22, 23); treat exactly like Real.
/// </summary>
public sealed record ResolutionResult(
    string RuleId,
    IReadOnlyList<ActiveRule> RulesThatMatched,
    IReadOnlyList<ActiveRule> ActiveRuleSetSnapshot,
    int TurnWhenResolved,
    Directive FinalDirective,
    bool IsFake,
    bool IsFakeCurrentlyDisplayed,
    bool IsIgnored,
    bool IsTrustedThenReal,
    string? FakeClassifiedByRuleId,
    string? IgnoreByRuleId,
    bool IgnoredByCascadeRule,
    bool WasInvertedMechanically,
    int InversionCount,
    VisualMetadata InstructionVisual)
{
    /// <summary>Convenience: the card is mechanically real in this turn (title act applies).</summary>
    public bool IsReal => !IsFake && !IsIgnored;

    /// <summary>
    /// A fake instruction is on screen and still mechanically fake: the player
    /// reacted to something visibly displayed, so an incorrect action traces to it
    /// (Source B, principle 25). A fake that an identifier made mechanically real
    /// (TrustedThenReal) or one that was voided by an Ignore is not a Source B
    /// signal.
    /// </summary>
    public bool IsMechanicallyVisibleFake =>
        IsFakeCurrentlyDisplayed && !IsTrustedThenReal && !IsIgnored;

    /// <summary>
    /// Inversion diagnostics (plan §6 step 3). <see cref="InversionCount"/> is the
    /// number of inversion modifiers in force for this instruction; the derived
    /// net parity is <c>InversionCount % 2 == 1</c> and only an odd parity flips
    /// the directive. <see cref="WasInvertedMechanically"/> reports that the
    /// inversion machinery was engaged at all (count &gt; 0) — it is a
    /// transparency flag for presentation and fuzzing, and is reported for fake
    /// results too (Boss-Turn-14: the Lie is in force but cannot resurrect a fake
    /// already removed by an identifier).
    /// </summary>
    public bool NetParityIsInverted => InversionCount % 2 == 1;

    public static ResolutionResult Fake(string ruleId, Directive correctDirective, VisualMetadata visual, int turn,
        string classifiedByRuleId, IReadOnlyList<ActiveRule> activeSnapshot,
        int inversionCount = 0, bool wasInvertedMechanically = false) =>
        new(ruleId, EmptyRules, activeSnapshot, turn, correctDirective,
            IsFake: true, IsFakeCurrentlyDisplayed: true, IsIgnored: false, IsTrustedThenReal: false,
            FakeClassifiedByRuleId: classifiedByRuleId, IgnoreByRuleId: null, IgnoredByCascadeRule: false,
            WasInvertedMechanically: wasInvertedMechanically, InversionCount: inversionCount, InstructionVisual: visual);

    public static ResolutionResult Ignored(string ruleId, Directive correctDirective, VisualMetadata visual, int turn,
        string ignoreByRuleId, bool cascade, IReadOnlyList<ActiveRule> activeSnapshot) =>
        new(ruleId, EmptyRules, activeSnapshot, turn, correctDirective,
            IsFake: false, IsFakeCurrentlyDisplayed: false, IsIgnored: true, IsTrustedThenReal: false,
            FakeClassifiedByRuleId: null, IgnoreByRuleId: ignoreByRuleId, IgnoredByCascadeRule: cascade,
            WasInvertedMechanically: false, InversionCount: 0, InstructionVisual: visual);

    public static ResolutionResult Real(
        string ruleId,
        Directive finalDirective,
        VisualMetadata visual,
        int turn,
        IReadOnlyList<ActiveRule> activeSnapshot,
        bool wasInvertedMechanically,
        int inversionCount,
        IReadOnlyList<ActiveRule> matchingRules,
        bool trustedThenReal,
        string? trustedThenRealByRuleId) =>
        new(ruleId, matchingRules, activeSnapshot, turn, finalDirective,
            IsFake: false, IsFakeCurrentlyDisplayed: false, IsIgnored: false, IsTrustedThenReal: trustedThenReal,
            FakeClassifiedByRuleId: trustedThenReal ? trustedThenRealByRuleId : null,
            IgnoreByRuleId: null, IgnoredByCascadeRule: false,
            WasInvertedMechanically: wasInvertedMechanically, InversionCount: inversionCount,
            InstructionVisual: visual);

    /// <summary>For scenarios where no active rules match at all (empty state). Mark explicitly.</summary>
    public static ResolutionResult Empty(string ruleId, Directive finalDirective, VisualMetadata visual, int turn) =>
        new(ruleId, EmptyRules, EmptyRules, turn, finalDirective,
            IsFake: false, IsFakeCurrentlyDisplayed: false, IsIgnored: false, IsTrustedThenReal: false,
            FakeClassifiedByRuleId: null, IgnoreByRuleId: null, IgnoredByCascadeRule: false,
            WasInvertedMechanically: false, InversionCount: 0, InstructionVisual: visual);

    /// <summary>Empty lists — cheap reuse, avoids per-result allocation in tests/fuzz.</summary>
    public static IReadOnlyList<ActiveRule> EmptyRules => Array.Empty<ActiveRule>();
}
