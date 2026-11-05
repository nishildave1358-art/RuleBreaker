namespace RuleBreaker.Core.Systems;

using System.Text.Json;
using RuleBreaker.Core.Content;
using RuleBreaker.Core.Model;

/// <summary>
/// The persisted player profile (GDD "CARD PERSISTENCE", principle 37; plan §7
/// save shape <c>{ settings, lastLoadout, perTierBestScores }</c>).
///
/// This type owns the <em>model</em>, not the file: Phase 11 decides where the
/// JSON lives (user data dir, cloud sync, etc.). It is deliberately a flat DTO so
/// a save file stays readable and forward-compatible — card copies are counts keyed
/// by card id, and unknown ids are reported on load rather than silently dropped.
///
/// Card definitions are not stored: the library is content, so a save only records
/// which ids the player owns and the library supplies the rest.
/// </summary>
public sealed class PlayerProfile
{
    /// <summary>Owned copies per card id (principle 38: copies, not a set).</summary>
    public Dictionary<string, int> Collection { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Earned currency balance (principle 36).</summary>
    public int Currency { get; set; }

    /// <summary>The loadout chosen last time — pre-selected on the next session (GDD deck selection).</summary>
    public List<string> LastLoadout { get; set; } = [];

    /// <summary>Best score per difficulty tier (principle 33: tiers never compare across).</summary>
    public Dictionary<string, int> BestScoresByTier { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Non-gameplay settings (audio/HUD) — Phase 11 fills these.</summary>
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Snapshot the live collection and the last loadout into a save-able profile.</summary>
    public static PlayerProfile From(
        CardCollection collection,
        CardLoadout? loadout = null,
        IReadOnlyDictionary<Difficulty, int>? bestScores = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var profile = new PlayerProfile
        {
            Currency = collection.Currency,
            LastLoadout = loadout?.Cards.Select(c => c.Id).ToList() ?? [],
        };

        foreach (var (id, count) in collection.Counts)
            profile.Collection[id] = count;

        if (bestScores is not null)
            foreach (var (tier, score) in bestScores)
                profile.BestScoresByTier[tier.ToString()] = score;

        if (settings is not null)
            foreach (var (key, value) in settings)
                profile.Settings[key] = value;

        return profile;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonLoader.Options);

    /// <summary>
    /// Load a profile from JSON. A malformed save fails loudly
    /// (<see cref="RuleContentException"/>), the same contract as content loading.
    /// </summary>
    public static PlayerProfile FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new RuleContentException("A player profile cannot be loaded from empty JSON.");

        PlayerProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<PlayerProfile>(json, JsonLoader.Options);
        }
        catch (JsonException ex)
        {
            throw new RuleContentException($"Invalid player profile JSON: {ex.Message}");
        }

        if (profile is null)
            throw new RuleContentException("The player profile JSON deserialized to null.");

        if (profile.Currency < 0)
            throw new RuleContentException($"A saved profile cannot hold negative currency ({profile.Currency}).");

        return profile;
    }

    /// <summary>
    /// Rebuild the live collection. Copies whose card id is no longer in the
    /// library are reported in <paramref name="unknownCardIds"/> instead of being
    /// dropped silently — a content rename must be visible, not invisible.
    /// </summary>
    public CardCollection ToCollection(
        IReadOnlyList<CardDefinition> library,
        out IReadOnlyList<string> unknownCardIds)
    {
        ArgumentNullException.ThrowIfNull(library);

        var collection = new CardCollection();
        var unknown = new List<string>();

        foreach (var (id, count) in Collection)
        {
            if (count <= 0) continue;

            var card = Find(library, id);
            if (card is null)
            {
                unknown.Add(id);
                continue;
            }

            collection.Add(card, count);
        }

        collection.Earn(Currency);
        unknownCardIds = unknown;
        return collection;
    }

    /// <summary>
    /// Rebuild the last loadout. Ids missing from the library are reported; a
    /// loadout longer than <see cref="CardLoadout.MaxSlots"/> is truncated to the
    /// first three — a save written by an older build can never block a session.
    /// </summary>
    public CardLoadout ToLoadout(
        IReadOnlyList<CardDefinition> library,
        out IReadOnlyList<string> unknownCardIds)
    {
        ArgumentNullException.ThrowIfNull(library);

        var loadout = CardLoadout.Empty;
        var unknown = new List<string>();

        foreach (var id in LastLoadout)
        {
            if (loadout.Count >= CardLoadout.MaxSlots) break;

            var card = Find(library, id);
            if (card is null)
            {
                unknown.Add(id);
                continue;
            }

            loadout = loadout.With(card);
        }

        unknownCardIds = unknown;
        return loadout;
    }

    private static CardDefinition? Find(IReadOnlyList<CardDefinition> library, string id) =>
        library.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}
