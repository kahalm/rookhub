using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Claude-Client, der als „nicht konfiguriert" gilt — für Tests, die die Tipp-Generierung
/// nicht ausüben (Generierung ist dann no-op).</summary>
internal sealed class UnconfiguredClaude : IClaudeJsonClient
{
    public bool IsConfigured => false;
    public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

internal static class HintTestHelper
{
    /// <summary>Baut einen HintGenerationService ohne API-Key (Generierung inaktiv) — für Controller-Tests.</summary>
    public static HintGenerationService Build(AppDbContext db) =>
        new(db, new UnconfiguredClaude(),
            new StockfishAnalyzer(new ConfigurationBuilder().Build(), NullLogger<StockfishAnalyzer>.Instance),
            NullLogger<HintGenerationService>.Instance);
}
