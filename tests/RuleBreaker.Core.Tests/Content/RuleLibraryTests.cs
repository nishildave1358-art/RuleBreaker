namespace RuleBreaker.Core.Tests.Content;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using Xunit;

/// <summary>
/// GDD principles 67–70: 100 unique base rules in six categories, weighted
/// 30/25/15/15/10/5, with Identifier rules inside the Modifier 25.
/// </summary>
public class RuleLibraryTests
{
    [Fact]
    public void Library_HasTheGddDistribution_AndNoLoadOrSchemaErrors()
    {
        var rules = TestPaths.LoadLibrary(out var errors);

        Assert.Empty(errors);
        Assert.Equal(100, rules.Count);
        Assert.Equal(30, rules.Count(r => r.Category == RuleCategory.Action));
        Assert.Equal(25, rules.Count(r => r.Category == RuleCategory.Modifier));
        Assert.Equal(15, rules.Count(r => r.Category == RuleCategory.Restriction));
        Assert.Equal(15, rules.Count(r => r.Category == RuleCategory.Trigger));
        Assert.Equal(10, rules.Count(r => r.Category == RuleCategory.Memory));
        Assert.Equal(5, rules.Count(r => r.Category == RuleCategory.Trap));

        // Principle 70: Identifiers are Modifier rules, not a seventh pool.
        Assert.Equal(9, rules.Count(r => r.ModifierSubType == ModifierSubType.Identifier));
    }

    [Fact]
    public void Library_PassesThePoolLinter()
    {
        var rules = TestPaths.LoadLibrary(out var errors);
        Assert.Empty(errors);

        var lint = RuleLibraryLinter.Validate(rules);

        Assert.True(lint.Count == 0, string.Join(Environment.NewLine, lint));
    }

    [Fact]
    public void EveryRule_ResolvesThroughTheEngine_WithItsCategorySemantics()
    {
        var rules = TestPaths.LoadLibrary(out var errors);
        Assert.Empty(errors);

        var engine = new ModifierEngine();
        var memory = new MemoryBank();
        memory.Store(MemoryKeys.Number, "7");
        memory.Store(MemoryKeys.Color, "Blue");
        memory.Store(MemoryKeys.Symbol, "Star");
        memory.Store(MemoryKeys.Word, "RATE");

        foreach (var rule in rules)
        {
            var context = new ResolutionContext(
                new RuleSetState(),
                CurrentTurn: 1,
                ScreenText: rule.TriggerWord,
                Memory: memory,
                ScreenValue: "7");

            var result = engine.Resolve(rule.ToInstruction(), context);
            var directive = result.FinalDirective;

            Assert.NotNull(directive);

            switch (rule.Category)
            {
                case RuleCategory.Action:
                    Assert.Equal(rule.BaseDirective!.Action, directive!.Action);
                    break;

                case RuleCategory.Restriction:
                    Assert.Equal(CoinAction.None, directive!.Action);
                    break;

                case RuleCategory.Modifier:
                    Assert.Equal(CoinAction.None, directive!.Action);
                    break;

                case RuleCategory.Trigger:
                    Assert.Equal(rule.BaseDirective!.Action, directive!.Action);
                    break;

                case RuleCategory.Memory when rule.MemoryStoreKey is not null:
                    Assert.Equal(CoinAction.None, directive!.Action);
                    Assert.Equal(rule.MemoryStoreKey, result.MemoryWrite?.Key);
                    break;

                case RuleCategory.Memory:
                    var expected = MemoryEvaluator.IsSatisfied(rule.MemoryTask!.Value, memory)
                        ? rule.BaseDirective!.Action
                        : CoinAction.None;
                    Assert.Equal(expected, directive!.Action);
                    break;

                case RuleCategory.Trap:
                    Assert.True(result.IsFake);
                    Assert.Equal(CoinAction.None, directive!.Action);
                    break;
            }
        }
    }

