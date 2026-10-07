namespace RuleBreaker.Core.Content;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Loads all game content (rules, cards, levels, pacing tables) from JSON.
/// Engineering rule 2 of the plan: content is data, never code.
/// Errors are collected, never swallowed — a broken content file must fail loudly.
/// </summary>
public static class JsonLoader
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads and deserializes, throwing a descriptive <see cref="RuleContentException"/> on failure.</summary>
    public static T Load<T>(string json) where T : class
    {
        if (!TryLoad<T>(json, out var value, out var errors))
        {
            throw new RuleContentException(
                $"Failed to load {typeof(T).Name} from JSON:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }

        return value!;
    }

    /// <summary>Attempted load; returns false and collects human-readable errors instead of throwing.</summary>
    public static bool TryLoad<T>(string json, out T? value, out IReadOnlyList<string> errors) where T : class
    {
        value = null;
        errors = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(json))
        {
            errors = new[] { "JSON input was null, empty, or whitespace." };
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException ex)
        {
            errors = new[] { $"Invalid JSON: {ex.Message}" };
            return false;
        }

        if (value is null)
        {
            errors = new[] { "JSON input deserialized to null (was the document literally \"null\"?)." };
            return false;
        }

        return true;
    }
}

/// <summary>Thrown when game content fails to load or validate.</summary>
public sealed class RuleContentException : Exception
{
    public RuleContentException(string message) : base(message) { }
}
