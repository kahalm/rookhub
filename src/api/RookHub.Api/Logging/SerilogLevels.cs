using Serilog;
using Serilog.Events;

namespace RookHub.Api.Logging;

/// <summary>
/// Mindest-Level und Overrides des Serilog-Bootstraps (Program.cs). Eigene Datei, damit ein Test die
/// Overrides am echten Logger pruefen kann (Codereview 2026-09-29, A10-014).
/// </summary>
public static class SerilogLevels
{
    /// <summary>Kategorie-Praefix → Mindest-Level (Serilog vergleicht praefixweise an Punktgrenzen).</summary>
    public static readonly IReadOnlyDictionary<string, LogEventLevel> Overrides = new Dictionary<string, LogEventLevel>
    {
        ["Microsoft.AspNetCore"] = LogEventLevel.Warning,
        ["Microsoft.EntityFrameworkCore"] = LogEventLevel.Warning,
        // Erwartetes, harmloses Startup-Rauschen: DataProtection persistiert den Key-Ring
        // bewusst unverschlüsselt in das gemountete /keys-Volume (privat, durable). Die zwei
        // Hinweise ("no XML encryptor" / "may not be persisted") kämen bei JEDEM Neustart →
        // hier auf Error angehoben, echte DataProtection-Fehler bleiben sichtbar.
        ["Microsoft.AspNetCore.DataProtection"] = LogEventLevel.Error,
        // Pro ausgehendem Request (17 HttpClient-Registrierungen: Crawler, Lichess, Explorer, LLM, …) loggt
        // Microsoft.Extensions.Http sonst 4 INF-Zeilen (Start/Sending/Received/End) samt vollem Pfad — bei
        // PlayTimeService also die chess.com-/Lichess-Namen der Nutzer alle 6 h im ES. Wie in piratechess nur
        // noch Warnungen; die Dienste loggen ihre Aufrufe selbst.
        ["System.Net.Http.HttpClient"] = LogEventLevel.Warning,
    };

    /// <summary>Information als Grundlevel plus <see cref="Overrides"/>.</summary>
    public static LoggerConfiguration ApplyRookHubLevels(this LoggerConfiguration configuration)
    {
        configuration.MinimumLevel.Information();
        foreach (var (source, level) in Overrides)
            configuration.MinimumLevel.Override(source, level);
        return configuration;
    }
}
