namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// GDD "PRE-SESSION DECK SELECTION" and "CARD PERSISTENCE" — principles 36–39:
/// three slots, real copies, consumption on activation, unused cards returned,
/// currency earned only.
/// </summary>
public class CardCollectionTests
{
    private static CardDefinition Card(string id, CardEffectKind effect = CardEffectKind.Freeze) =>
        new(id, id, effect == CardEffectKind.Joker || effect == CardEffectKind.Shield
            ? CardActivation.Auto
            : CardActivation.Manual, effect, CardRarity.Common, "text", VisualCue: "cue");

    [Fact]
    public void Loadout_HoldsAtMostThreeCards()
    {
        var loadout = CardLoadout.Empty
            .With(Card("CRD_A"))
            .With(Card("CRD_B"))
            .With(Card("CRD_C"));

        Assert.Equal(3, loadout.Count);
        Assert.Throws<InvalidOperationException>(() => loadout.With(Card("CRD_D")));
    }

    [Fact]
    public void Loadout_AllowsDuplicateCopies_AsSeparateSlots()
    {
        var joker = Card("CRD_JOKER", CardEffectKind.Joker);
        var loadout = CardLoadout.Empty.With(joker).With(joker);

        Assert.Equal(2, loadout.Count);
        Assert.Equal(2, loadout.CountOf("CRD_JOKER"));

        // One consumed copy leaves the other slot alone (principle 38).
        var after = loadout.Without("CRD_JOKER");
        Assert.Equal(1, after.CountOf("CRD_JOKER"));
    }

    [Fact]
    public void BuildLoadout_RejectsAnUnownedCard()
    {
        var collection = new CardCollection();
        collection.Add(Card("CRD_A"));

        Assert.Throws<InvalidOperationException>(() => collection.BuildLoadout(new[] { "CRD_B" }));
    }

    [Fact]
    public void BuildLoadout_RejectsMoreCopiesThanOwned()
    {
        var collection = new CardCollection();
        collection.Add(Card("CRD_A"));

        Assert.Throws<InvalidOperationException>(
            () => collection.BuildLoadout(new[] { "CRD_A", "CRD_A" }));
    }

    [Fact]
    public void BuildLoadout_RejectsAFourthSlot()
    {
        var collection = new CardCollection();
        foreach (var id in new[] { "CRD_A", "CRD_B", "CRD_C", "CRD_D" }) collection.Add(Card(id));

        Assert.Throws<InvalidOperationException>(
            () => collection.BuildLoadout(new[] { "CRD_A", "CRD_B", "CRD_C", "CRD_D" }));
    }

    [Fact]
    public void Consumption_RemovesExactlyOneCopy()
    {
        var collection = new CardCollection();
        collection.Add(Card("CRD_A"), copies: 2);

        Assert.True(collection.TryConsume("CRD_A"));
        Assert.Equal(1, collection.CountOf("CRD_A"));

        Assert.True(collection.TryConsume("CRD_A"));
        Assert.Equal(0, collection.CountOf("CRD_A"));
        Assert.False(collection.Owns("CRD_A"));

        Assert.False(collection.TryConsume("CRD_A"));
    }

    [Fact]
    public void UnusedLoadoutCards_AreReturned_NotConsumed()
    {
        var collection = new CardCollection();
        collection.Add(Card("CRD_A"));
        collection.Add(Card("CRD_B"));

        var loadout = collection.BuildLoadout(new[] { "CRD_A", "CRD_B" });
        foreach (var card in loadout.Cards) Assert.True(collection.TryConsume(card.Id));

        // Principle 39: the session hands back whatever it did not use.
        Assert.Equal(0, collection.TotalCopies);
        collection.ReturnUnused(loadout);
        Assert.Equal(1, collection.CountOf("CRD_A"));
        Assert.Equal(1, collection.CountOf("CRD_B"));
    }

    [Fact]
    public void Currency_IsEarnedThroughGameplayAndSpent()
    {
        var collection = new CardCollection();

        collection.Earn(CurrencyRules.LevelCompletionReward);
        Assert.Equal(25, collection.Currency);

        Assert.False(collection.TrySpend(30));
        Assert.Equal(25, collection.Currency); // a failed purchase spends nothing

        Assert.True(collection.TrySpend(25));
        Assert.Equal(0, collection.Currency);

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.Earn(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.TrySpend(-1));
    }

    [Fact]
    public void Prices_RiseWithRarity()
    {
        Assert.True(CurrencyRules.PriceFor(CardRarity.Rare) > CurrencyRules.PriceFor(CardRarity.Common));
        Assert.Equal(CurrencyRules.CommonCardPrice, CurrencyRules.PriceFor(CardRarity.Common));
        Assert.Equal(CurrencyRules.RareCardPrice, CurrencyRules.PriceFor(CardRarity.Rare));
    }

    [Fact]
    public void Collection_RemembersCardDefinitions_ForTheCollectionScreen()
    {
        var collection = new CardCollection();
        var joker = Card("CRD_JOKER", CardEffectKind.Joker);
        collection.Add(joker);

        Assert.Same(joker, collection.DefinitionOf("CRD_JOKER"));
        Assert.Equal(1, collection.DistinctCards);
    }
}
