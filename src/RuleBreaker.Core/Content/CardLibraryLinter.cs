namespace RuleBreaker.Core.Content;

using RuleBreaker.Core.Model;

/// <summary>
/// Pool-wide lint for the Special Card library. <see cref="CardSchemaValidator"/>
/// judges one card in isolation; this judges the eight as a set:
///
///  • exactly 8 cards, one per <see cref="CardEffectKind"/> (GDD "FULL CARD
///    CLASSIFICATION TABLE") — a missing effect means a mechanic the GDD promises
///    cannot be obtained;
///  • exactly 2 Auto cards and 6 Manual ones (principles 40–43);
///  • unique ids and names;
///  • the Joker is never rarer than the Shield (GDD: "Joker is more accessible and
///    more common in drops").
///
/// Parameter pins are per-card and therefore already covered by the schema
/// validator, which this runs first.
/// </summary>
public static class CardLibraryLinter
{
    /// <summary>The GDD ships eight Special Cards.</summary>
    public const int ExpectedCardCount = 8;

    public static IReadOnlyList<string> Validate(IEnumerable<CardDefinition> cards)
    {
        var all = cards.ToList();
        var errors = new List<string>();

        foreach (var card in all)
            errors.AddRange(CardSchemaValidator.Validate(card));

        CheckCount(all, errors);
        CheckIdentity(all, errors);
        CheckEffectCoverage(all, errors);
        CheckActivationSplit(all, errors);
        CheckAbsorberRarity(all, errors);

        return errors;
    }

    private static void CheckCount(List<CardDefinition> cards, List<string> errors)
    {
        if (cards.Count != ExpectedCardCount)
            errors.Add($"Library has {cards.Count} cards; the GDD fixes {ExpectedCardCount}.");
    }

    private static void CheckIdentity(List<CardDefinition> cards, List<string> errors)
    {
        foreach (var group in cards.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Duplicate card id '{group.Key}'.");

        foreach (var group in cards.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Duplicate card name '{group.First().Name}' (ids: {string.Join(", ", group.Select(c => c.Id))}).");
    }

    private static void CheckEffectCoverage(List<CardDefinition> cards, List<string> errors)
    {
        foreach (var effect in Enum.GetValues<CardEffectKind>())
        {
            var count = cards.Count(c => c.Effect == effect);
            if (count != 1)
                errors.Add($"Library has {count} card(s) with effect {effect}; the GDD fixes exactly 1.");
        }
    }

    private static void CheckActivationSplit(List<CardDefinition> cards, List<string> errors)
    {
        var auto = cards.Count(c => c.Activation == CardActivation.Auto);
        var manual = cards.Count(c => c.Activation == CardActivation.Manual);

        if (auto != 2)
            errors.Add($"Library has {auto} Auto cards; the GDD fixes 2 (Joker, Shield).");
        if (manual != 6)
            errors.Add($"Library has {manual} Manual cards; the GDD fixes 6.");
    }

    private static void CheckAbsorberRarity(List<CardDefinition> cards, List<string> errors)
    {
        var joker = cards.FirstOrDefault(c => c.Effect == CardEffectKind.Joker);
        var shield = cards.FirstOrDefault(c => c.Effect == CardEffectKind.Shield);
        if (joker is null || shield is null) return; // coverage already reported it

        if (joker.Rarity > shield.Rarity)
            errors.Add("The Joker must not be rarer than the Shield (GDD: Joker is more accessible and more common in drops).");
    }
}
