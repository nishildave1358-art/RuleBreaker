namespace RuleBreaker.Core.Model;

/// <summary>
/// Short-term memory for the Memory Rules (GDD Memory System). A Memory Rule
/// stores the value shown on its turn (number / color / symbol / word) under a
/// key; later rules ask about that key ("Click If Number Was Odd").
///
/// Keys are case-insensitive and values are kept verbatim. A missing key is never
/// treated as a match: an untaught memory cannot fire, so a level that skips the
/// store rule fails closed rather than rewarding a guess.
/// </summary>
public sealed class MemoryBank
{
    private readonly Dictionary<string, string> _values =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Read-only view for HUD/debugging (GDD: recalled value in amber).</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    public int Count => _values.Count;

    /// <summary>Store the value displayed on a Memory Rule's turn.</summary>
    public void Store(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A memory key must not be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A memory value must not be empty.", nameof(value));

        _values[key] = value.Trim();
    }

    /// <summary>Apply an engine-reported store (the loop records these after resolution).</summary>
    public void Apply(MemoryWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        Store(write.Key, write.Value);
    }

    public string? Recall(string key) =>
        _values.TryGetValue(key, out var value) ? value : null;

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>Forget everything (new level / new run).</summary>
    public void Clear() => _values.Clear();

    public MemoryBank Clone()
    {
        var clone = new MemoryBank();
        foreach (var (key, value) in _values) clone._values[key] = value;
        return clone;
    }
}

/// <summary>A memory write the engine reports so the caller can persist it (pure resolution, explicit side effect).</summary>
public sealed record MemoryWrite(string Key, string Value);
