namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Model;

/// <summary>
/// One session's loadout: at most three cards, selected before the session starts
/// (GDD "PRE-SESSION DECK SELECTION", principle 39). Each slot holds one copy, so
/// bringing two Jokers is two slots — the GDD allows multiple copies of the same
/// card, and consumption is per activation (principle 38).
///
/// Immutable: activation removes a slot by producing a new loadout, which is what
/// lets the between-turns window and the presentation layer share one snapshot.
/// </summary>
public sealed class CardLoadout
{
    /// <summary>"Maximum of 3 cards per session loadout" (principle 39).</summary>
    public const int MaxSlots = 3;

    private readonly List<CardDefinition> _cards;

    private CardLoadout(List<CardDefinition> cards) => _cards = cards;

    public static CardLoadout Empty { get; } = new(new List<CardDefinition>());

    /// <summary>Slots in pick order — the HUD shows them left to right (Phase 9).</summary>
    public IReadOnlyList<CardDefinition> Cards => _cards;

    public int Count => _cards.Count;

    public bool IsEmpty => _cards.Count == 0;

    public int CountOf(string cardId) =>
        _cards.Count(c => string.Equals(c.Id, cardId, StringComparison.OrdinalIgnoreCase));

    public bool Contains(string cardId) => CountOf(cardId) > 0;

    public CardDefinition? Slot(int index) =>
        index >= 0 && index < _cards.Count ? _cards[index] : null;

    /// <summary>Add one copy to the loadout. Throws when all three slots are taken.</summary>
    public CardLoadout With(CardDefinition card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (_cards.Count >= MaxSlots)
            throw new InvalidOperationException(
                $"A session loadout holds at most {MaxSlots} cards (principle 39); " +
                $"cannot add '{card.Name}' to a full loadout.");

        return new CardLoadout(new List<CardDefinition>(_cards) { card });
    }

    /// <summary>Slot in the given card type by index (duplicates are ordered as picked).</summary>
    public CardLoadout With(string cardId, IReadOnlyList<CardDefinition> library) =>
        With(Find(cardId, library));

