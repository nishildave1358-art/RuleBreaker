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
/// A file may contain one rule object or an array of rules. <c>--library</c> also
/// runs the pool-wide linter (exact 30/25/15/15/10/5 distribution, copy rules,
/// depth gating, memory coverage, resolvability).
/// Exit codes: 0 = all valid, 1 = at least one file or the pool is invalid.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var libraryMode = args.Any(a => a == "--library");
        var paths = args.Where(a => a != "--library").ToArray();

        if (paths.Length == 0)
        {
            Console.Error.WriteLine("Usage: LevelValidator [--library] <rule.json> [more.json ...]");
            return 1;
        }

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
