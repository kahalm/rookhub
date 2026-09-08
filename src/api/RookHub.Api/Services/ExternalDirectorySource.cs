using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>Was ein Durchgang einer externen Quelle getan hat.</summary>
public record ExternalSweepResult(int Read, int Added, int Updated, int Matched, int Retired);

/// <summary>
/// Das Gemeinsame aller ZUSAETZLICHEN Turnierquellen — Verbandskalender neben chess-results.
///
/// <para><b>Warum das herausgezogen ist.</b> Jede dieser Quellen stellt dieselben vier Fragen,
/// und jede falsche Antwort beschaedigt den Bestand auf dieselbe Weise:</para>
///
/// <list type="number">
/// <item><b>Kenne ich das Turnier schon?</b> Ueber Termin und Namen, nicht ueber eine Nummer —
/// die fremde Kennung sagt nichts ueber die chess-results-Nummer. Verlangt werden ZWEI
/// unterscheidende Woerter: eines allein trifft auch das andere Turnier derselben Woche am
/// selben Ort, und aus zwei Turnieren eines zu machen ist schlimmer als ein Duplikat.</item>
/// <item><b>Habe ich es selbst schon angelegt?</b> Ueber die eigene Kennung
/// (<c>&lt;praefix&gt;&lt;fremde-id&gt;</c>). Ohne stabile fremde Kennung wird gar nichts
/// angelegt — sonst liesse sich „neues Turnier" nicht von „umbenanntes Turnier" unterscheiden und
/// der Bestand bekaeme jede Nacht ein Duplikat mehr.</item>
/// <item><b>Hat die Turniersuche mein Turnier eingeholt?</b> Dann steht dieselbe Veranstaltung
/// unter zwei Kennungen, und die eigene wird zurueckgezogen. Dieser Fall tritt bei JEDEM Turnier
/// ein, das lange genug vorher angekuendigt wurde.</item>
/// <item><b>Woher kommt die Angabe?</b> Herkunftsvermerk mit der fremden Kennung, damit bei
/// einem Widerspruch nachvollziehbar bleibt, welche Seite was sagt.</item>
/// </list>
///
/// <para>Was NICHT hier steht, weil es je Quelle verschieden ist: welche Felder sie liefert und
/// wie gut. Das entscheidet der jeweilige Dienst.</para>
/// </summary>
public static class ExternalDirectorySource
{
    /// <summary>
    /// Wie weit die Startdaten auseinanderliegen duerfen, um noch dasselbe Turnier zu sein.
    /// Dieselbe Toleranz wie beim FIDE-Abgleich: Verbandskalender und chess-results nennen bei
    /// mehrtaegigen Turnieren gelegentlich den Anreise- statt den ersten Spieltag.
    /// </summary>
    public const int MatchDayTolerance = 1;

    /// <summary>
    /// Denselben Eintrag im Verzeichnis finden — ueber Termin und Namen.
    ///
    /// <para>Gesucht wird nur unter Eintraegen MIT chess-results-Nummer: die eigenen Eintraege
    /// anderer Zusatzquellen zu treffen waere kein Zugewinn, sondern eine zweite Baustelle.</para>
    /// </summary>
    public static async Task<TournamentDirectoryEntry?> FindMatchAsync(
        AppDbContext db, string? federation, DateOnly start, string name, CancellationToken ct)
    {
        var from = start.AddDays(-MatchDayTolerance);
        var to = start.AddDays(MatchDayTolerance);

        var candidates = await db.TournamentDirectoryEntries
            .Where(e => e.ChessResultsId != null
                        && e.RemovedAt == null
                        && (federation == null || e.Federation == federation)
                        && e.StartDate != null && e.StartDate >= from && e.StartDate <= to)
            .ToListAsync(ct);
        if (candidates.Count == 0) return null;

        var words = FideDirectorySweepService.DistinctiveWords(name);
        if (words.Count == 0) return null;

        return candidates.FirstOrDefault(c =>
            words.Intersect(FideDirectorySweepService.DistinctiveWords(c.Name)).Count() >= 2);
    }

