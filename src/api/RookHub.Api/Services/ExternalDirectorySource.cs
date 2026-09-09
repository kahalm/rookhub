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
            .Include(e => e.Sources)
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

    /// <summary>
    /// Denselben Eintrag ueber die chess-results-NUMMER finden — der exakte Weg, wenn die Quelle
    /// sie selbst nennt.
    ///
    /// <para>Er schlaegt <see cref="FindMatchAsync"/> in jeder Hinsicht: kein Termin-Fenster, kein
    /// Wortvergleich, keine Verwechslung zweier Turniere derselben Woche am selben Ort. Nur
    /// chess.sk liefert sie heute mit (27 von 79 Eintraegen, Feld „Swiss manager URL"); wo sie
    /// fehlt, bleibt der Namensvergleich.</para>
    /// </summary>
    public static Task<TournamentDirectoryEntry?> FindByChessResultsIdAsync(
        AppDbContext db, string? chessResultsId, CancellationToken ct) =>
        chessResultsId is { Length: > 0 }
            ? db.TournamentDirectoryEntries
                .Include(e => e.Sources)
                .FirstOrDefaultAsync(
                    e => e.ChessResultsId == chessResultsId && e.RemovedAt == null, ct)
            : Task.FromResult<TournamentDirectoryEntry?>(null);

    /// <summary>
    /// Den eigenen, frueher angelegten Eintrag holen (<c>null</c>, wenn es keinen gibt).
    ///
    /// <para><b>Das <c>Include</c> ist keine Bequemlichkeit, es haelt den Bestand ganz.</b> Alle
    /// drei Suchwege hier geben ihren Fund an <see cref="NoteSource"/> weiter, und der entscheidet
    /// anhand von <c>entry.Sources</c>, ob er einen Vermerk AUFFRISCHT oder einen neuen ANLEGT.
    /// Ohne geladene Sammlung ist sie in einem frischen Scope leer — also in jeder Nacht ausser
    /// der ersten, in der ein Eintrag entsteht. Die Folge waere eine zweite Zeile mit derselben
    /// <c>(Kind, ExternalId)</c>, und darauf liegt ein EINDEUTIGER Index: der Durchgang bricht
    /// beim Speichern ab, und zwar der ganze, nicht nur die eine Zeile.</para>
    ///
    /// <para>Dieselbe leere Sammlung laesst ausserdem jedes „habe ich das schon geholt?" mit Nein
    /// antworten — <c>ChessArbiterDirectorySweepService.HasDetail</c> und die gleichnamigen
    /// Pruefungen der uebrigen Quellen lesen genau dieses Feld. Polen haette damit seine 150
    /// Detailseiten JEDE Nacht neu geholt.</para>
    ///
    /// <para>Aufgefallen beim Bau der franzoesischen Quelle, bevor eine der neun betroffenen
    /// Quellen ihre zweite Nacht erlebt hat (auf Dev standen zu dem Zeitpunkt nur Vermerke der
    /// Arten 1 und 2). Lazy Loading ist bewusst nicht eingeschaltet — es waere hier die
    /// bequemere, aber die schlechtere Loesung: eine Abfrage je Eintrag statt einer je
    /// Durchgang.</para>
    /// </summary>
    public static Task<TournamentDirectoryEntry?> FindOwnAsync(
        AppDbContext db, string publicId, CancellationToken ct) =>
        db.TournamentDirectoryEntries
            .Include(e => e.Sources)
            .FirstOrDefaultAsync(e => e.PublicId == publicId, ct);

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

    /// <summary>Wie oft eine Quelle einen Eintrag nicht mehr liefern muss, bis er als
    /// zurueckgezogen gilt. Wie bei der Turniersuche zwei — ein einzelner Ausfall der Quelle
    /// (halb geladene Seite, Netzfehler) darf kein Turnier absagen.</summary>
    public const int MissesUntilRetired = 2;

    /// <summary>
    /// Zieht Eintraege zurueck, die eine ZUSATZQUELLE nicht mehr liefert.
    ///
    /// <para>Die Turniersuche macht das seit langem (<c>MissedSweeps</c> in
    /// <see cref="TournamentDirectoryService"/>), die 15 Verbandskalender bisher NICHT: ein Turnier,
    /// das aus dem polnischen oder italienischen Kalender verschwand, blieb bei uns fuer immer
    /// stehen. Ihr Zaehler „zurueckgezogen" bedeutet etwas anderes, naemlich
    /// <see cref="RetireIfSuperseded"/>.</para>
    ///
    /// <para><b>Vier Schranken, und jede hat einen Grund:</b></para>
    /// <list type="number">
    /// <item><b>Nur KUENFTIGE Eintraege.</b> Was vorbei ist, sagt niemand mehr ab, und die Quellen
    /// lassen Vergangenes irgendwann fallen — das ist kein Verschwinden.</item>
    /// <item><b>Nur Eintraege OHNE chess-results-Nummer.</b> Fuehrt die Turniersuche dasselbe
    /// Turnier, entscheidet SIE ueber sein Verschwinden; sie sieht mehr als ein Verbandskalender.</item>
    /// <item><b>Nur Eintraege, deren EINZIGER Herkunftsvermerk diese Quelle ist.</b> Steht ein
    /// Turnier auf zwei Kalendern, ist sein Fehlen auf einem keine Absage.</item>
    /// <item><b>Bremse gegen halbe Laeufe.</b> Liefert ein Lauf nichts oder weniger als die HAELFTE
    /// der Kandidaten, wird gar nicht geprueft. Genau dafuer gibt es bei der Turniersuche die
    /// MaxRows-Bremse: eine systematische Luecke (eine von 25 Regionen faellt aus, eine von 12
    /// Monatsseiten) faengt die Karenz von zwei Laeufen NICHT ab, sie wiederholt sich jede Nacht.</item>
    /// </list>
    /// </summary>
    /// <param name="deliveredExternalIds">Die Kennungen, die DIESER Lauf geliefert hat.</param>
    /// <returns>Wie viele Eintraege zurueckgezogen wurden.</returns>
    public static async Task<int> RetireVanishedAsync(AppDbContext db, DirectorySourceKind kind,
        IReadOnlyCollection<string> deliveredExternalIds, DateTime now, CancellationToken ct = default)
    {
        if (deliveredExternalIds.Count == 0) return 0;

        var today = DateOnly.FromDateTime(now);
        var candidates = await db.TournamentDirectoryEntries
            .Include(e => e.Sources)
            .Where(e => e.RemovedAt == null
                        && e.ChessResultsId == null
                        && (e.EndDate ?? e.StartDate) >= today
                        && e.Sources.Count == 1
                        && e.Sources.Any(s => s.Kind == kind))
            .ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        // Die Bremse: ein Lauf, der weniger als die Haelfte der Kandidaten liefert, ist
        // unvollstaendig und kein Beleg fuer Absagen.
        if (deliveredExternalIds.Count * 2 < candidates.Count) return 0;

        var delivered = deliveredExternalIds.ToHashSet(StringComparer.Ordinal);
        var retired = 0;
        foreach (var entry in candidates)
        {
            var externalId = entry.Sources.First(s => s.Kind == kind).ExternalId;
            if (delivered.Contains(externalId))
            {
                if (entry.MissedSweeps != 0) { entry.MissedSweeps = 0; entry.UpdatedAt = now; }
                continue;
            }

            entry.MissedSweeps++;
            entry.UpdatedAt = now;
            if (entry.MissedSweeps < MissesUntilRetired) continue;
            entry.RemovedAt = now;
            retired++;
        }

        if (retired > 0 || candidates.Any(c => c.UpdatedAt == now)) await db.SaveChangesAsync(ct);
        return retired;
    }

    /// <summary>Laenge der Spalte <see cref="TournamentDirectorySource.ExternalId"/>.</summary>
    public const int MaxExternalIdLength = 60;

    /// <summary>
    /// Herkunftsvermerk setzen oder auffrischen. Ohne fremde Kennung wird NICHTS vermerkt: ein
    /// Vermerk ohne Kennung liesse sich beim naechsten Durchgang nicht wiedererkennen und legte
    /// jede Nacht eine neue Zeile an.
    /// </summary>
    /// <exception cref="ArgumentException">Die Kennung ist laenger als die Spalte.</exception>
    public static async Task NoteSourceAsync(AppDbContext db, TournamentDirectoryEntry entry,
        DirectorySourceKind kind, string? externalId, string? url, DateTime now,
        CancellationToken ct = default)
    {
        if (externalId is not { Length: > 0 }) return;

        // ABBRUCH mit Klartext statt Abschneiden. Die Kennung ist ein SCHLUESSEL: gekuerzt koennten
        // zwei verschiedene Turniere denselben bekommen und in Liste und Kalender zu einem
        // verschmelzen — der teuerste Fehler dieser Klasse (siehe TournamentNameGrouping).
        // Gehasht waere es noch schlimmer: die Suchen der Quellen vergleichen die ROHE Kennung
        // (`s.ExternalId == slug`), fanden ihren Vermerk also nie wieder und liefen bei jedem
        // Durchgang in den eindeutigen Index.
        //
        // Am 2026-09-09 auf Dev gefunden: Wales und Deutschland fuehren keine Turniernummer, ihre
        // Kennung entsteht aus Termin und Anschrift und ist damit laenger als 60 Zeichen. Beide
        // Quellen scheiterten jede Nacht mit „Data too long for column 'ExternalId'" — bei
        // Deutschland erst nach 283 s hoeflichen Crawlens, das den ganzen Durchgang wegwarf. Die
        // Meldung stand nur als innere Ausnahme eines DbUpdateException im Log.
        if (externalId.Length > MaxExternalIdLength)
            throw new ArgumentException(
                $"Quelle {kind} liefert eine {externalId.Length} Zeichen lange Kennung, die Spalte "
                + $"haelt {MaxExternalIdLength}. Einen KURZSCHLUESSEL vermerken (Hash der Quellen-"
                + "Kennung, wie WcuDirectorySweepService.PublicIdOf) — nicht abschneiden.",
                nameof(externalId));

        var existing = entry.Sources.FirstOrDefault(
            s => s.Kind == kind && s.ExternalId == externalId);
        if (existing is not null)
        {
            existing.LastSeenAt = now;
            if (url is { Length: > 0 } && existing.Url is null) existing.Url = Truncate(url, 500);
            return;
        }

        // UMHAENGEN, wenn die Kennung schon an einem ANDEREN Eintrag haengt. Der eindeutige Index
        // liegt auf (Kind, ExternalId) und gilt damit ueber den ganzen Bestand — die Suche oben
        // sieht aber nur die Vermerke DIESES Eintrags. Aendert sich die Zuordnung einer Quelle
        // (ihr Turnier wird jetzt einem chess-results-Eintrag zugeordnet statt dem eigenen), legte
        // der Lauf einen zweiten Vermerk an und starb an „Duplicate entry".
        //
        // Am 2026-09-09 auf Dev an DREI Quellen gleichzeitig aufgetreten (Ungarn, Tschechien,
        // Ankuendigungskalender), ausgeloest von ueber 400 neuen Eintraegen aus den am selben Tag
        // reparierten Quellen: die Namens-/Terminvergleiche landeten seither auf anderen Eintraegen.
        //
        // Umhaengen statt Verwerfen, weil der Vermerk sagt „diese Quelle kennt das Turnier unter
        // X" — gehoert das Turnier jetzt zu einem anderen Eintrag, gehoert der Vermerk mit dorthin.
        // Ueber die NAVIGATION, nicht ueber den Fremdschluessel: bei einem neu angelegten Eintrag
        // ist die Id noch 0, EF setzt sie beim Speichern selbst.
        var elsewhere = await db.TournamentDirectorySources
            .FirstOrDefaultAsync(s => s.Kind == kind && s.ExternalId == externalId, ct);
        if (elsewhere is not null)
        {
            elsewhere.LastSeenAt = now;
            if (url is { Length: > 0 } && elsewhere.Url is null) elsewhere.Url = Truncate(url, 500);
            entry.Sources.Add(elsewhere);
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
