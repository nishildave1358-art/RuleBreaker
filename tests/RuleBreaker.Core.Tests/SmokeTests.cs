namespace RuleBreaker.Core.Tests;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using Xunit;

/// <summary>
/// Phase 0 smoke test: the full pipeline end-to-end —
/// JSON rule → deserialize → schema-validate → resolve through the ModifierEngine.
/// When this is green, the "empty-but-running project" acceptance criterion is met.
/// </summary>
public class SmokeTests
{
    private const string ActionRuleJson = """
        {
          "id": "ACT_CLICK",
          "category": "Action",
          "text": "Click Coin",
          "baseDirective": { "action": "Click" },
          "introDepth": 1
        }
        """;

    [Fact]
    public void JsonRule_Validates_AndResolves_ToClickDirective()
    {
        var rule = JsonLoader.Load<RuleDefinition>(ActionRuleJson);

        Assert.Empty(RuleSchemaValidator.Validate(rule));

        var engine = new ModifierEngine();
        var result = engine.Resolve(rule.ToInstruction());

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
        Assert.Equal(0, result.InversionCount);
    }
}
