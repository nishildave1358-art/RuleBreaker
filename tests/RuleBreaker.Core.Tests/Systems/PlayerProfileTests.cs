namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// GDD principle 37 — the collection survives between sessions. The profile is the
/// persisted model; Phase 11 owns where the file lives.
/// </summary>
public class PlayerProfileTests
{
    private static readonly IReadOnlyList<CardDefinition> Library = TestPaths.LoadCardLibrary(out _);

    private static CardCollection FilledCollection()
    {
        var collection = new CardCollection();
        collection.Add(Library.Single(c => c.Effect == CardEffectKind.Joker), copies: 2);
        collection.Add(Library.Single(c => c.Effect == CardEffectKind.Echo));
        collection.Earn(CurrencyRules.BossRoundReward);
        return collection;
    }

    [Fact]
    public void Profile_RoundTrips_CollectionCurrencyLoadoutScoresAndSettings()
    {
        var collection = FilledCollection();
        var loadout = CardLoadout.Empty
            .With(Library.Single(c => c.Effect == CardEffectKind.Joker))
            .With(Library.Single(c => c.Effect == CardEffectKind.Echo));

        var profile = PlayerProfile.From(
            collection,
            loadout,
            new Dictionary<Difficulty, int> { [Difficulty.Hard] = 12_345 },
            new Dictionary<string, string> { ["hud"] = "off" });

        var reloaded = PlayerProfile.FromJson(profile.ToJson());

        var restored = reloaded.ToCollection(Library, out var unknownCollection);
        Assert.Empty(unknownCollection);
        Assert.Equal(2, restored.CountOf(Library.Single(c => c.Effect == CardEffectKind.Joker).Id));
        Assert.Equal(1, restored.CountOf(Library.Single(c => c.Effect == CardEffectKind.Echo).Id));
        Assert.Equal(CurrencyRules.BossRoundReward, restored.Currency);

        var restoredLoadout = reloaded.ToLoadout(Library, out var unknownLoadout);
        Assert.Empty(unknownLoadout);
        Assert.Equal(2, restoredLoadout.Count);

        Assert.Equal(12_345, reloaded.BestScoresByTier[nameof(Difficulty.Hard)]);
        Assert.Equal("off", reloaded.Settings["hud"]);
    }

    [Fact]
    public void Profile_ReportsIdsTheLibraryNoLongerHas_InsteadOfDroppingThemSilently()
    {
        var profile = new PlayerProfile
        {
            Collection = { ["CRD_RETIRED"] = 3 },
            LastLoadout = { "CRD_RETIRED" },
        };

        var collection = profile.ToCollection(Library, out var unknownCollection);
        var loadout = profile.ToLoadout(Library, out var unknownLoadout);

        Assert.Contains("CRD_RETIRED", unknownCollection);
        Assert.Contains("CRD_RETIRED", unknownLoadout);
        Assert.Equal(0, collection.TotalCopies);
        Assert.True(loadout.IsEmpty);
    }

    [Fact]
    public void Profile_TruncatesAnOverlongSavedLoadout()
    {
        var ids = Library.Take(5).Select(c => c.Id).ToList();
        var profile = new PlayerProfile { LastLoadout = ids };

        var loadout = profile.ToLoadout(Library, out _);

        Assert.Equal(CardLoadout.MaxSlots, loadout.Count);
    }

    [Fact]
    public void Profile_RejectsMalformedOrNegativeSaves()
    {
        Assert.Throws<RuleContentException>(() => PlayerProfile.FromJson(""));
        Assert.Throws<RuleContentException>(() => PlayerProfile.FromJson("{ not json"));
        Assert.Throws<RuleContentException>(
            () => PlayerProfile.FromJson("""{ "currency": -5, "collection": {} }"""));
    }

    [Fact]
    public void Profile_SurvivesAnEmptySave()
    {
        var profile = PlayerProfile.FromJson("""{ "currency": 0, "collection": {} }""");

        Assert.Equal(0, profile.Currency);
        Assert.Empty(profile.Collection);
        Assert.Empty(profile.LastLoadout);
    }
}
