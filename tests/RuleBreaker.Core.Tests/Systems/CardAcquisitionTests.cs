namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// GDD principles 35, 36 and 48 — the four acquisition channels and the shop.
/// Every roll is seeded, so these tests are deterministic.
/// </summary>
public class CardAcquisitionTests
{
    private static readonly IReadOnlyList<CardDefinition> Pool = TestPaths.LoadCardLibrary(out _);

    private static CardAcquisition NewAcquisition(int seed = 42, double dropRate = CurrencyRules.DefaultDropRate) =>
        new(Pool, new Random(seed), dropRate);

    [Fact]
    public void ComboMilestones_AtTenAndTwenty_GrantOneRandomCard()
    {
        var acquisition = NewAcquisition();

        foreach (var milestone in new[] { ComboMilestone.Ten, ComboMilestone.Twenty })
        {
            var reward = acquisition.ForMilestone(milestone);

            Assert.False(reward.IsChoice);
            Assert.Single(reward.Offered);
            Assert.NotNull(reward.Guaranteed);
            Assert.Equal(CardRewardSource.ComboMilestone, reward.Source);
        }
    }

    [Fact]
    public void ThirtyPlusCombo_OffersARareChoice()
    {
        var reward = NewAcquisition().ForMilestone(ComboMilestone.ThirtyPlus);

        Assert.NotEmpty(reward.Offered);
        Assert.All(reward.Offered, c => Assert.Equal(CardRarity.Rare, c.Rarity));
        Assert.Equal(reward.Offered.Count > 1, reward.IsChoice);
    }

    [Fact]
    public void NoMilestone_IsNotARewardPoint()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewAcquisition().ForMilestone(ComboMilestone.None));
    }

    [Fact]
    public void LevelCompletion_OffersTwoOrThreeDistinctCards()
    {
        var acquisition = NewAcquisition();

        var three = acquisition.ForLevelCompletion();
        Assert.Equal(3, three.Offered.Count);
        Assert.Equal(3, three.Offered.Select(c => c.Id).Distinct().Count());
        Assert.True(three.IsChoice);

        var two = acquisition.ForLevelCompletion(2);
        Assert.Equal(2, two.Offered.Count);
        Assert.True(two.IsChoice);
    }

    [Fact]
    public void LevelCompletion_RejectsAnOfferCountOutsideTwoToThree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewAcquisition().ForLevelCompletion(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewAcquisition().ForLevelCompletion(4));
    }

    [Fact]
    public void BossCompletion_GuaranteesARareCard()
    {
        var reward = NewAcquisition().ForBossCompletion();

        Assert.False(reward.IsChoice);
        Assert.Single(reward.Offered);
        Assert.Equal(CardRarity.Rare, reward.Guaranteed!.Rarity);
        Assert.Equal(CardRewardSource.BossCompletion, reward.Source);
    }

    [Fact]
    public void RandomDrop_RespectsTheConfiguredRate()
    {
        Assert.Null(NewAcquisition(dropRate: 0.0).TryRandomDrop());

        var always = NewAcquisition(dropRate: 1.0).TryRandomDrop();
        Assert.NotNull(always);
        Assert.Single(always!.Offered);
        Assert.Equal(CardRewardSource.RandomDrop, always.Source);
    }

    [Fact]
    public void RandomDrop_IsRoughlyTheConfiguredRateOverManyTurns()
    {
        var acquisition = NewAcquisition(seed: 7, dropRate: 0.2);
        var drops = Enumerable.Range(0, 10_000).Count(_ => acquisition.TryRandomDrop() is not null);

        Assert.InRange(drops, 1_800, 2_200); // 20% ± tolerance
    }

    [Fact]
    public void Shop_SellsACardForEarnedCurrency()
    {
        var acquisition = NewAcquisition();
        var collection = new CardCollection();
        var common = Pool.First(c => c.Rarity == CardRarity.Common);

        collection.Earn(CurrencyRules.CommonCardPrice);

        Assert.True(acquisition.TryPurchase(collection, common.Id));
        Assert.Equal(1, collection.CountOf(common.Id));
        Assert.Equal(0, collection.Currency);
    }

    [Fact]
    public void Shop_RefusesWhenTheBalanceIsShort_AndSpendsNothing()
    {
        var acquisition = NewAcquisition();
        var collection = new CardCollection();
        var rare = Pool.First(c => c.Rarity == CardRarity.Rare);

        collection.Earn(CurrencyRules.RareCardPrice - 1);

        Assert.False(acquisition.TryPurchase(collection, rare.Id));
        Assert.Equal(0, collection.CountOf(rare.Id));
        Assert.Equal(CurrencyRules.RareCardPrice - 1, collection.Currency);
    }

    [Fact]
    public void Shop_RejectsAnUnknownCard()
    {
        Assert.Throws<InvalidOperationException>(
            () => NewAcquisition().TryPurchase(new CardCollection(), "CRD_MISSING"));
    }

    [Fact]
    public void Currency_IsAwardedForGameplayChannels_ButNotForADrop()
    {
        Assert.Equal(CurrencyRules.MilestoneReward, CardAcquisition.CurrencyFor(CardRewardSource.ComboMilestone));
        Assert.Equal(CurrencyRules.LevelCompletionReward, CardAcquisition.CurrencyFor(CardRewardSource.LevelCompletion));
        Assert.Equal(CurrencyRules.BossRoundReward, CardAcquisition.CurrencyFor(CardRewardSource.BossCompletion));
        Assert.Equal(0, CardAcquisition.CurrencyFor(CardRewardSource.RandomDrop));
    }

    // ── granting a reward ──────────────────────────────────────────────────────
    [Fact]
    public void GuaranteedReward_IsGrantedWithoutAChoice()
    {
        var acquisition = NewAcquisition();
        var collection = new CardCollection();
        var reward = acquisition.ForBossCompletion();

        collection.Add(reward);

        Assert.Equal(1, collection.CountOf(reward.Guaranteed!.Id));
    }

    [Fact]
    public void ChoiceReward_RequiresAChosenCard_FromTheOfferedSet()
    {
        var acquisition = NewAcquisition();
        var collection = new CardCollection();
        var reward = acquisition.ForLevelCompletion();

        Assert.Throws<InvalidOperationException>(() => collection.Add(reward));

        var chosen = reward.Offered[0];
        collection.Add(reward, chosen);
        Assert.Equal(1, collection.CountOf(chosen.Id));

        var notOffered = Pool.First(c => reward.Offered.All(o => o.Id != c.Id));
        Assert.Throws<InvalidOperationException>(() => collection.Add(reward, notOffered));
    }
}
