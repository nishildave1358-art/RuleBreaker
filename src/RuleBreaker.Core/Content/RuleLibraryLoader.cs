namespace RuleBreaker.Core.Content;

using System.Text.Json;
using RuleBreaker.Core.Model;

/// <summary>
/// Loads the 100-rule library. A file may hold a single rule object (the Phase 0
/// seed format) or an array of rules (the Phase 4 category files), so the
/// authoring format can scale without breaking the existing seed files.
/// </summary>
public static class RuleLibraryLoader
{
    public static bool TryLoadFile(
        string path,
        out IReadOnlyList<RuleDefinition> rules,
        out IReadOnlyList<string> errors)
    {
        rules = Array.Empty<RuleDefinition>();
        errors = Array.Empty<string>();

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors = new[] { $"Could not read '{path}': {ex.Message}" };
            return false;
        }

        var trimmed = json.TrimStart();
        if (trimmed.StartsWith('['))
        {
            try
            {
                rules = JsonSerializer.Deserialize<List<RuleDefinition>>(json, JsonLoader.Options)
                        ?? (IReadOnlyList<RuleDefinition>)Array.Empty<RuleDefinition>();
                return true;
            }
            catch (JsonException ex)
            {
                errors = new[] { $"Invalid JSON in '{path}': {ex.Message}" };
                return false;
            }
        }

        if (!JsonLoader.TryLoad<RuleDefinition>(json, out var single, out var loadErrors))
        {
            errors = loadErrors.Select(e => $"'{path}': {e}").ToArray();
            return false;
        }

        rules = new[] { single! };
        return true;
    }

    /// <summary>Load every path and collect both read/parse and schema errors.</summary>
    public static IReadOnlyList<RuleDefinition> LoadAll(
        IEnumerable<string> paths,
        out IReadOnlyList<string> errors)
    {
        var all = new List<RuleDefinition>();
        var problems = new List<string>();

        foreach (var path in paths)
        {
            if (!TryLoadFile(path, out var rules, out var fileErrors))
            {
                problems.AddRange(fileErrors);
                continue;
            }

            foreach (var rule in rules)
            {
                var ruleErrors = RuleSchemaValidator.Validate(rule);
                if (ruleErrors.Count > 0)
                    problems.AddRange(ruleErrors.Select(e => $"{path}: {e}"));
                else
                    all.Add(rule);
            }
        }

        errors = problems;
        return all;
    }
}