    private static CardDefinition Find(string cardId, IReadOnlyList<CardDefinition> library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return library.FirstOrDefault(c => string.Equals(c.Id, cardId, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Unknown card id '{cardId}'.");
    }

    /// <summary>Remove exactly one copy (consumption on activation, principle 38).</summary>
    public CardLoadout Without(string cardId)
    {
        var index = _cards.FindIndex(c => string.Equals(c.Id, cardId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException($"Card '{cardId}' is not in the loadout.");

        var next = new List<CardDefinition>(_cards);
        next.RemoveAt(index);
        return new CardLoadout(next);
    }
}

/// <summary>
/// The player's permanent card collection and in-game currency (GDD "CARD
/// PERSISTENCE", principles 35–39, 36).
///
///   • cards persist between sessions (principle 37);
///   • one copy is consumed per activation (principle 38), so copies are counts,
///     not a set;
///   • unused loadout cards come back (principle 39);
///   • currency is <em>earned through gameplay only</em> (principle 36) — the type
///     deliberately has no real-money path, only <see cref="Earn"/>.
/// </summary>
public sealed class CardCollection
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CardDefinition> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Currency balance (principle 36 — earned, never bought).</summary>
    public int Currency { get; private set; }

    /// <summary>How many different cards the player owns at least one copy of.</summary>
    public int DistinctCards => _counts.Count;

    /// <summary>Total copies across all card types.</summary>
    public int TotalCopies => _counts.Values.Sum();

    /// <summary>Owned copies per card id (read-only view for the collection screen).</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    public int CountOf(string cardId) =>
        _counts.TryGetValue(cardId, out var count) ? count : 0;

    public bool Owns(string cardId) => CountOf(cardId) > 0;

    /// <summary>The definition of a card the player has seen (collection screen copy).</summary>
    public CardDefinition? DefinitionOf(string cardId) =>
        _known.TryGetValue(cardId, out var card) ? card : null;

    /// <summary>Add copies (awarded reward, shop purchase, unused loadout return).</summary>
    public void Add(CardDefinition card, int copies = 1)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (copies <= 0)
            throw new ArgumentOutOfRangeException(nameof(copies), copies, "Adding a card requires at least one copy.");

        _known[card.Id] = card;
        _counts[card.Id] = CountOf(card.Id) + copies;
    }

    public void Add(CardReward reward, CardDefinition? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(reward);

        var card = chosen ?? reward.Guaranteed
            ?? throw new InvalidOperationException(
                $"Reward from {reward.Source} offers a choice; pass the chosen card.");

        if (!reward.Offered.Any(o => string.Equals(o.Id, card.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"'{card.Name}' was not one of the cards offered by the {reward.Source} reward.");

        Add(card);
    }

    /// <summary>Consume one copy (principle 38). Returns false when the player has none.</summary>
    public bool TryConsume(string cardId)
    {
        var count = CountOf(cardId);
        if (count <= 0) return false;

        if (count == 1) _counts.Remove(cardId);
        else _counts[cardId] = count - 1;
        return true;
    }

    /// <summary>
    /// Build a session loadout from card ids. Validates every rule the GDD states:
    /// at most three cards, every card owned, and never more copies than owned.
    /// </summary>
    public CardLoadout BuildLoadout(IEnumerable<string> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);

        var loadout = CardLoadout.Empty;
        foreach (var id in cardIds)
        {
            if (!_known.TryGetValue(id, out var card))
                throw new InvalidOperationException($"Unknown card id '{id}'.");

            if (loadout.Count >= CardLoadout.MaxSlots)
                throw new InvalidOperationException(
                    $"A session loadout holds at most {CardLoadout.MaxSlots} cards (principle 39).");

            // Count as we go: two copies of a card the player owns once is invalid.
            if (loadout.CountOf(id) + 1 > CountOf(id))
                throw new InvalidOperationException(
                    $"The loadout asks for {loadout.CountOf(id) + 1} copies of '{card.Name}' but the collection has {CountOf(id)}.");

            loadout = loadout.With(card);
        }

        return loadout;
    }

    /// <summary>
    /// Give back everything still in the loadout (principle 39: "cards not used
    /// during the session are returned to the collection — not consumed").
    /// </summary>
    public void ReturnUnused(CardLoadout loadout)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        foreach (var card in loadout.Cards) Add(card);
    }

    /// <summary>Earn currency through gameplay (principle 36).</summary>
    public void Earn(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Currency cannot be earned negatively.");
        Currency += amount;
    }

    /// <summary>Spend earned currency. Returns false when the balance is short.</summary>
    public bool TrySpend(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Currency cannot be spent negatively.");
        if (amount > Currency) return false;

        Currency -= amount;
        return true;
    }
}

/// <summary>
/// The card economy defaults (plan §7 — Phase 14 re-pins these; nothing here is a
/// hidden literal in gameplay code).
///
/// Earnings follow the GDD's list of gameplay sources (levels, combo milestones,
/// boss rounds — principle 36); prices are the "shop" channel (principle 35) and
/// the drop rate is the GDD's deliberately low Method-3 rate.
/// </summary>
public static class CurrencyRules
{
    /// <summary>Currency awarded for a combo milestone (10/20/30+).</summary>
    public const int MilestoneReward = 10;

    /// <summary>Currency awarded for completing a level.</summary>
    public const int LevelCompletionReward = 25;

    /// <summary>Currency awarded for completing a Boss Round.</summary>
    public const int BossRoundReward = 100;

    /// <summary>Shop price of a common card.</summary>
    public const int CommonCardPrice = 50;

    /// <summary>Shop price of a rare card.</summary>
    public const int RareCardPrice = 120;

    /// <summary>Method 3's low drop rate (per settled turn).</summary>
    public const double DefaultDropRate = 0.05;

    public static int PriceFor(CardRarity rarity) => rarity switch
    {
        CardRarity.Common => CommonCardPrice,
        CardRarity.Rare => RareCardPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Unknown card rarity."),
    };
}
