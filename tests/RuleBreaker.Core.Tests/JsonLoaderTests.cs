namespace RuleBreaker.Core.Tests;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Model;
using Xunit;

public class JsonLoaderTests
{
    [Fact]
    public void Load_MapsCamelCaseJson_ToPascalCaseProperties()
    {
        const string json = """
            {
              "id": "TRG_RATE",
              "category": "Trigger",
              "text": "Click When You See RATE",
              "introDepth": 2
            }
            """;

        var rule = JsonLoader.Load<RuleDefinition>(json);

        Assert.Equal("TRG_RATE", rule.Id);
        Assert.Equal(RuleCategory.Trigger, rule.Category);
        Assert.Equal("Click When You See RATE", rule.Text);
        Assert.Equal(2, rule.IntroDepth);
    }

    [Fact]
    public void TryLoad_MalformedJson_ReturnsFalseWithDescriptiveError()
    {
        var ok = JsonLoader.TryLoad<RuleDefinition>("{ not valid json }", out var value, out var errors);

        Assert.False(ok);
        Assert.Null(value);
        var error = Assert.Single(errors);
        Assert.Contains("Invalid JSON", error);
    }

    [Fact]
    public void TryLoad_EmptyInput_ReturnsFalseWithClearError()
    {
        var ok = JsonLoader.TryLoad<RuleDefinition>("   ", out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("null, empty, or whitespace"));
    }

    [Fact]
    public void Load_InvalidContent_ThrowsRuleContentException()
    {
        Assert.Throws<RuleContentException>(() => JsonLoader.Load<RuleDefinition>("null"));
    }
}
