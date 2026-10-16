namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// The GDD response-timer tables, reproduced exactly (principles 30, 31).
/// "Medium difficulty always equals the level-designed base timer."
/// </summary>
public class TimerSystemTests
{
    // ── GDD "Combined Timer Reference Table" ────────────────────────────────────
    [Theory]
    [InlineData(6.0, 9.0, 6.0, 4.2, 3.0)] // Level 10
    [InlineData(4.0, 6.0, 4.0, 2.8, 2.0)] // Level 50
    [InlineData(2.0, 3.0, 2.0, 1.4, 1.0)] // Level 100
    public void TurnBasedTimerTable_MatchesTheGddReferenceTable(
        double levelBase, double easy, double medium, double hard, double nightmare)
    {
        Assert.Equal(easy, TimerTable.TurnBasedSeconds(levelBase, Difficulty.Easy));
        Assert.Equal(medium, TimerTable.TurnBasedSeconds(levelBase, Difficulty.Medium));
        Assert.Equal(hard, TimerTable.TurnBasedSeconds(levelBase, Difficulty.Hard));
        Assert.Equal(nightmare, TimerTable.TurnBasedSeconds(levelBase, Difficulty.Nightmare));
    }

    // ── GDD "Response Timer — Streaming Mode" table ─────────────────────────────
    [Theory]
    [InlineData(StreamingSpeed.Slow, 7.5, 5.0, 3.5, 2.5)]
    [InlineData(StreamingSpeed.Medium, 4.5, 3.0, 2.1, 1.5)]
    [InlineData(StreamingSpeed.Fast, 3.0, 2.0, 1.4, 1.0)]
    [InlineData(StreamingSpeed.Maximum, 1.5, 1.0, 0.7, 0.5)]
    public void StreamingTimerTable_MatchesTheGddReferenceTable(
        StreamingSpeed speed, double easy, double medium, double hard, double nightmare)
    {
        Assert.Equal(easy, TimerTable.StreamingSeconds(speed, Difficulty.Easy));
        Assert.Equal(medium, TimerTable.StreamingSeconds(speed, Difficulty.Medium));
        Assert.Equal(hard, TimerTable.StreamingSeconds(speed, Difficulty.Hard));
        Assert.Equal(nightmare, TimerTable.StreamingSeconds(speed, Difficulty.Nightmare));
    }

    [Fact]
    public void StreamingCompressedBases_AreFiveThreeTwoOneSeconds()
    {
        Assert.Equal(5.0, StreamingSpeed.Slow.CompressedBaseSeconds());
        Assert.Equal(3.0, StreamingSpeed.Medium.CompressedBaseSeconds());
        Assert.Equal(2.0, StreamingSpeed.Fast.CompressedBaseSeconds());
        Assert.Equal(1.0, StreamingSpeed.Maximum.CompressedBaseSeconds());
    }

    [Theory]
    [InlineData(6.0)]
    [InlineData(4.0)]
    [InlineData(2.0)]
    [InlineData(12.5)]
    public void MediumDifficulty_EqualsTheLevelBaseExactly(double levelBase)
    {
        Assert.Equal(levelBase, TimerTable.TurnBasedSeconds(levelBase, Difficulty.Medium));
    }

    [Theory]
    [InlineData(6.0)]
    [InlineData(4.0)]
    public void DifficultyOrdering_IsEasyMediumHardNightmare_ShortestLast(double levelBase)
    {
        var easy = TimerTable.TurnBasedSeconds(levelBase, Difficulty.Easy);
        var medium = TimerTable.TurnBasedSeconds(levelBase, Difficulty.Medium);
        var hard = TimerTable.TurnBasedSeconds(levelBase, Difficulty.Hard);
        var nightmare = TimerTable.TurnBasedSeconds(levelBase, Difficulty.Nightmare);

        Assert.True(easy > medium, "Easy is 50% longer than base");
        Assert.True(medium > hard, "Hard is 30% shorter than base");
        Assert.True(hard > nightmare, "Nightmare is halved");
    }

    // ── principle 14: "the minimum response timer is never zero" ────────────────
    [Fact]
    public void ResponseTimer_IsNeverZero_EvenForAPathologicalBase()
    {
        Assert.Equal(TimerTable.MinimumSeconds, TimerTable.TurnBasedSeconds(0, Difficulty.Nightmare));
        Assert.True(TimerTable.TurnBasedSeconds(0.0001, Difficulty.Nightmare) > 0);
        Assert.True(TimerTable.StreamingSeconds(StreamingSpeed.Maximum, Difficulty.Nightmare) > 0);
    }

    [Fact]
    public void CompressionFactor_IsOneWhenTurnBased_AndTheBaseRatioWhenStreaming()
    {
        Assert.Equal(1.0, TimerTable.CompressionFactor(PacingMode.TurnBased, null, 6.0));
        Assert.Equal(1.0, TimerTable.CompressionFactor(PacingMode.Streaming, StreamingSpeed.Slow, 5.0));
        Assert.Equal(2.0 / 6.0, TimerTable.CompressionFactor(PacingMode.Streaming, StreamingSpeed.Fast, 6.0));
        Assert.Equal(0.25, TimerTable.CompressionFactor(PacingMode.Streaming, StreamingSpeed.Maximum, 4.0));
    }

    // ── the timer bar's thresholds: amber → orange 20% → red 10% ────────────────
    [Fact]
    public void TimerThresholds_FollowTheTwentyAndTenPercentRule()
    {
        var started = ResponseTimer.Start(10.0);

        Assert.False(started.IsWarning);
        Assert.False(started.IsCritical);

        // Just above 20% remaining — still amber.
        var aboveWarning = started.Advance(7.999);
        Assert.False(aboveWarning.IsWarning);
        Assert.False(aboveWarning.IsCritical);

        // Exactly 20% remaining — orange.
        var atWarning = started.Advance(8.0);
        Assert.True(atWarning.IsWarning);
        Assert.False(atWarning.IsCritical);

        // Just above 10% — still orange, not critical.
        var between = started.Advance(8.999);
        Assert.True(between.IsWarning);
        Assert.False(between.IsCritical);

        // Exactly 10% remaining — red, critical cue.
        var atCritical = started.Advance(9.0);
        Assert.True(atCritical.IsWarning);
        Assert.True(atCritical.IsCritical);
    }

    [Fact]
    public void Timer_ClampsAtZero_AndReportsExpiry()
    {
        var timer = ResponseTimer.Start(2.0).Advance(5.0);

        Assert.Equal(0, timer.RemainingSeconds);
        Assert.True(timer.HasExpired);
        Assert.Equal(2.0, timer.TotalSeconds); // the window's length never changes
    }

    [Fact]
    public void Timer_NotStarted_IsNotExpired()
    {
        Assert.False(ResponseTimer.NotStarted.HasExpired);
        Assert.False(ResponseTimer.NotStarted.IsWarning);
        Assert.False(ResponseTimer.NotStarted.IsCritical);
    }
}
