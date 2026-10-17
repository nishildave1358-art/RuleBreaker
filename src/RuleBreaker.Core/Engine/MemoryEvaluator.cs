namespace RuleBreaker.Core.Engine;

using RuleBreaker.Core.Model;

/// <summary>The four memory keys the library stores (GDD Memory System).</summary>
public static class MemoryKeys
{
    public const string Number = "number";
    public const string Color = "color";
    public const string Symbol = "symbol";
    public const string Word = "word";

    public static IReadOnlyList<string> All { get; } = [Number, Color, Symbol, Word];
}

/// <summary>
/// Evaluates a <see cref="MemoryTask"/> against the stored value (GDD Memory
/// System). Pure and total: a missing or malformed value makes the task false,
/// never an exception, so a level that omits the store rule cannot crash a turn.
/// </summary>
public static class MemoryEvaluator
{
    public static bool IsSatisfied(MemoryTask task, MemoryBank? bank) => task switch
    {
        MemoryTask.NumberIsOdd => TryNumber(bank) is { } odd && odd % 2 != 0,
        MemoryTask.NumberIsEven => TryNumber(bank) is { } even && even % 2 == 0,
        MemoryTask.ColorIsBlue => Equals(bank, MemoryKeys.Color, "blue"),
        MemoryTask.SymbolIsStar => Equals(bank, MemoryKeys.Symbol, "star"),
        MemoryTask.SymbolIsNotStar =>
            bank?.Recall(MemoryKeys.Symbol) is { } symbol
            && !string.Equals(symbol, "star", StringComparison.OrdinalIgnoreCase),
        MemoryTask.WordIsRate => Equals(bank, MemoryKeys.Word, "rate"),
        _ => throw new ArgumentOutOfRangeException(nameof(task), task, "Unknown memory task."),
    };

    /// <summary>The key a task reads — used by the linter to prove a recall task has a store rule.</summary>
    public static string KeyFor(MemoryTask task) => task switch
    {
        MemoryTask.NumberIsOdd or MemoryTask.NumberIsEven => MemoryKeys.Number,
        MemoryTask.ColorIsBlue => MemoryKeys.Color,
        MemoryTask.SymbolIsStar or MemoryTask.SymbolIsNotStar => MemoryKeys.Symbol,
        MemoryTask.WordIsRate => MemoryKeys.Word,
        _ => throw new ArgumentOutOfRangeException(nameof(task), task, "Unknown memory task."),
    };

    private static bool Equals(MemoryBank? bank, string key, string expected) =>
        string.Equals(bank?.Recall(key), expected, StringComparison.OrdinalIgnoreCase);

    private static int? TryNumber(MemoryBank? bank) =>
        int.TryParse(bank?.Recall(MemoryKeys.Number), out var number) ? number : null;
}