    /// <summary>Den eigenen, frueher angelegten Eintrag holen (<c>null</c>, wenn es keinen gibt).</summary>
    public static Task<TournamentDirectoryEntry?> FindOwnAsync(
        AppDbContext db, string publicId, CancellationToken ct) =>
        db.TournamentDirectoryEntries.FirstOrDefaultAsync(e => e.PublicId == publicId, ct);

    /// <summary>
    /// Zieht den eigenen Eintrag zurueck, wenn die Turniersuche dieselbe Veranstaltung inzwischen
    /// unter ihrer Nummer fuehrt. Gibt zurueck, ob etwas zurueckgezogen wurde.
    /// </summary>
    public static bool RetireIfSuperseded(
        TournamentDirectoryEntry? own, TournamentDirectoryEntry match, DateTime now)
    {
        if (own is null || own.Id == match.Id || own.RemovedAt is not null) return false;

        own.RemovedAt = now;
        return true;
    }

    /// <summary>
    /// Herkunftsvermerk setzen oder auffrischen. Ohne fremde Kennung wird NICHTS vermerkt: ein
    /// Vermerk ohne Kennung liesse sich beim naechsten Durchgang nicht wiedererkennen und legte
    /// jede Nacht eine neue Zeile an.
    /// </summary>
    public static void NoteSource(TournamentDirectoryEntry entry, DirectorySourceKind kind,
        string? externalId, string? url, DateTime now)
    {
        if (externalId is not { Length: > 0 }) return;

        var existing = entry.Sources.FirstOrDefault(
            s => s.Kind == kind && s.ExternalId == externalId);
        if (existing is not null)
        {
            existing.LastSeenAt = now;
            if (url is { Length: > 0 } && existing.Url is null) existing.Url = Truncate(url, 500);
            return;
        }

        entry.Sources.Add(new TournamentDirectorySource
        {
            Kind = kind,
            ExternalId = externalId,
            Url = Truncate(url, 500),
            FirstSeenAt = now,
            LastSeenAt = now,
        });
    }

    /// <summary>
    /// Uebertraegt einen Wert NUR, wenn beim Eintrag noch keiner steht.
    ///
    /// <para>Die Regel fuer alle Zusatzquellen: chess-results ist der gepflegte Bestand, eine
    /// Zusatzquelle fuellt Luecken. Sie darf einen vorhandenen Wert nicht ersetzen — sonst
    /// entscheidet die Reihenfolge der naechtlichen Durchgaenge darueber, welche Angabe gilt.</para>
    /// </summary>
    public static bool FillIfEmpty(string? incoming, ref string? target, int max)
    {
        if (incoming is not { Length: > 0 } || target is { Length: > 0 }) return false;
        target = Truncate(incoming, max);
        return true;
    }

    /// <summary>Dasselbe fuer Zahlen: 0 und <c>null</c> gelten als „noch nichts".</summary>
    public static bool FillIfEmpty(int? incoming, ref int? target)
    {
        if (incoming is not > 0 || target is > 0) return false;
        target = incoming;
        return true;
    }

    /// <summary>
    /// Publikum und Format aus dem Namen ableiten — dieselbe Ableitung wie im chess-results-Sweep.
    /// Die Turnierart (<see cref="TournamentKind"/>) bleibt bewusst unberuehrt: keine der
    /// Zusatzquellen sagt etwas darueber, und Raten waere schlechter als Schweigen.
    /// </summary>
    public static void ApplyClassification(TournamentDirectoryEntry entry)
    {
        entry.AgeGroups = TournamentClassifier.AgeGroupsOf(entry.Name);
        entry.Gender = TournamentClassifier.GenderOf(entry.Name);
        entry.IsLeague = TournamentClassifier.LooksLikeLeague(
            entry.Name, entry.Kind, entry.StartDate, entry.EndDate);
    }

    public static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
