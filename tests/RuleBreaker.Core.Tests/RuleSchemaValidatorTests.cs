namespace RuleBreaker.Core.Tests;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Model;
using Xunit;

public class RuleSchemaValidatorTests
{
    private static RuleDefinition ValidActionRule() => new(
        "ACT_HOLD", RuleCategory.Action, "Hold Coin", new Directive(CoinAction.Hold));

    [Fact]
    public void ValidRule_ProducesNoErrors()
    {
        Assert.Empty(RuleSchemaValidator.Validate(ValidActionRule()));
    }

    [Theory]
    [InlineData("act_click")]
    [InlineData("ACT")]
    [InlineData("ACT_")]
    [InlineData("_ACT")]
    [InlineData("ACT--CLICK")]
    [InlineData("")]
    public void InvalidRuleIds_AreRejected(string id)
    {
        var rule = ValidActionRule() with { Id = id };

        Assert.Contains(RuleSchemaValidator.Validate(rule), e => e.Contains("Rule Id"));
    }

    [Fact]
    public void EmptyText_IsRejected()
    {
        var rule = ValidActionRule() with { Text = "  " };

        Assert.Contains(RuleSchemaValidator.Validate(rule), e => e.Contains("Text"));
    }

    [Fact]
    public void ActionRuleWithoutDirective_IsRejected()
    {
        var rule = ValidActionRule() with { BaseDirective = null };

        Assert.Contains(RuleSchemaValidator.Validate(rule), e => e.Contains("BaseDirective"));
    }

    [Fact]
    public void FakeOriginOutsideTrapCategory_IsRejected()
    {
        var rule = ValidActionRule() with { IsFakeByOrigin = true };

        Assert.Contains(RuleSchemaValidator.Validate(rule), e => e.Contains("Trap"));
    }

    [Fact]
    public void FakeOriginInsideTrapCategory_IsAllowed()
    {
        var trap = new RuleDefinition(
            "TRP_CLICK_NOW", RuleCategory.Trap, "CLICK NOW", BaseDirective: null, IsFakeByOrigin: true);

        Assert.Empty(RuleSchemaValidator.Validate(trap));
    }

    [Fact]
    public void BaseDirectiveNone_IsRejected()
    {
        var rule = ValidActionRule() with { BaseDirective = Directive.NoInput };

        Assert.Contains(RuleSchemaValidator.Validate(rule), e => e.Contains("must not be None"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void IntroDepthOutsideTiers_IsRejected(int depth)
    {
        var rule = ValidActionRule() with { IntroDepth = depth };

        Assert.Contains(RuleSchemaValidator.Validate(rule), e => e.Contains("IntroDepth"));
    }

    [Fact]
    public void ModifierRuleWithoutDirective_IsAllowedInPhase0()
    {
        // Modifier/Trigger/Memory rules get directive wiring in Phase 1+;
        // the Phase 0 schema deliberately permits them to be directive-less.
        var modifier = new RuleDefinition("MOD_LIE_NEXT", RuleCategory.Modifier, "Next Instruction Is A Lie");

        Assert.Empty(RuleSchemaValidator.Validate(modifier));
    }
}
