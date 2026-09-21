using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die EINE Normalisierung der Versuchs-Werte (<see cref="AttemptRecording"/>), die sich seit v0.499.0
/// alle Recorder teilen. Sie nagelt genau das fest, was vorher an sieben Stellen von Hand stand und
/// dabei auseinandergelaufen war: die beiden Klemmen, den Modus-Rückfall und die Rechnung
/// „Startzeit = Versuchszeitpunkt minus der GEKLEMMTEN Lösezeit".
/// </summary>
public class AttemptRecordingTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(42, 42)]
    [InlineData(86400, 86400)]
    [InlineData(86401, 86400)]
    [InlineData(999999, 86400)]
    [InlineData(-1, 0)]
    public void From_ClampsSecondsTo24Hours(int input, int expected)
    {
        Assert.Equal(expected, AttemptRecording.From(true, input, 0, null, Now).TimeSeconds);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(-5, 0)]
    public void From_ClampsHintsToThreeLevels(int input, int expected)
    {
        Assert.Equal(expected, AttemptRecording.From(true, 0, input, null, Now).HintsUsed);
    }

    [Theory]
    [InlineData("easy", SolveMode.Easy)]
    [InlineData("EASY", SolveMode.Easy)]
    [InlineData("training", SolveMode.Training)]
    [InlineData("hyperspeed", SolveMode.Training)]
    [InlineData("", SolveMode.Training)]
    [InlineData(null, SolveMode.Training)]
    public void From_NormalizesMode_UnknownFallsBackToTraining(string? mode, string expected)
    {
        Assert.Equal(expected, AttemptRecording.From(true, 0, 0, mode, Now).Mode);
    }

    [Fact]
    public void From_StartedAt_IsAttemptedAtMinusTime()
    {
        var core = AttemptRecording.From(true, 90, 0, null, Now);

        Assert.Equal(Now, core.AttemptedAt);
        Assert.Equal(Now.AddSeconds(-90), core.StartedAt);
    }

    /// <summary>Die Startzeit rechnet mit der GEKLEMMTEN Zeit — sonst läge sie bei einem
    /// Ausreißer Jahre vor dem Versuch, während die gespeicherte Zeit 24 h sagt.</summary>
    [Fact]
    public void From_StartedAt_UsesClampedTime_NotTheRawValue()
    {
        var core = AttemptRecording.From(false, 999999, 0, null, Now);

        Assert.Equal(AttemptRecording.MaxSeconds, core.TimeSeconds);
        Assert.Equal(Now.AddSeconds(-AttemptRecording.MaxSeconds), core.StartedAt);
    }

    [Fact]
    public void From_KeepsSolvedFlag()
    {
        Assert.True(AttemptRecording.From(true, 0, 0, null, Now).Solved);
        Assert.False(AttemptRecording.From(false, 0, 0, null, Now).Solved);
    }

    /// <summary>Ohne Zeitpunkt gilt „jetzt" (UTC) — die Recorder sollen keinen eigenen
    /// <c>DateTime.UtcNow</c>-Aufruf mehr brauchen.</summary>
    [Fact]
    public void From_WithoutNow_UsesUtcNow()
    {
        var before = DateTime.UtcNow;
        var core = AttemptRecording.From(true, 10, 0, null);
        var after = DateTime.UtcNow;

        Assert.InRange(core.AttemptedAt, before, after);
        Assert.Equal(DateTimeKind.Utc, core.AttemptedAt.Kind);
        Assert.Equal(core.AttemptedAt.AddSeconds(-10), core.StartedAt);
    }
}
