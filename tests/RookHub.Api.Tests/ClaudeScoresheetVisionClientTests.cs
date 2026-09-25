using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Wie der Claude-Leser je Modell und Modus nachdenkt (ohne API-Aufruf).</summary>
public class ClaudeScoresheetVisionClientTests
{
    [Theory]
    // Mit Nachdenken: der eingestellte effort (oder die Vorgabe des Modells) geht durch.
    [InlineData("claude-opus-5", ScoresheetReadMode.Full, null, false, null)]
    [InlineData("claude-opus-5-5", ScoresheetReadMode.Full, "low", false, "low")]
    // Nur abschreiben: abschalten, wo es geht …
    [InlineData("claude-opus-5", ScoresheetReadMode.Transcribe, null, true, null)]
    [InlineData("claude-sonnet-5", ScoresheetReadMode.Transcribe, "medium", true, "medium")]
    // … Opus 5 verbietet das Abschalten zusammen mit xhigh/max — der effort fällt dann weg …
    [InlineData("claude-opus-5", ScoresheetReadMode.Transcribe, "max", true, null)]
    // … und Opus 5.5 lässt sich gar nicht abschalten: so wenig wie möglich.
    [InlineData("claude-opus-5-5", ScoresheetReadMode.Transcribe, null, false, "low")]
    [InlineData("claude-fable-5-1", ScoresheetReadMode.Transcribe, "high", false, "low")]
    public void PlanThinking_PerModelAndMode(string model, ScoresheetReadMode mode, string? effort, bool disabled, string? expectedEffort)
    {
        var plan = ClaudeScoresheetVisionClient.PlanThinking(model, mode, effort);
        Assert.Equal(disabled, plan.Disabled);
        Assert.Equal(expectedEffort, plan.Effort);
    }
}
