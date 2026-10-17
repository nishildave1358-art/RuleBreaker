namespace RuleBreaker.Core.Content;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;

/// <summary>
/// Pool-wide lint for the 100-rule library (plan §6 Phase 4). Schema validation
/// checks one rule on its own; this checks the library as a whole:
///
///  • category counts exactly 30/25/15/15/10/5 (principles 67, 69, 70);
///  • unique ids, unique display copy, category-correct id prefixes;
///  • progression depth per tier (Identifiers 5 / Level 76+, traps 5, memory 4,
///    triggers 2–4, basics 1–3 — principle 32);
///  • every Modifier declares its sub-type and carries that sub-type's data;
///  • Ignore vs Identifier copy is disambiguated (R-7);
///  • each memory recall task has a store rule for its key;
///  • each visual flag occurs on at least one real card, so an Identifier rule has
///    something to classify (R-5/R-6);
///  • every rule resolves through the real Modifier Engine.
/// </summary>
public static class RuleLibraryLinter
{
    public static IReadOnlyDictionary<RuleCategory, int> ExpectedCounts { get; } =
        new Dictionary<RuleCategory, int>
        {
            [RuleCategory.Action] = 30,
            [RuleCategory.Modifier] = 25,
            [RuleCategory.Restriction] = 15,
            [RuleCategory.Trigger] = 15,
            [RuleCategory.Memory] = 10,
            [RuleCategory.Trap] = 5,
        };

    public static IReadOnlyList<string> Validate(IEnumerable<RuleDefinition> rules)
    {
        var all = rules.ToList();
        var errors = new List<string>();

        foreach (var rule in all)
            errors.AddRange(RuleSchemaValidator.Validate(rule));

        CheckCounts(all, errors);
        CheckIdentity(all, errors);
        CheckDepths(all, errors);
        CheckModifierData(all, errors);
        CheckTrapCopy(all, errors);
        CheckIgnoreIdentifierCopy(all, errors);
        CheckTriggerCopy(all, errors);
        CheckMemoryCoverage(all, errors);
        CheckVisualCoverage(all, errors);
        CheckResolvable(all, errors);

        return errors;
    }

    private static void CheckCounts(List<RuleDefinition> rules, List<string> errors)
    {
        foreach (var (category, expected) in ExpectedCounts)
        {
            var actual = rules.Count(r => r.Category == category);
            if (actual != expected)
                errors.Add($"Library has {actual} {category} rules; the GDD fixes {expected}.");
        }

        if (rules.Count != ExpectedCounts.Values.Sum())
            errors.Add($"Library has {rules.Count} rules; the GDD fixes 100.");
    }

