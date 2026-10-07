namespace RuleBreaker.Core.Engine;

using RuleBreaker.Core.Model;

/// <summary>
/// The core rule-resolution system (GDD: Modifier Engine).
///
/// PHASE 0 SCOPE — passthrough resolver only. It resolves a single, standalone
/// instruction to exactly one directive and classifies origin (real vs fake).
/// The full pipeline lands in Phase 1 in strict GDD priority order:
///   1. Ignore check (absolute, voids the next instruction of any type)
///   2. Identifier classification (real/fake; inverted identifiers trust fakes)
///   3. Inversion count (Lie + Reverse together; odd = inverted, even = original)
///   4. Conflict resolution (most recently activated rule wins)
///   5. Output exactly one final directive per instruction
///
/// The public contract of this class is stable from Phase 0 onward — presentation
/// and tests build against it, so Phase 1 work happens inside the pipeline.
/// </summary>
public sealed class ModifierEngine
{
    public ResolutionResult Resolve(Instruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        // Fake-origin instructions (Trap category) never produce a directive —
        // they are removed from the pipeline unless an Identifier Rule trusts
        // them (GDD principles 19–23). Classification arrives in Phase 1.
        if (instruction.IsFakeByOrigin)
        {
            return ResolutionResult.Fake(instruction.RuleId);
        }

        // Non-action categories (Modifier/Trigger/Memory) have no standalone
        // directive until the full pipeline exists — Phase 1 wires them in.
        if (instruction.BaseDirective is null)
        {
            throw new InvalidOperationException(
                $"Instruction '{instruction.RuleId}' ({instruction.Category}) has no base directive; " +
                "the full resolution pipeline arrives in Phase 1.");
        }

        return ResolutionResult.Resolved(instruction.RuleId, instruction.BaseDirective);
    }
}

/// <summary>
/// Immutable outcome of resolving one instruction. Invariant enforced from Phase 1:
/// every real instruction yields exactly one FinalDirective; fake-origin
/// instructions yield none (voided from the pipeline).
/// </summary>
public sealed record ResolutionResult(
    string RuleId,
    bool IsFake,
    Directive? FinalDirective,
    int InversionCount = 0)
{
    public static ResolutionResult Resolved(string ruleId, Directive directive) =>
        new(ruleId, IsFake: false, directive);

    public static ResolutionResult Fake(string ruleId) =>
        new(ruleId, IsFake: true, FinalDirective: null);
}
