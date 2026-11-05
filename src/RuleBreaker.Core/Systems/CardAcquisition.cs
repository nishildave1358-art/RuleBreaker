namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Model;

/// <summary>
/// The GDD's four acquisition channels (principle 35) and the shop, as one
/// deterministic service over a card pool:
///
///   Method 1  combo milestones 10 / 20 / 30+ (principle 48)
///   Method 2  level completion (choice of 2–3) and boss completion (guaranteed)
///   Method 3  low-rate random drop during play
///   Method 4  shop purchase with earned currency only (principle 36)
///
/// Every roll goes through the injected <see cref="Random"/>, so a seeded generator
/// replays the same rewards in tests and fuzz runs. Rewards are described, never
/// granted: the caller applies them with <see cref="CardCollection.Add(CardReward, CardDefinition?)"/>,
/// which is what lets a UI show the choice before anything is committed.
/// </summary>
public sealed class CardAcquisition
{
    /// <summary>GDD Method 2: level completion offers 2–3 cards.</summary>
    public const int MinimumLevelOffers = 2;

    /// <summary>Upper bound of the level-completion choice.</summary>
    public const int MaximumLevelOffers = 3;

    private readonly IReadOnlyList<CardDefinition> _pool;
    private readonly Random _random;

    public CardAcquisition(
        IReadOnlyList<CardDefinition> pool,
        Random? random = null,
        double dropRate = CurrencyRules.DefaultDropRate)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        if (_pool.Count == 0)
            throw new ArgumentException("The card pool must not be empty.", nameof(pool));
        if (dropRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(dropRate), dropRate, "A drop rate is a probability in [0, 1].");

        DropRate = dropRate;
        _random = random ?? new Random();
    }

    public IReadOnlyList<CardDefinition> Pool => _pool;

    /// <summary>Method 3's per-turn chance (Phase 14 tunes it).</summary>
    public double DropRate { get; }

    /// <summary>
    /// Method 1 (principle 48): a random card at 10 and 20; at 30+ a rare card or a
    /// choice of rares. <see cref="ComboMilestone.None"/> is not a reward point, so
    /// asking for one is a caller bug.
    /// </summary>
    public CardReward ForMilestone(ComboMilestone milestone)
    {
        switch (milestone)
        {
            case ComboMilestone.Ten:
            case ComboMilestone.Twenty:
                return new CardReward(new[] { RandomCard(_pool) }, CardRewardSource.ComboMilestone, IsChoice: false);

            case ComboMilestone.ThirtyPlus:
                var rares = _pool.Where(c => c.Rarity == CardRarity.Rare).ToList();
                if (rares.Count == 0)
                    return new CardReward(new[] { RandomCard(_pool) }, CardRewardSource.ComboMilestone, IsChoice: false);

                return new CardReward(
                    Distinct(rares, Math.Min(MaximumLevelOffers, rares.Count)),
                    CardRewardSource.ComboMilestone,
                    IsChoice: rares.Count > 1);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(milestone), milestone, "Only the 10 / 20 / 30+ milestones award a card (principle 48).");
        }
    }

    /// <summary>
    /// Method 2a: level completion offers a choice of 2–3 distinct cards. The player
    /// picks one; the rest are discarded (GDD "Level Completion Card Selection").
    /// </summary>
    public CardReward ForLevelCompletion(int offers = MaximumLevelOffers)
    {
        if (offers is < MinimumLevelOffers or > MaximumLevelOffers)
            throw new ArgumentOutOfRangeException(
                nameof(offers), offers,
                $"A level-completion reward offers {MinimumLevelOffers}–{MaximumLevelOffers} cards.");

        var count = Math.Min(offers, _pool.Count);
        return new CardReward(
            Distinct(_pool, count),
            CardRewardSource.LevelCompletion,
            IsChoice: count > 1);
    }

    /// <summary>
    /// Method 2b: a boss round guarantees a card, "typically higher quality or rarer
    /// than standard drops" — a rare when one exists in the pool.
    /// </summary>
    public CardReward ForBossCompletion()
    {
        var rares = _pool.Where(c => c.Rarity == CardRarity.Rare).ToList();
        var source = rares.Count > 0 ? rares : _pool;
        return new CardReward(new[] { RandomCard(source) }, CardRewardSource.BossCompletion, IsChoice: false);
    }

    /// <summary>
    /// Method 3: roll the low drop rate once per settled turn. Returns null on no
    /// drop, so the caller can keep the notifying UI silent (GDD: corner,
    /// non-intrusive).
    /// </summary>
    public CardReward? TryRandomDrop()
    {
        if (_random.NextDouble() >= DropRate) return null;

        return new CardReward(
            new[] { RandomCard(_pool) }, CardRewardSource.RandomDrop, IsChoice: false);
    }

    /// <summary>
    /// Method 4: buy a specific card with currency earned through gameplay
    /// (principle 36). Returns false when the balance is short; an unknown card id
    /// is a caller bug.
    /// </summary>
    public bool TryPurchase(CardCollection collection, string cardId)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var card = _pool.FirstOrDefault(c => string.Equals(c.Id, cardId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown card id '{cardId}'.");

        if (!collection.TrySpend(CurrencyRules.PriceFor(card.Rarity))) return false;

        collection.Add(card);
        return true;
    }

    /// <summary>
    /// Currency paid alongside an acquisition channel (principle 36 lists levels,
    /// combo milestones and boss rounds; a random drop pays none).
    /// </summary>
    public static int CurrencyFor(CardRewardSource source) => source switch
    {
        CardRewardSource.ComboMilestone => CurrencyRules.MilestoneReward,
        CardRewardSource.LevelCompletion => CurrencyRules.LevelCompletionReward,
        CardRewardSource.BossCompletion => CurrencyRules.BossRoundReward,
        CardRewardSource.RandomDrop => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown reward source."),
    };

    private CardDefinition RandomCard(IReadOnlyList<CardDefinition> source) =>
        source[_random.Next(source.Count)];

    /// <summary>N distinct cards, in a fresh random order.</summary>
    private List<CardDefinition> Distinct(IReadOnlyList<CardDefinition> source, int count) =>
        source.OrderBy(_ => _random.Next()).Take(count).ToList();
}