    private static void CheckIdentity(List<RuleDefinition> rules, List<string> errors)
    {
        foreach (var group in rules.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Duplicate rule id '{group.Key}'.");

        foreach (var group in rules.GroupBy(r => Normalize(r.Text)).Where(g => g.Count() > 1))
            errors.Add($"Duplicate rule copy '{group.First().Text}' (ids: {string.Join(", ", group.Select(r => r.Id))}).");

        foreach (var rule in rules)
        {
            var expectedPrefix = rule.Category switch
            {
                RuleCategory.Action => "ACT_",
                RuleCategory.Modifier => "MOD_",
                RuleCategory.Restriction => "RST_",
                RuleCategory.Trigger => "TRG_",
                RuleCategory.Memory => "MEM_",
                RuleCategory.Trap => "TRP_",
                _ => string.Empty,
            };

            if (!rule.Id.StartsWith(expectedPrefix, StringComparison.Ordinal))
                errors.Add($"Rule '{rule.Id}' belongs to {rule.Category} but its id does not start with {expectedPrefix}.");
        }
    }

    private static void CheckDepths(List<RuleDefinition> rules, List<string> errors)
    {
        foreach (var rule in rules)
        {
            var isIdentifier = rule.ModifierSubType == ModifierSubType.Identifier;

            if (isIdentifier && rule.IntroDepth != 5)
                errors.Add($"Identifier rule '{rule.Id}' must be depth 5 (Level 76+ — principle 32).");
            if (rule.Category == RuleCategory.Trap && rule.IntroDepth != 5)
                errors.Add($"Trap rule '{rule.Id}' must be depth 5 (introduced last, Level 76+).");
            if (rule.Category == RuleCategory.Memory && rule.IntroDepth != 4)
                errors.Add($"Memory rule '{rule.Id}' must be depth 4 (introduced Levels 51–75).");

            var allowed = rule.Category switch
            {
                RuleCategory.Action => (1, 2),
                RuleCategory.Restriction => (1, 3),
                RuleCategory.Trigger => (2, 4),
                RuleCategory.Modifier => isIdentifier ? (5, 5) : (2, 4),
                _ => (rule.IntroDepth, rule.IntroDepth), // fixed depths checked above
            };

            if (rule.IntroDepth < allowed.Item1 || rule.IntroDepth > allowed.Item2)
                errors.Add($"Rule '{rule.Id}' has IntroDepth {rule.IntroDepth}; {rule.Category} allows {allowed.Item1}–{allowed.Item2}.");
        }
    }

    private static void CheckModifierData(List<RuleDefinition> rules, List<string> errors)
    {
        foreach (var rule in rules.Where(r => r.Category == RuleCategory.Modifier))
        {
            if (rule.ModifierSubType is not { } subType)
            {
                errors.Add($"Modifier rule '{rule.Id}' must declare ModifierSubType.");
                continue;
            }

            switch (subType)
            {
                case ModifierSubType.Inversion when rule.InversionKind is null:
                    errors.Add($"Inversion rule '{rule.Id}' must declare InversionKind (Lie / Reverse / RetroactiveLie).");
                    break;

                case ModifierSubType.Ignore when rule.InversionKind is not null:
                    errors.Add($"Ignore rule '{rule.Id}' must not declare InversionKind.");
                    break;

                case ModifierSubType.Identifier:
                    if (rule.IdentifierMode is null)
                        errors.Add($"Identifier rule '{rule.Id}' must declare IdentifierMode.");
                    if (rule.Visual is null || !HasAnyFlag(rule.Visual))
                        errors.Add($"Identifier rule '{rule.Id}' must declare at least one visual predicate flag.");
                    break;
            }
        }
    }

    private static void CheckTrapCopy(List<RuleDefinition> rules, List<string> errors)
    {
        // R-6: traps may carry any visual metadata — metadata is orthogonal to
        // truth, and real cards carry the same flags, so metadata is never a tell.
        // The only hard rule is that a trap is fake by origin.
        foreach (var trap in rules.Where(r => r.Category == RuleCategory.Trap))
        {
            if (!trap.IsFakeByOrigin)
                errors.Add($"Trap rule '{trap.Id}' must be IsFakeByOrigin.");
        }
    }

    /// <summary>
    /// R-7: both sub-types exist and are mechanically distinct. "Ignore [category]"
    /// voids; "Ignore/Trust [category] ..." classifies fake/real. Their copy must
    /// say which: identifier cards name the instruction surface (Instruction /
    /// Card / Text), ignore cards never do.
    /// </summary>
    private static void CheckIgnoreIdentifierCopy(List<RuleDefinition> rules, List<string> errors)
    {
        var ignores = rules.Where(r => r.ModifierSubType == ModifierSubType.Ignore).ToList();
        var identifiers = rules.Where(r => r.ModifierSubType == ModifierSubType.Identifier).ToList();
        string[] cardWords = ["instruction", "card", "text"];

        foreach (var identifier in identifiers)
        {
            if (!cardWords.Any(w => identifier.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Identifier rule '{identifier.Id}' copy must name the instruction surface (Instruction / Card / Text) to stay distinct from Ignore rules (R-7).");

            foreach (var ignore in ignores)
            {
                if (Normalize(ignore.Text) == Normalize(identifier.Text))
                    errors.Add($"Ignore rule '{ignore.Id}' and Identifier rule '{identifier.Id}' share copy (R-7).");
            }
        }
    }

    private static void CheckTriggerCopy(List<RuleDefinition> rules, List<string> errors)
    {
        var triggers = rules.Where(r => r.Category == RuleCategory.Trigger).ToList();

        foreach (var group in triggers.GroupBy(r => r.TriggerWord!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Trigger word '{group.Key}' is used by more than one rule: {string.Join(", ", group.Select(r => r.Id))}.");

        foreach (var trigger in triggers)
        {
            if (trigger.TriggerWord is { } word && word != word.ToUpperInvariant())
                errors.Add($"Trigger rule '{trigger.Id}' word '{word}' must be UPPERCASE.");

            if (trigger.BaseDirective is null)
                errors.Add($"Trigger rule '{trigger.Id}' must declare the directive it grants.");
        }
    }

    private static void CheckMemoryCoverage(List<RuleDefinition> rules, List<string> errors)
    {
        var memory = rules.Where(r => r.Category == RuleCategory.Memory).ToList();
        var storedKeys = memory.Where(r => r.MemoryStoreKey is not null)
            .Select(r => r.MemoryStoreKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var recall in memory.Where(r => r.MemoryTask is not null))
        {
            var key = MemoryEvaluator.KeyFor(recall.MemoryTask!.Value);
            if (!storedKeys.Contains(key))
                errors.Add($"Memory rule '{recall.Id}' recalls '{key}' but no Memory rule stores that key.");
        }
    }

    private static void CheckVisualCoverage(List<RuleDefinition> rules, List<string> errors)
    {
        var real = rules.Where(r => r.Category != RuleCategory.Trap).ToList();

        if (!real.Any(r => r.Visual?.Flashing == true)) errors.Add("No real card carries Flashing metadata; an Identifier predicate has no target.");
        if (!real.Any(r => r.Visual?.RedText == true)) errors.Add("No real card carries RedText metadata; an Identifier predicate has no target.");
        if (!real.Any(r => r.Visual?.Animated == true)) errors.Add("No real card carries Animated metadata; an Identifier predicate has no target.");
        if (!real.Any(r => r.Visual?.BorderBlue == true)) errors.Add("No real card carries BorderBlue metadata; an Identifier predicate has no target.");
    }

    /// <summary>Every rule must resolve through the real engine — never throw, never emit zero directives.</summary>
    private static void CheckResolvable(List<RuleDefinition> rules, List<string> errors)
    {
        var engine = new ModifierEngine();

        foreach (var rule in rules)
        {
            var instruction = rule.ToInstruction();
            var memory = new MemoryBank();
            memory.Store(MemoryKeys.Number, "7");
            memory.Store(MemoryKeys.Color, "Blue");
            memory.Store(MemoryKeys.Symbol, "Star");
            memory.Store(MemoryKeys.Word, "RATE");

            var context = new ResolutionContext(
                new RuleSetState(),
                CurrentTurn: 1,
                ScreenText: rule.TriggerWord,
                Memory: memory,
                ScreenValue: "7");

            try
            {
                var result = engine.Resolve(instruction, context);
                if (result.FinalDirective is null)
                    errors.Add($"Rule '{rule.Id}' resolved to no directive.");
            }
            catch (Exception ex)
            {
                errors.Add($"Rule '{rule.Id}' failed to resolve: {ex.Message}");
            }
        }
    }

    private static bool HasAnyFlag(VisualMetadata visual) =>
        visual.Animated || visual.Flashing || visual.RedText || visual.BorderBlue;

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
