namespace RuleBreaker.Tools.LevelValidator;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;

/// <summary>
/// Rule/level validator CLI — the seed of the Phase 8 authoring toolkit.
/// Usage:
///   dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/act_click.json
///   dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/*.json
///   dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- --library data/rules/*.json
///   dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- --cards data/cards/*.json
/// A file may contain one rule object or an array of rules. <c>--library</c> also
/// runs the pool-wide linter (exact 30/25/15/15/10/5 distribution, copy rules,
/// depth gating, memory coverage, resolvability). <c>--cards</c> switches to the
/// Special Card library and runs the card linter (eight cards, one per effect,
/// 2 Auto / 6 Manual, pinned Freeze and Time Warp parameters).
/// Exit codes: 0 = all valid, 1 = at least one file or the pool is invalid.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var libraryMode = args.Any(a => a == "--library");
        var cardMode = args.Any(a => a == "--cards");
        var paths = args.Where(a => a != "--library" && a != "--cards").ToArray();

        if (paths.Length == 0)
        {
            Console.Error.WriteLine("Usage: LevelValidator [--library] <rule.json> [more.json ...]");
            Console.Error.WriteLine("       LevelValidator --cards <card.json> [more.json ...]");
            return 1;
        }

        if (cardMode) return ValidateCards(paths);

        var engine = new ModifierEngine();
        var allRules = new List<RuleDefinition>();
        var fileCount = 0;
        var fileErrorCount = 0;

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[ERROR] File not found: {path}");
                fileErrorCount++;
                continue;
            }

            fileCount++;

            if (!RuleLibraryLoader.TryLoadFile(path, out var rules, out var loadErrors))
            {
                PrintFailure(path, loadErrors);
                fileErrorCount++;
                continue;
            }

            var problems = new List<string>();
            foreach (var rule in rules)
            {
                var schemaErrors = RuleSchemaValidator.Validate(rule);
                if (schemaErrors.Count > 0)
                {
                    problems.AddRange(schemaErrors);
                    continue;
                }

                problems.AddRange(CheckResolvable(engine, rule));
            }

            if (problems.Count > 0)
            {
                PrintFailure(path, problems);
                fileErrorCount++;
            }
            else
            {
                Console.WriteLine($"[OK]   {path} ({rules.Count} rule(s))");
                allRules.AddRange(rules);
            }
        }

        IReadOnlyList<string> lintErrors = libraryMode && fileErrorCount == 0
            ? RuleLibraryLinter.Validate(allRules)
            : Array.Empty<string>();

        if (libraryMode && fileErrorCount == 0)
        {
            if (lintErrors.Count == 0)
            {
                Console.WriteLine($"[OK]   library: {allRules.Count} rules, distribution 30/25/15/15/10/5");
            }
            else
            {
                Console.WriteLine("[FAIL] library lint");
                foreach (var error in lintErrors) Console.WriteLine($"       - {error}");
            }
        }

        Console.WriteLine(
            $"Checked {fileCount} file(s), {fileErrorCount} file error(s)" +
            (libraryMode ? $", {lintErrors.Count} lint error(s)." : "."));

        return fileErrorCount == 0 && lintErrors.Count == 0 ? 0 : 1;
    }

    /// <summary>Card-library mode: per-file schema, then the pool linter.</summary>
    private static int ValidateCards(IReadOnlyList<string> paths)
    {
        var missing = paths.Where(p => !File.Exists(p)).ToList();
        foreach (var path in missing) Console.Error.WriteLine($"[ERROR] File not found: {path}");
        if (missing.Count > 0) return 1;

        var cards = CardLibraryLoader.LoadAll(paths, out var loadErrors);
        if (loadErrors.Count > 0)
        {
            Console.WriteLine("[FAIL] card files");
            foreach (var error in loadErrors) Console.WriteLine($"       - {error}");
            Console.WriteLine($"Checked {paths.Count} file(s), {loadErrors.Count} error(s).");
            return 1;
        }

        var lintErrors = CardLibraryLinter.Validate(cards);
        if (lintErrors.Count > 0)
        {
            Console.WriteLine("[FAIL] card library lint");
            foreach (var error in lintErrors) Console.WriteLine($"       - {error}");
        }
        else
        {
            Console.WriteLine(
                $"[OK]   card library: {cards.Count} cards, " +
                $"{cards.Count(c => c.Activation == CardActivation.Auto)} auto / " +
                $"{cards.Count(c => c.Activation == CardActivation.Manual)} manual");
        }

        Console.WriteLine(
            $"Checked {paths.Count} file(s), 0 file error(s), {lintErrors.Count} lint error(s).");
        return lintErrors.Count == 0 ? 0 : 1;
    }

    private static IReadOnlyList<string> CheckResolvable(ModifierEngine engine, RuleDefinition rule)
    {
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
            var result = engine.Resolve(rule.ToInstruction(), context);
            return result.FinalDirective is null
                ? new[] { $"Rule '{rule.Id}' resolved to no directive." }
                : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            return new[] { $"Rule '{rule.Id}' failed to resolve: {ex.Message}" };
        }
    }

    private static void PrintFailure(string path, IReadOnlyList<string> errors)
    {
        Console.WriteLine($"[FAIL] {path}");
        foreach (var error in errors) Console.WriteLine($"       - {error}");
    }
}
