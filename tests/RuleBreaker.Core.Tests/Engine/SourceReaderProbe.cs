namespace RuleBreaker.Core.Tests.Engine;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using System.Reflection;
using Xunit;

public class SourceReaderProbe
{
    [Fact]
    public void Directive_Record_Fields_Are_Readable()
    {
        // Confirm the exact parameter names the test file must use.
        var t = typeof(Directive);
        var constructor = t.GetConstructors().Single();
        var parameters = constructor.GetParameters().Select(p => p.Name).ToList();
        Assert.Contains("Action", parameters);
        Assert.Contains("Condition", parameters);
    }

    [Fact]
    public void SourceTracker_Classify_Parameters_Are_Readable()
    {
        var method = typeof(SourceTracker).GetMethod("Classify", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var parameters = method!.GetParameters().Select(p => p.Name).ToList();
        Assert.Contains("actionMatchesFinalDirective", parameters);
        Assert.Contains("actedFrom", parameters);
        Assert.Contains("wasThatInstructionFakeInEngine", parameters);
    }

    [Fact]
    public void WordMatcher_Matches_BoundarySemantic()
    {
        // Confirm the exact-match semantics the GDD word-trap table expects.
        Assert.True(WordMatcher.Matches("RATE", "RATE"));
        Assert.True(WordMatcher.Matches("RATE", "Rate it now"));
        Assert.False(WordMatcher.Matches("RATE", "RATED"));
        Assert.False(WordMatcher.Matches("RATE", "RATER"));
        Assert.False(WordMatcher.Matches("RATE", "PIRATE"));

        // Explicit token-level check: uppercase single token must match exactly.
        Assert.True(WordMatcher.Matches("RATE", "RATE"));
        Assert.False(WordMatcher.Matches("RATE", ""));
        Assert.False(WordMatcher.Matches("RATE", null!));
    }
}
