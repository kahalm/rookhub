using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Berichtigt den BESTAND des Kurses „Persönliche Taktiken“ (0.973.1): Kapitel halten fest, was beim ersten Klick empfohlen
/// war — Buchdiagramm-Linien von vor 0.973.1 (<c>Source = game-exercise</c>, <c>StartPly = -1</c>) können deshalb den Löser
/// auf die Seite setzen, die in der Linie mattgesetzt wird (Meldung 2026-11-14, „Alekhine: Move by Move“, S. 154). Der Lauf
/// wendet auf jede solche Linie genau <see cref="BookSolverLine"/> an:
///
/// <list type="bullet">
/// <item>die Seite am Zug gewinnt → bleibt, wie sie ist;</item>
/// <item>ihr Gegner setzt matt → <c>StartPly = 0</c> (Stellung und Züge stimmen schon: Diagramm = Stellung vor dem Gegnerzug);
/// Tipps der Linie werden geleert, sie galten der falschen Seite;</item>
/// <item>Linie unspielbar oder gewinnende Seite nicht bestimmbar → <c>Retired</c> UND <c>IsInfoOnly</c>. <c>Retired</c> allein
/// nimmt eine Linie nur aus den Zufallstöpfen, im Kurs bliebe sie abfragbar; erst <c>IsInfoOnly</c> nimmt sie aus Abfrage und
/// Fortschritt. Gelöscht wird nichts — Versuche hängen an der Linie.</item>
/// </list>
///
/// <para>Linien ohne Matt tragen ihre Engine-Bewertung nicht mit; sie wird am Buchdiagramm mit derselben Stellung und Linie
/// nachgeschlagen (<c>EbookDiagrams</c>, beste Bewertung). Kein Diagramm mehr (Lösung inzwischen korrigiert) → nicht bestimmbar.</para>
///
/// <para><b>Warum keine Migration</b>: die Regel braucht Schachlogik (Linie nachspielen, Matt erkennen) — die gibt es nur im
/// Code, nicht in SQL, und eine Migration dürfte den Dienstcode nicht einfrieren. Deshalb der hier übliche Weg für
/// Bestandsreparaturen: ein einmaliger Hosted Service beim Start (wie <c>TournamentGroupingBackfillService</c>). Andere Linien
/// und alle Lernstände bleiben unberührt (nur <c>StartPly</c>/<c>Retired</c>/<c>IsInfoOnly</c>/Tipps der betroffenen Zeile).</para>
///
/// <para>Einmalig: nach dem ersten vollständigen Lauf steht <see cref="DoneKey"/> in <c>SystemSettings</c>; neue Linien
/// entstehen ab 0.973.1 schon nach der Regel. Idempotent — ein zweiter Lauf fände nichts mehr.</para>
/// </summary>
public class PersonalTacticsLineRepair(IServiceScopeFactory scopeFactory, ILogger<PersonalTacticsLineRepair> logger) : BackgroundService
{
    public const string DoneKey = "PersonalTactics.SolverSideRepair";
    /// <summary>Fassung der Reparatur — erhöhen, wenn sich <see cref="BookSolverLine"/> so ändert, dass der Bestand erneut zu prüfen ist.</summary>
    public const string DoneValue = "1";

    public sealed record Result(int Checked, int Switched, int Disabled);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await RunOnceAsync(db, stoppingToken) is { } r && r.Switched + r.Disabled > 0)
                logger.LogInformation(
                    "Persönliche Taktiken: {Checked} Buchdiagramm-Linien geprüft, {Switched} auf den Gegnerzug umgestellt, {Disabled} stillgelegt.",
                    r.Checked, r.Switched, r.Disabled);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren während des Laufs: der nächste Start macht weiter (Marke noch nicht gesetzt).
        }
        catch (Exception ex)
        {
            // Kein Grund, den Start scheitern zu lassen — der Bestand bleibt wie vorher, der nächste Start versucht es wieder.
            logger.LogWarning(ex, "Persönliche Taktiken: Berichtigung der Buchdiagramm-Linien fehlgeschlagen.");
        }
    }

    /// <summary>Läuft nur, solange <see cref="DoneKey"/> nicht auf <see cref="DoneValue"/> steht; <c>null</c> = schon erledigt.</summary>
    internal static async Task<Result?> RunOnceAsync(AppDbContext db, CancellationToken ct = default)
    {
        var mark = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == DoneKey, ct);
        if (mark?.Value == DoneValue) return null;
        var result = await RepairAsync(db, ct);
        if (mark is null) db.SystemSettings.Add(mark = new SystemSetting { Key = DoneKey });
        mark.Value = DoneValue;
        mark.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Die Regel auf jede Buchdiagramm-Linie des Bestands anwenden (ohne Marke — für Tests und den Lauf).</summary>
    internal static async Task<Result> RepairAsync(AppDbContext db, CancellationToken ct = default)
    {
        var lines = await db.BookPuzzles
            .Where(p => p.Source == PersonalTacticsCourse.LineSource && p.StartPly == -1 && !p.IsInfoOnly)
            .ToListAsync(ct);
        if (lines.Count == 0) return new Result(0, 0, 0);

        // Linien ohne Matt: Bewertung am Buchdiagramm mit derselben Stellung und Linie nachschlagen.
        var fens = lines.Select(l => l.Fen).Distinct().ToList();
        var evals = (await db.EbookDiagrams.AsNoTracking()
                .Where(d => fens.Contains(d.Fen) && d.SolutionUci != null && d.EngineEval != null)
                .Select(d => new { d.Fen, d.SolutionUci, d.EngineEval }).ToListAsync(ct))
            .GroupBy(d => (d.Fen, d.SolutionUci)).ToDictionary(g => g.Key, g => g.Max(d => d.EngineEval));

        int switched = 0, disabled = 0;
        foreach (var l in lines)
        {
            var moves = string.Join(' ', l.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var line = BookSolverLine.Of(l.Fen, moves, evals.GetValueOrDefault((l.Fen, (string?)moves)));
            if (line is null)
            {
                l.Retired = true;
                l.IsInfoOnly = true;
                disabled++;
            }
            else if (line.OpponentFirst)
            {
                l.StartPly = line.StartPly;
                l.HintsJson = null;      // Tipps galten der Seite, die mattgesetzt wird
                l.HintsVersion = 0;
                switched++;
            }
        }
        await db.SaveChangesAsync(ct);
        return new Result(lines.Count, switched, disabled);
    }
}
