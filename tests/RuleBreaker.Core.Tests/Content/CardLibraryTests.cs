namespace RuleBreaker.Core.Tests.Content;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Model;
using Xunit;

/// <summary>
/// GDD "FULL CARD CLASSIFICATION TABLE" — eight cards, two Auto and six Manual,
/// one per effect, with Freeze at 3 turns and Time Warp at 5 turns / −40%
/// (plan ruling R-10, plan §7 pin 6).
/// </summary>
public class CardLibraryTests
{
    [Fact]
    public void Library_HasTheEightGddCards_AndPassesTheLinter()
    {
        var cards = TestPaths.LoadCardLibrary(out var errors);

        Assert.Empty(errors);
        Assert.Equal(8, cards.Count);

        Assert.Equal(2, cards.Count(c => c.Activation == CardActivation.Auto));
        Assert.Equal(6, cards.Count(c => c.Activation == CardActivation.Manual));

        foreach (var effect in Enum.GetValues<CardEffectKind>())
            Assert.Single(cards, c => c.Effect == effect);

        Assert.Empty(CardLibraryLinter.Validate(cards));
    }

    [Fact]
    public void Library_PinsTheGddParameters()
    {
        var cards = TestPaths.LoadCardLibrary(out var errors);
        Assert.Empty(errors);

        var freeze = Assert.Single(cards, c => c.Effect == CardEffectKind.Freeze);
        Assert.Equal(3, freeze.DurationTurns);

        var warp = Assert.Single(cards, c => c.Effect == CardEffectKind.TimeWarp);
        Assert.Equal(5, warp.DurationTurns);
        Assert.Equal(0.6, warp.TimerFactor, 9);

        // The absorbing pair (principles 41, 42).
        var joker = Assert.Single(cards, c => c.Effect == CardEffectKind.Joker);
        var shield = Assert.Single(cards, c => c.Effect == CardEffectKind.Shield);
        Assert.True(joker.IsAuto && shield.IsAuto);
        Assert.NotNull(joker.VisualCue);
        Assert.NotNull(shield.VisualCue);
    }

    // ── schema ─────────────────────────────────────────────────────────────────
    private static IReadOnlyList<CardDefinition> Loaded()
    {
        var cards = TestPaths.LoadCardLibrary(out var errors);
        Assert.Empty(errors);
        return cards;
    }

    private static CardDefinition Card(IReadOnlyList<CardDefinition> cards, CardEffectKind effect) =>
        cards.Single(c => c.Effect == effect);

    [Fact]
    public void Schema_RejectsAFreezeThatDoesNotLastThreeTurns()
    {
        var freeze = Card(Loaded(), CardEffectKind.Freeze) with { DurationTurns = 2 };

        Assert.Contains(CardSchemaValidator.Validate(freeze), e => e.Contains("exactly 3 turns"));
    }

    [Fact]
    public void Schema_RejectsAWarpThatIsNotFortyPercent()
    {
        var warp = Card(Loaded(), CardEffectKind.TimeWarp) with { TimerFactor = 0.5 };

        Assert.Contains(CardSchemaValidator.Validate(warp), e => e.Contains("−40%"));
    }

    [Fact]
    public void Schema_RejectsParametersAnEffectCannotUse()
    {
        var joker = Card(Loaded(), CardEffectKind.Joker) with { DurationTurns = 3, TimerFactor = 0.6 };
        var errors = CardSchemaValidator.Validate(joker);

        Assert.Contains(errors, e => e.Contains("DurationTurns is not used"));
        Assert.Contains(errors, e => e.Contains("TimerFactor is not used"));
    }

    [Fact]
    public void Schema_KeepsAbsorptionOnAutoCardsOnly()
    {
        var manualJoker = Card(Loaded(), CardEffectKind.Joker) with { Activation = CardActivation.Manual };
        Assert.Contains(CardSchemaValidator.Validate(manualJoker), e => e.Contains("must be an Auto card"));

        var autoFreeze = Card(Loaded(), CardEffectKind.Freeze) with { Activation = CardActivation.Auto };
        Assert.Contains(CardSchemaValidator.Validate(autoFreeze), e => e.Contains("only defined for Joker/Shield"));
    }

    [Fact]
    public void Schema_RejectsAnIdWithoutTheCardPrefix()
    {
        var joker = Card(Loaded(), CardEffectKind.Joker) with { Id = "JOKER" };

        Assert.Contains(CardSchemaValidator.Validate(joker), e => e.Contains("CRD_"));
    }

    // ── linter ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Linter_RejectsAMissingEffect()
    {
        var cards = Loaded().Where(c => c.Effect != CardEffectKind.Echo).ToList();

        Assert.Contains(CardLibraryLinter.Validate(cards), e => e.Contains("effect Echo"));
    }

    [Fact]
    public void Linter_RejectsAWrongActivationSplit()
    {
        var cards = Loaded().ToList();
        var index = cards.FindIndex(c => c.Effect == CardEffectKind.Freeze);
        cards[index] = cards[index] with { Activation = CardActivation.Auto };

        Assert.Contains(CardLibraryLinter.Validate(cards), e => e.Contains("Auto cards"));
    }

    [Fact]
    public void Linter_RejectsADuplicateName()
    {
        var cards = Loaded().ToList();
        var index = cards.FindIndex(c => c.Effect == CardEffectKind.Chaos);
        cards[index] = cards[index] with { Name = "Joker" };

        Assert.Contains(CardLibraryLinter.Validate(cards), e => e.Contains("Duplicate card name"));
    }

    [Fact]
    public void Linter_RejectsAJokerRarerThanTheShield()
    {
        var cards = Loaded().ToList();
        var index = cards.FindIndex(c => c.Effect == CardEffectKind.Joker);
        cards[index] = cards[index] with { Rarity = CardRarity.Rare };
        var shieldIndex = cards.FindIndex(c => c.Effect == CardEffectKind.Shield);
        cards[shieldIndex] = cards[shieldIndex] with { Rarity = CardRarity.Common };

        Assert.Contains(CardLibraryLinter.Validate(cards), e => e.Contains("more common in drops"));
    }

    // ── loader ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Loader_AcceptsSingleObjectsAndArrays()
    {
        var arrayFile = Path.GetTempFileName();
        var objectFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(arrayFile, """
                [
                  { "id": "CRD_A", "name": "A", "activation": "Auto", "effect": "Joker", "text": "a", "visualCue": "flash" },
                  { "id": "CRD_B", "name": "B", "activation": "Auto", "effect": "Shield", "text": "b", "visualCue": "pulse" }
                ]
                """);
            File.WriteAllText(objectFile, """
                { "id": "CRD_C", "name": "C", "activation": "Manual", "effect": "Freeze", "text": "c", "visualCue": "clock", "durationTurns": 3 }
                """);

            Assert.True(CardLibraryLoader.TryLoadFile(arrayFile, out var array, out var arrayErrors));
            Assert.Empty(arrayErrors);
            Assert.Equal(2, array.Count);

            Assert.True(CardLibraryLoader.TryLoadFile(objectFile, out var single, out var objectErrors));
            Assert.Empty(objectErrors);
            Assert.Single(single);
            Assert.Equal(CardEffectKind.Freeze, single[0].Effect);
        }
        finally
        {
            File.Delete(arrayFile);
            File.Delete(objectFile);
        }
    }

    [Fact]
    public void Loader_ReportsABrokenFileInsteadOfThrowing()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ not json");

            Assert.False(CardLibraryLoader.TryLoadFile(file, out _, out var errors));
            Assert.NotEmpty(errors);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
