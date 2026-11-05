namespace RuleBreaker.Core.Content;

using System.Text.Json;
using RuleBreaker.Core.Model;

/// <summary>
/// Loads the Special Card library. Same shape as <see cref="RuleLibraryLoader"/>:
/// a file may hold a single card object or an array of cards, so the authoring
/// format can grow without breaking existing files. Errors are collected, never
/// swallowed — a broken card file must fail loudly (plan engineering rule 2).
/// </summary>
public static class CardLibraryLoader
{
    public static bool TryLoadFile(
        string path,
        out IReadOnlyList<CardDefinition> cards,
        out IReadOnlyList<string> errors)
    {
        cards = Array.Empty<CardDefinition>();
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

        if (json.TrimStart().StartsWith('['))
        {
            try
            {
                cards = JsonSerializer.Deserialize<List<CardDefinition>>(json, JsonLoader.Options)
                        ?? (IReadOnlyList<CardDefinition>)Array.Empty<CardDefinition>();
                return true;
            }
            catch (JsonException ex)
            {
                errors = new[] { $"Invalid JSON in '{path}': {ex.Message}" };
                return false;
            }
        }

        if (!JsonLoader.TryLoad<CardDefinition>(json, out var single, out var loadErrors))
        {
            errors = loadErrors.Select(e => $"'{path}': {e}").ToArray();
            return false;
        }

        cards = new[] { single! };
        return true;
    }

    /// <summary>Load every path, applying per-card schema validation as it goes.</summary>
    public static IReadOnlyList<CardDefinition> LoadAll(
        IEnumerable<string> paths,
        out IReadOnlyList<string> errors)
    {
        var all = new List<CardDefinition>();
        var problems = new List<string>();

        foreach (var path in paths)
        {
            if (!TryLoadFile(path, out var cards, out var fileErrors))
            {
                problems.AddRange(fileErrors);
                continue;
            }

            foreach (var card in cards)
            {
                var cardErrors = CardSchemaValidator.Validate(card);
                if (cardErrors.Count > 0)
                    problems.AddRange(cardErrors.Select(e => $"{path}: {e}"));
                else
                    all.Add(card);
            }
        }

        errors = problems;
        return all;
    }
}
