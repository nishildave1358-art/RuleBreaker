namespace RuleBreaker.Core.Model;

/// <summary>
/// The live rule set ordered by activation (oldest → newest). This is the state the
/// Modifier Engine reads on every turn. Mutations (activation, expiry tick) return
/// new states so the presentation layer can hold immutable snapshots.
///
/// Guarantees the GDD makes of this state:
/// - "most recently activated rule wins" (principle 7) — newest = last in order;
/// - expired rules are permanently removed; no auto-resurrection (principles 4, 11)
///   — the Echo Special Card is the only resurrection path (hook point, Phase 6);
/// - "Previous Instruction Was A Lie" (Retroactive Lie, principle 3) checks
///   activation order here: forward-only if the target is still active, zero
///   mechanical effect if it already resolved (its turns ran out before the
///   retroactive rule activated).
/// </summary>
public sealed class RuleSetState
{
    private readonly List<ActiveRule> _rules;
    private int _expiryTick; // monotonic: proves dead rules stay dead if tick order is misused

    public RuleSetState()
    {
        _rules = new List<ActiveRule>();
    }

    private RuleSetState(List<ActiveRule> rules)
    {
        _rules = rules;
    }

    /// <summary>Backed-up view, ordered oldest → newest.</summary>
    public IReadOnlyList<ActiveRule> OrderedRules => _rules;

    public RuleSetState Activate(ActiveRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var next = new List<ActiveRule>(_rules) { rule };
        return new RuleSetState(next);
    }

    /// <summary>
    /// Run the once-per-instruction lifecycle tick: remove all rules whose turn
    /// countdown has run out. Returns the surviving state plus what just expired.
    /// </summary>
    public (RuleSetState Surviving, RuleSetTickResult Tick) TickExpiry(int currentTurn)
    {
        var surviving = new List<ActiveRule>(_rules.Count);
        var expired = new List<ActiveRule>();
        foreach (var rule in _rules)
        {
            if (rule.HasExpired(currentTurn))
            {
                expired.Add(rule);
            }
            else
            {
                surviving.Add(rule);
            }
        }

        var remaining = surviving.Where(r => r.Scope == RuleScope.Turns).ToList();
        _expiryTick++;
        return (new RuleSetState(surviving), new RuleSetTickResult(surviving, expired));
    }

    /// <summary>
    /// Does a rule with this instruction text (the identity used in GDD examples
    /// like "Rule 1 … Do Not Click For 3 Turns") exist in the *current* live set?
    /// Used by Retroactive Lie (principle 3) to decide forward-only-inversion vs
    /// zero-effect, and by tests for lifecycle checks.
    /// </summary>
    public bool IsInstructionActive(string ruleId) =>
        _rules.Any(r => r.Instruction.RuleId == ruleId);

    /// <summary>
    /// Is the rule with this id still standing at <paramref name="currentTurn"/>?
    /// "Standing" means present in the live set AND not yet expired — a rule whose
    /// countdown has run out has already resolved and is permanently removed
    /// (principles 4, 11). Retroactive Lie (principle 3) asks exactly this question:
    /// forward-only inversion while the target stands, zero mechanical effect once
    /// it does not.
    /// </summary>
    public bool IsInstructionActive(string ruleId, int currentTurn) =>
        _rules.Any(r => r.Instruction.RuleId == ruleId && !r.HasExpired(currentTurn));
}
