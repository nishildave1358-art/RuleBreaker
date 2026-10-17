namespace RuleBreaker.Core.Content;

using RuleBreaker.Core.Model;

/// <summary>
/// Structural validation for <see cref="RuleDefinition"/> content.
/// Phase 0 checks are intentionally minimal but enforced; Phase 4 (100-rule
/// authoring) extends this with per-category rules and pool-wide linting.
/// The Level Validator CLI already routes through this class.
/// </summary>
public static class RuleSchemaValidator
{
    public static IReadOnlyList<string> Validate(RuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            errors.Add("Rule Id must not be empty.");
        }
        else if (!IsValidRuleId(rule.Id))
        {
            errors.Add($"Rule Id '{rule.Id}' must be UPPERCASE snake case with a category prefix (e.g. 'ACT_CLICK', 'MOD_LIE_NEXT').");
        }

        if (!Enum.IsDefined(rule.Category))
        {
            errors.Add($"Rule Category '{rule.Category}' is not a valid RuleCategory.");
        }

        if (string.IsNullOrWhiteSpace(rule.Text))
        {
            errors.Add("Rule Text (displayed instruction) must not be empty.");
        }

        // Fake-origin rules are the Trap pool (GDD: 5 trap rules, smallest category).
        if (rule.IsFakeByOrigin && rule.Category != RuleCategory.Trap)
        {
            errors.Add($"Rule '{rule.Id}': IsFakeByOrigin is only valid for the Trap category.");
        }

        if (!rule.IsFakeByOrigin && rule.BaseDirective is null)
        {
            switch (rule.Category)
            {
                case RuleCategory.Action:
                case RuleCategory.Restriction:
                    errors.Add($"Rule '{rule.Id}' ({rule.Category}) must define a BaseDirective.");
                    break;
                // Modifier / Trigger / Memory get directive wiring in Phase 1+;
                // they are allowed to be directive-less in the Phase 0 schema.
            }
        }

        if (rule is { BaseDirective: not null, BaseDirective.Action: CoinAction.None })
        {
            // Restrictions model "Do Not X" as an explicit CoinAction-derived
            // inversion; a rule whose base directive is already NoInput would be
            // ambiguous with inversion results.
            errors.Add($"Rule '{rule.Id}': BaseDirective must not be None; restrictions are modelled as inversions in the engine.");
        }

        if (rule.IntroDepth is < 1 or > 5)
        {
            errors.Add($"Rule '{rule.Id}': IntroDepth must be 1–5 (progression tiers Learning→Mastery).");
        }

        // ── Phase 4 params ──────────────────────────────────────────────────────
        // Trigger word: only a Trigger rule has one, and it is one exact token.
        if (rule.TriggerWord is not null)
        {
            if (rule.Category != RuleCategory.Trigger)
                errors.Add($"Rule '{rule.Id}': TriggerWord is only valid on Trigger rules.");
            else if (rule.TriggerWord.Length == 0 || rule.TriggerWord.Any(c => !char.IsAsciiLetterOrDigit(c)))
                errors.Add($"Rule '{rule.Id}': TriggerWord must be a single alphanumeric token (exact-match word).");
        }
        else if (rule.Category == RuleCategory.Trigger)
        {
            errors.Add($"Rule '{rule.Id}': a Trigger rule must declare TriggerWord (exact-match condition).");
        }

        // Memory: exactly one of store-key / recall-task, and only on Memory rules.
        var hasStore = rule.MemoryStoreKey is not null;
        var hasTask = rule.MemoryTask is not null;
        if ((hasStore || hasTask) && rule.Category != RuleCategory.Memory)
        {
            errors.Add($"Rule '{rule.Id}': MemoryStoreKey/MemoryTask are only valid on Memory rules.");
        }
        else if (rule.Category == RuleCategory.Memory)
        {
            if (hasStore == hasTask)
                errors.Add($"Rule '{rule.Id}': a Memory rule declares exactly one of MemoryStoreKey (store) or MemoryTask (recall).");
            if (hasStore && !Engine.MemoryKeys.All.Contains(rule.MemoryStoreKey!, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Rule '{rule.Id}': MemoryStoreKey '{rule.MemoryStoreKey}' is not one of {string.Join(", ", Engine.MemoryKeys.All)}.");
        }

        // Ignore scope applies to Ignore modifiers only.
        if (rule.IgnoreCategory is not null
            && (rule.Category != RuleCategory.Modifier || rule.ModifierSubType != ModifierSubType.Ignore))
        {
            errors.Add($"Rule '{rule.Id}': IgnoreCategory is only valid on Modifier rules with ModifierSubType Ignore.");
        }

        return errors;
    }

    /// <summary>
    /// Valid rule ID: UPPERCASE snake case, at least two segments, first segment is
    /// the category prefix convention (ACT_, MOD_, RST_, TRG_, MEM_, TRP_).
    /// Examples: ACT_CLICK, MOD_LIE_NEXT, TRP_CLICK_NOW.
    /// </summary>
    private static bool IsValidRuleId(string id)
    {
        const string prefix = "ACT|MOD|RST|TRG|MEM|TRP";
        var segments = id.Split('_');

        if (segments.Length < 2 || segments[0].Length != 3 || !System.Text.RegularExpressions.Regex.IsMatch(segments[0], $"^(?:{prefix})$"))
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || !char.IsAsciiLetterOrDigit(segment[0]) || segment.Any(c => !char.IsAsciiLetterOrDigit(c)))
            {
                return false;
            }
        }

        return true;
    }
}
