namespace RuleBreaker.Core.Engine;

using RuleBreaker.Core.Model;

/// <summary>
/// Performs two tiny, pure, deterministic helpers the Modifier Engine delegates to:
///   • matching a trigger/word condition (exact match only — RATED, PIRATE, RATER
///     are NOT RATE; principle 16 intent + impact card reference).
///   • tracing an incorrect action back to a Source A / Source B classification
///     (GDD: fake-action author says "Modifier Engine resolves the correct final
///     directive ... If action is incorrect and traces to a visible fake
///     instruction → Source B"; principle 25/24).
/// These live in their own static types to keep ModifierEngine focused on the
/// priority-order pipeline and to make the word-trap and source tests directly
/// readable.
/// </summary>
public static class WordMatcher
{
    /// <summary>
    /// Exact-match word condition (GDD Word Trap table: RATE valid; RATED,
    /// PIRATE, RATER invalid). A word matches only when it appears as a distinct
    /// token in the instruction text — "RATE" inside "RATED" does NOT match,
    /// because tokenisation is on non-alphanumeric boundaries and the whole token
    /// must equal the trigger word.
    /// </summary>
    /// <remarks>
    /// The whole-token comparison is case-insensitive (ordinal, culture-free):
    /// instruction cards render mixed case, so "Rate it now" still contains the
    /// trigger word RATE. Case folding never loosens the boundary rule — RATED /
    /// RATER / PIRATE remain invalid because they are different tokens.
    /// </remarks>
    public static bool Matches(string? triggerWord, string? text)
    {
        if (triggerWord is null || text is null) return false;
        if (triggerWord.Length == 0) return true; // degenerate: empty condition trivially met
        var tokens = SplitTokens(text);
        foreach (var token in tokens)
            if (string.Equals(token, triggerWord, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static IEnumerable<string> SplitTokens(string text)
    {
        var buf = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c))
            {
                buf.Append(c);
            }
            else
            {
                if (buf.Length > 0)
                {
                    yield return buf.ToString();
                    buf.Clear();
                }
            }
        }
        if (buf.Length > 0) yield return buf.ToString();
    }
}

/// <summary>
/// Post-action source trace (GDD: "After the player acts ... traces source of
/// action (real or fake instruction) for penalty attribution"). Source is only
/// meaningful when the action was incorrect; the engine still computes the
/// classification for every wrong action so the presentation layer can justify
/// whatever penalty it applies.
/// </summary>
public static class SourceTracker
{
    /// <summary>
    /// Exactly one source classification for an incorrect action, per the GDD:
    ///   • Source B — the wrong action traces to a visible fake instruction on
    ///     screen (e.g. the player clicked because a fake "CLICK COIN" card was
    ///     showing and no active Identifier/modifier had made it mechanically real;
    ///     the correct directive was something else).
    ///   • Source A — the wrong action does not trace to any visible fake on
    ///     screen. The player misread or mis-resolved a real instruction.
    /// A result is only classified as FakeOrigin when there is actually a fake
    /// instruction currently displayed AND the engine did NOT override it to
    /// mechanically real in *this* resolution (i.e. it stayed fake through all
    /// priority steps). We detect that from the very instruction the player
    /// reacted to and from the engine's classification of it.
    /// </summary>
    public static SourceTrace Classify(
        bool actionMatchesFinalDirective,
        Instruction actedFrom,
        bool wasThatInstructionFakeInEngine)
    {
        if (actionMatchesFinalDirective) return SourceTrace.Real;
        return wasThatInstructionFakeInEngine
            ? SourceTrace.FakeOrigin
            : SourceTrace.Real;
    }
}

/// <summary>
/// The engine-reported source of an incorrect action. Phase 1 only classifies
/// A vs B; the penalty table itself (difficulty × source → penalty magnitude)
/// is wired in Phase 5.
/// </summary>
public enum SourceTrace
{
    /// <summary>Incorrect action traced to a real instruction misread/badly resolved (normal penalty).</summary>
    Real,
    /// <summary>Incorrect action traced to a visible fake instruction the player reacted to (difficulty-dependent penalty).</summary>
    FakeOrigin,
}
