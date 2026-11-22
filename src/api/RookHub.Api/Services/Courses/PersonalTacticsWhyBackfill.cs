using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Trägt im BESTAND des Kurses „Persönliche Taktiken“ den Vermerk „Warum diese Aufgabe“ nach (1.001.0, <see cref="PersonalTacticsWhy"/>).
/// Kapitel halten fest, was beim ersten Klick empfohlen war; der Vermerk wird deshalb nicht neu ausgewählt, sondern ZUGEORDNET:
/// je Partie (aus der <c>LineId</c> <c>{Datei}:{club|own}{PartieId}:{nn}</c>) rechnet der Lauf die Empfohlenen Übungen mit dem
/// Buchzugriff des Kursbesitzers nach (derselbe Weg wie beim Anlegen, <see cref="PersonalTacticsCourse"/>), und eine vorhandene
/// Linie bekommt den Vermerk der nachgerechneten Aufgabe mit GLEICHER Stellung, GLEICHEN Zügen und GLEICHER Zugnummer im Titel
/// („23. · …“ — die Stelle der Partie). Passt keine oder passen mehrere mit verschiedenem Vermerk, bleibt die Linie ohne — nichts
/// wird geraten. Die Löserseite (<c>StartPly</c>) spielt keine Rolle, die hat <see cref="PersonalTacticsLineRepair"/> evtl. umgestellt.
///
/// <para>Einmalig beim Start wie <see cref="PersonalTacticsLineRepair"/> (Schachlogik → keine Migration): nach dem ersten
/// vollständigen Lauf steht <see cref="DoneKey"/> in <c>SystemSettings</c>. Idempotent — nur Linien ohne Vermerk werden angefasst,
/// und nur ihr Feld <c>Why</c>; Lernstände bleiben.</para>
/// </summary>
public class PersonalTacticsWhyBackfill(IServiceScopeFactory scopeFactory, ILogger<PersonalTacticsWhyBackfill> logger) : BackgroundService
{
    public const string DoneKey = "PersonalTactics.WhyBackfill";
    /// <summary>Fassung — erhöhen, wenn der Bestand erneut zuzuordnen ist (z. B. neue Felder im Vermerk).</summary>
    public const string DoneValue = "1";

    public sealed record Result(int Checked, int Filled, int Unmatched);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var resolver = sp.GetRequiredService<PermissionResolver>();
            var course = sp.GetRequiredService<PersonalTacticsCourse>();
            if (await RunOnceAsync(db, course, async (userId, ct) =>
                    new EbookViewer(userId, (await resolver.GetAsync(userId, ct)).Has(Permissions.EbooksManage)), stoppingToken) is { Checked: > 0 } r)
                logger.LogInformation(
                    "Persönliche Taktiken: {Checked} Linien ohne Vermerk geprüft, {Filled} mit „Warum diese Aufgabe“ ergänzt, {Unmatched} nicht zuzuordnen.",
                    r.Checked, r.Filled, r.Unmatched);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren während des Laufs: der nächste Start macht weiter (Marke noch nicht gesetzt).
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Persönliche Taktiken: Nachtrag „Warum diese Aufgabe“ fehlgeschlagen.");
        }
    }

    /// <summary>Läuft nur, solange <see cref="DoneKey"/> nicht auf <see cref="DoneValue"/> steht; <c>null</c> = schon erledigt.</summary>
    internal static async Task<Result?> RunOnceAsync(AppDbContext db, PersonalTacticsCourse course,
        Func<int, CancellationToken, Task<EbookViewer>> viewerOf, CancellationToken ct = default)
    {
        var mark = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == DoneKey, ct);
        if (mark?.Value == DoneValue) return null;
        var result = await BackfillAsync(db, course, viewerOf, ct);
        if (mark is null) db.SystemSettings.Add(mark = new SystemSetting { Key = DoneKey });
        mark.Value = DoneValue;
        mark.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Partie einer Linie aus ihrer <c>LineId</c>: Besitzer (Dateiname) und Partie; <c>null</c> = fremdes Format.</summary>
    internal static (int UserId, PersonalTacticsCourse.Game Game)? GameOf(string lineId)
    {
        var parts = lineId.Split(':');
        if (parts.Length != 3 || !parts[0].StartsWith(PersonalTacticsCourse.FilePrefix, StringComparison.Ordinal) || !parts[0].EndsWith(".pgn", StringComparison.Ordinal))
            return null;
        var user = parts[0][PersonalTacticsCourse.FilePrefix.Length..^4];
        if (!int.TryParse(user, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)) return null;
        var (kind, digits) = parts[1].StartsWith("club", StringComparison.Ordinal) ? (PersonalTacticsCourse.GameKind.Club, parts[1][4..])
            : parts[1].StartsWith("own", StringComparison.Ordinal) ? (PersonalTacticsCourse.GameKind.Own, parts[1][3..]) : ((PersonalTacticsCourse.GameKind?)null, "");
        if (kind is not { } k || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var gameId)) return null;
        return (userId, new PersonalTacticsCourse.Game(k, gameId, false));
    }

    /// <summary>Zugnummer der Stelle im Titel („23.“ bzw. „16…“ vor „ · “).</summary>
    private static string MoveOf(string? title) => title?.Split(" · ", 2)[0].Trim() ?? "";
    private static string Norm(string moves) => string.Join(' ', moves.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Ohne Marke (für Tests und den Lauf).</summary>
    internal static async Task<Result> BackfillAsync(AppDbContext db, PersonalTacticsCourse course,
        Func<int, CancellationToken, Task<EbookViewer>> viewerOf, CancellationToken ct = default)
    {
        var lines = await db.BookPuzzles
            .Where(p => p.Source == PersonalTacticsCourse.LineSource && p.Why == null && p.LineId.StartsWith(PersonalTacticsCourse.FilePrefix))
            .ToListAsync(ct);
        int filled = 0, unmatched = 0;
        foreach (var group in lines.GroupBy(l => GameOf(l.LineId)))
        {
            if (group.Key is not { } key) { unmatched += group.Count(); continue; }
            var fresh = await course.LinesAsync(key.Game, await viewerOf(key.UserId, ct), ct);
            foreach (var l in group)
            {
                var hits = fresh.Where(f => f.Fen == l.Fen && Norm(f.Moves) == Norm(l.Moves) && MoveOf(f.Title) == MoveOf(l.Title))
                    .Select(f => f.Why).Distinct().ToList();
                if (hits is [{ } why]) { l.Why = why; filled++; }
                else unmatched++;
            }
            await db.SaveChangesAsync(ct);
        }
        return new Result(lines.Count, filled, unmatched);
    }
}
