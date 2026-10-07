namespace RuleBreaker.Core.Model;

/// <summary>
/// The single final directive per instruction (GDD: "exactly one correct action").
/// Inverted rules ("Do Not [action]") resolve to <see cref="CoinAction.None"/> —
/// the correct response is no mouse input at all.
/// </summary>
public sealed record Directive(CoinAction Action, string? Condition = null)
{
    /// <summary>Directive meaning: do nothing with the coin.</summary>
    public static readonly Directive NoInput = new(CoinAction.None);

    public override string ToString() => Condition is null ? Action.ToString() : $"{Action} ({Condition})";
}
