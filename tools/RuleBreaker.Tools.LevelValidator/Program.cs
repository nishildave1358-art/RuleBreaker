namespace RuleBreaker.Tools.LevelValidator;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;

/// <summary>
/// Phase 0 level/rule validator CLI — the seed of the Phase 8 authoring toolkit.
/// Usage:
///   dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/act_click.json
///   dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/*.json
/// Validates each rule file through the same pipeline the game uses:
/// JSON deserialize → schema validation → engine resolvability.
/// Exit codes: 0 = all valid, 1 = at least one file invalid.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: LevelValidator <rule.json> [more.json ...]");
            return 1;
        }

        var engine = new ModifierEngine();
        var fileCount = 0;
        var errorCount = 0;

        foreach (var path in args)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[ERROR] File not found: {path}");
                errorCount++;
                continue;
            }

            fileCount++;
            var json = File.ReadAllText(path);
            var errors = new List<string>();

            if (!JsonLoader.TryLoad<RuleDefinition>(json, out var rule, out var loadErrors))
            {
                errors.AddRange(loadErrors);
            }
            else
            {
                errors.AddRange(RuleSchemaValidator.Validate(rule!));

                // Resolvability: a real, directive-bearing rule must resolve to
                // exactly one directive through the engine.
                if (errors.Count == 0 && rule!.BaseDirective is not null)
                {
                    try
                    {
                        var result = engine.Resolve(rule.ToInstruction());
                        if (!result.IsFake && result.FinalDirective is null)
                        {
                            errors.Add("Engine produced no directive for a real directive-bearing rule.");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Engine resolution failed: {ex.Message}");
                    }
                }
            }

            if (errors.Count == 0)
            {
                Console.WriteLine($"[OK]   {path}");
            }
            else
            {
                errorCount++;
                Console.WriteLine($"[FAIL] {path}");
                foreach (var error in errors)
                {
                    Console.WriteLine($"       - {error}");
                }
            }
        }

        Console.WriteLine($"Checked {fileCount} file(s), {errorCount} error(s).");
        return errorCount == 0 ? 0 : 1;
    }
}