    [Fact]
    public void Loader_AcceptsSingleObjectsAndArrays()
    {
        var arrayFile = Path.GetTempFileName();
        var objectFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(arrayFile, """
                [
                  { "id": "ACT_A", "category": "Action", "text": "Click A", "baseDirective": { "action": "Click" }, "introDepth": 1 },
                  { "id": "ACT_B", "category": "Action", "text": "Click B", "baseDirective": { "action": "Click" }, "introDepth": 1 }
                ]
                """);
            File.WriteAllText(objectFile, """
                { "id": "ACT_C", "category": "Action", "text": "Click C", "baseDirective": { "action": "Click" }, "introDepth": 1 }
                """);

            Assert.True(RuleLibraryLoader.TryLoadFile(arrayFile, out var array, out var arrayErrors));
            Assert.Empty(arrayErrors);
            Assert.Equal(2, array.Count);

            Assert.True(RuleLibraryLoader.TryLoadFile(objectFile, out var single, out var objectErrors));
            Assert.Empty(objectErrors);
            Assert.Single(single);
        }
        finally
        {
            File.Delete(arrayFile);
            File.Delete(objectFile);
        }
    }

    // ── Linter: each check must fail loudly on a broken library ─────────────────
    private static IReadOnlyList<RuleDefinition> Mutated(
        Func<List<RuleDefinition>, List<RuleDefinition>> mutate)
    {
        var rules = TestPaths.LoadLibrary(out var errors);
        Assert.Empty(errors);
        return mutate(rules.ToList());
    }

    [Fact]
    public void Linter_RejectsDuplicateCopy()
    {
        var rules = Mutated(list =>
        {
            var index = list.FindIndex(r => r.Id == "ACT_TAP_COIN");
            list[index] = list[index] with { Text = list.Find(r => r.Id == "ACT_CLICK_THE_COIN")!.Text };
            return list;
        });

        Assert.Contains(RuleLibraryLinter.Validate(rules), e => e.Contains("Duplicate rule copy"));
    }

    [Fact]
    public void Linter_RejectsAnIdentifierOutsideDepth5()
    {
        var rules = Mutated(list =>
        {
            var index = list.FindIndex(r => r.Id == "MOD_TRUST_BLUE");
            list[index] = list[index] with { IntroDepth = 4 };
            return list;
        });

        Assert.Contains(RuleLibraryLinter.Validate(rules), e => e.Contains("must be depth 5"));
    }

    [Fact]
    public void Linter_RejectsIdentifierCopySharedWithAnIgnoreRule()
    {
        var rules = Mutated(list =>
        {
            var index = list.FindIndex(r => r.Id == "MOD_IGNORE_ANIMATED");
            list[index] = list[index] with { Text = "Ignore Next Instruction" };
            return list;
        });

        Assert.Contains(RuleLibraryLinter.Validate(rules), e => e.Contains("share copy"));
    }

    [Fact]
    public void Linter_RejectsARecallTaskWithNoStoreRule()
    {
        var rules = Mutated(list =>
        {
            list.RemoveAll(r => r.Id == "MEM_REMEMBER_NUMBER");
            return list;
        });

        Assert.Contains(RuleLibraryLinter.Validate(rules), e => e.Contains("no Memory rule stores that key"));
    }

    [Fact]
    public void Linter_AllowsTrapMetadata_ButRejectsAFakeTrap()
    {
        // R-6: metadata is orthogonal to truth, so a trap may carry it. What a trap
        // may not do is claim to be real by origin.
        var rules = Mutated(list =>
        {
            var index = list.FindIndex(r => r.Id == "TRP_FREE_BONUS");
            list[index] = list[index] with { IsFakeByOrigin = false };
            return list;
        });

        Assert.Contains(RuleLibraryLinter.Validate(rules), e => e.Contains("must be IsFakeByOrigin"));
    }

    [Fact]
    public void Linter_RejectsADuplicateTriggerWord()
    {
        var rules = Mutated(list =>
        {
            var index = list.FindIndex(r => r.Id == "TRG_CLICK_GOLD");
            list[index] = list[index] with { TriggerWord = "RATE" };
            return list;
        });

        Assert.Contains(RuleLibraryLinter.Validate(rules), e => e.Contains("used by more than one rule"));
    }

    [Fact]
    public void Linter_RejectsAWrongCategoryCount()
    {
        var rules = Mutated(list =>
        {
            list.RemoveAll(r => r.Category == RuleCategory.Trap);
            return list;
        });

        Assert.Contains(RuleLibraryLinter.Validate(rules), e => e.Contains("the GDD fixes 5"));
    }
}
