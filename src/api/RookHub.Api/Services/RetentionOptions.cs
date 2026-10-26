namespace RookHub.Api.Services;

/// <summary>
/// Aufbewahrungsfristen und Deckel des täglichen Aufräumlaufs (<see cref="Puzzles.AnonymousDataRetentionService"/>,
/// Codereview 2026-09-29 O-7, Entscheidung 2026-10-25). Alle Werte aus dem Abschnitt <c>Retention</c>; fehlt ein Wert oder ist er
/// ≤ 0 bzw. unlesbar, gilt die Vorgabe. Übersicht aller Fristen: <c>docs/api/speicherdauer.md</c>.
/// </summary>
public sealed record RetentionOptions
{
    public const string Section = "Retention";

    /// <summary><c>Retention:AnonymousDays</c> — anonyme Puzzle-/Punktepartie-Versuche (vier Senken) verfallen danach.</summary>
    public int AnonymousDays { get; init; } = 90;

    /// <summary><c>Retention:AnonymousCap</c> — höchstens so viele anonyme Zeilen je Senke; darüber gehen die ältesten.</summary>
    public int AnonymousCap { get; init; } = 200_000;

    /// <summary><c>Retention:MessagesDays</c> — GELESENE Admin-/Direktnachrichten verfallen danach; ungelesene bleiben.</summary>
    public int MessagesDays { get; init; } = 365;

    /// <summary><c>Retention:ScoresheetFailedDays</c> — Fotos gescheiterter Formular-Einlesungen werden danach geleert.</summary>
    public int ScoresheetFailedDays { get; init; } = 30;

    /// <summary><c>Retention:ScoresheetOpenAnonymousDays</c> — Fotos nie übernommener Liga-Einlesungen OHNE Konto
    /// (Teilen-Link) werden danach geleert.</summary>
    public int ScoresheetOpenAnonymousDays { get; init; } = 14;

    /// <summary>Werte aus der Konfiguration (Vorgabe bei fehlend/≤ 0/unlesbar).</summary>
    public static RetentionOptions From(IConfiguration? config)
    {
        var d = new RetentionOptions();
        int Read(string key, int fallback) =>
            int.TryParse(config?[$"{Section}:{key}"], out var v) && v > 0 ? v : fallback;
        return new RetentionOptions
        {
            AnonymousDays = Read(nameof(AnonymousDays), d.AnonymousDays),
            AnonymousCap = Read(nameof(AnonymousCap), d.AnonymousCap),
            MessagesDays = Read(nameof(MessagesDays), d.MessagesDays),
            ScoresheetFailedDays = Read(nameof(ScoresheetFailedDays), d.ScoresheetFailedDays),
            ScoresheetOpenAnonymousDays = Read(nameof(ScoresheetOpenAnonymousDays), d.ScoresheetOpenAnonymousDays),
        };
    }
}
