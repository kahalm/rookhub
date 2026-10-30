using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Tournaments;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Abgleich EINER Trefferliste der chess-results-Turniersuche mit dem Bestand (zweite Zerlegungsrunde W7b aus
/// <see cref="TournamentDirectoryService"/>): bekannte Eintraege laden (samt Nachzueglern ausserhalb des Fensters),
/// Zeilen auf Eintraege uebertragen bzw. neue anlegen, nicht mehr gelieferte zaehlen und absagen.
///
/// <para>Der Ablauf eines Durchgangs (Abruf, Sweep-Zeile, Speichern, Meldungen, Log) bleibt in
/// <see cref="TournamentDirectoryService.SweepFederationAsync"/>; hier steht nur, was mit den Zeilen geschieht.
/// <see cref="Map"/> und <see cref="MarkVanished"/> sind rein und ohne Datenbank pruefbar.</para>
/// </summary>
internal sealed class DirectorySweepMerge
{
    private readonly AppDbContext _db;
    private readonly DirectoryGeocoder _geocoder;

    internal DirectorySweepMerge(AppDbContext db, DirectoryGeocoder geocoder)
    {
        _db = db;
        _geocoder = geocoder;
    }

    /// <summary>
    /// Die bekannten Eintraege der Foederation im Fenster plus die Nachzuegler, die die Liste
    /// liefert, die aber nicht (mehr) ins Fenster passen — nach chess-results-Nummer.
    /// </summary>
    internal async Task<(List<TournamentDirectoryEntry> Existing, Dictionary<string, TournamentDirectoryEntry> ById)>
        LoadKnownAsync(string federation, DateOnly from, List<DirectorySearchRows.CrawlerDirectoryRow> rows,
            CancellationToken ct)
    {
        // Die Spielorte MIT laden: ohne sie steht `entry.Venues` leer da, ReplaceVenues loescht
        // nichts, und die alten Zeilen sammeln sich mit jedem naechtlichen Lauf an.
        var existing = await _db.TournamentDirectoryEntries
            .Include(e => e.Venues)
            .Include(e => e.Sources)
            .Where(e => e.Federation == federation && (e.EndDate == null || e.EndDate >= from))
            .ToListAsync(ct);
        // NUR die mit Nummer: `ChessResultsId` ist seit 0.428.0 nullbar (FIDE-Ereignisse und die
        // Verbandskalender fuehren keine), und `ToDictionary` wirft bei einem NULL-Schluessel.
        // Am 2026-09-09 auf Dev gemessen: Sweep LIE (keine solchen Eintraege) 200, Sweep AUT (78)
        // 500 mit `ArgumentNullException (Parameter 'key')` — der naechtliche Lauf der HAUPTQUELLE
        // war fuer jede Foederation tot, in der eine Zusatzquelle etwas beigetragen hatte, und das
        // sind gerade die taeglichen Nachbarlaender. Ein Eintrag ohne Nummer gehoert hier ohnehin
        // nicht hin: diese Zuordnung laeuft ueber die Nummer, alles andere ueber Termin und Namen.
        var byId = existing.Where(e => e.ChessResultsId is not null)
            .ToDictionary(e => e.ChessResultsId!, StringComparer.Ordinal);

        // Und die Zeilen NACHLADEN, die zwar geliefert wurden, aber nicht ins Fenster passen.
        // Ohne das gilt ein VERSCHOBENES Turnier als neu: gespeichert mit Ende im Maerz, vom
        // Veranstalter auf November verlegt, faellt es beim Mai-Lauf aus `existing` heraus, die
        // Suche liefert es aber weiterhin — der Einfuegeversuch laeuft in den Unique-Index auf
        // ChessResultsId und reisst den GANZEN Lauf mit (SaveChanges liegt ausserhalb des
        // try/catch weiter oben). Ausgerechnet die Terminaenderung also, fuer die das
        // Aenderungs-Feature gebaut wurde. Fremde Foederation absichtlich NICHT eingeschraenkt:
        // ein Turnier kann auch die Foederation wechseln, und der Unique-Index gilt global.
        var strays = rows.Select(r => r.ChessResultsId).Where(id => !byId.ContainsKey(id)).Distinct().ToList();
        if (strays.Count > 0)
        {
            foreach (var stray in await _db.TournamentDirectoryEntries
                         .Include(e => e.Venues)
                         .Include(e => e.Sources)
                         .Where(e => strays.Contains(e.ChessResultsId)).ToListAsync(ct))
            {
                if (stray.ChessResultsId is not null) byId[stray.ChessResultsId] = stray;
            }
        }

        return (existing, byId);
    }

    /// <summary>
    /// Die Trefferzeilen auf die Eintraege uebertragen: bekannte aktualisieren (und Aenderungen
    /// fuer die Meldung sammeln), unbekannte anlegen.
    /// </summary>
    internal async Task<(List<TournamentDirectoryEntry> Added,
            List<(TournamentDirectoryEntry Entry, string? OldDate, string? OldLocation)> Changed, int Updated)>
        ApplyRowsAsync(List<DirectorySearchRows.CrawlerDirectoryRow> rows,
            Dictionary<string, TournamentDirectoryEntry> byId, DateTime now,
            Dictionary<string, (TournamentKind Kind, TournamentSystem System)>? artMap, CancellationToken ct)
    {
        var added = new List<TournamentDirectoryEntry>();
        var changed = new List<(TournamentDirectoryEntry Entry, string? OldDate, string? OldLocation)>();
        var updated = 0;

        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.ChessResultsId, out var entry))
            {
                var oldHash = entry.ChangeHash;
                var oldDate = DirectoryKeys.FormatRange(entry.StartDate, entry.EndDate);
                var oldLocation = entry.LocationText;
                var oldLocationText = entry.LocationText;

                await ApplyAsync(row, entry, now, artMap, ct);
                entry.MissedSweeps = 0;
                entry.LastMissAt = null;
                entry.RemovedAt = null;
                updated++;

                if (oldHash is not null && oldHash != entry.ChangeHash)
                {
                    changed.Add((entry, oldDate, oldLocation));
                    // Der Termin hat sich geaendert — die gespeicherten Spieltermine sind damit
                    // Makulatur. Der Vermerk faellt weg, der naechste Rundenplan-Durchgang holt
                    // sie neu; bis dahin gilt wieder der ganze Zeitraum, was zwar ungenau, aber
                    // nicht falsch ist.
                    if (!string.Equals(oldDate, DirectoryKeys.FormatRange(entry.StartDate, entry.EndDate),
                            StringComparison.Ordinal))
                    {
                        entry.RoundPlanCheckedAt = null;
                        // Die FASSUNG faellt mit weg: sie beantwortet „mit welchem Parser
                        // geholt" und haenge sonst an einem Eintrag ohne Vermerk — eine Aussage
                        // ueber einen Abruf, den es nicht mehr gibt.
                        entry.RoundPlanVersion = 0;
                    }
                }

                // Nur neu verorten, wenn sich der Ortstext wirklich geaendert hat - sonst wuerde
                // jede Nacht der gesamte Bestand durch den Gazetteer laufen.
                if (!string.Equals(oldLocationText, entry.LocationText, StringComparison.Ordinal))
                {
                    await _geocoder.GeocodeAsync(entry, ct);
                    // UI-Sweep 2026-10-11 (r2-t-dup-cards): der Schluessel liest den verorteten
                    // Ort — nach dem Verorten neu rechnen, nicht erst in der naechsten Nacht.
                    DirectoryKeys.ApplyGrouping(entry);
                }
            }
            else
            {
                entry = new TournamentDirectoryEntry
                {
                    // Fuer einen chess-results-Eintrag ist die Identitaet die Nummer dort — die
                    // Adressen, Abos und Teilen-Links, die es schon gibt, bleiben damit gueltig.
                    PublicId = row.ChessResultsId,
                    ChessResultsId = row.ChessResultsId,
                    FirstSeenAt = now,
                    CreatedAt = now,
                };
                await ApplyAsync(row, entry, now, artMap, ct);
                await _geocoder.GeocodeAsync(entry, ct);
                DirectoryKeys.ApplyGrouping(entry); // UI-Sweep 2026-10-11 (r2-t-dup-cards): s. o.
                _db.TournamentDirectoryEntries.Add(entry);
                added.Add(entry);
                // Sonst legt dieselbe Nummer, zweimal in EINER Trefferliste, zwei Zeilen an —
                // und laeuft in denselben Unique-Index.
                byId[entry.ChessResultsId] = entry;
            }
        }

        return (added, changed, updated);
    }

    /// <summary>
    /// Was die Liste nicht mehr liefert: Fehlschlag zaehlen, ab <paramref name="missedSweepsUntilRemoved"/>
    /// als abgesagt markieren. Gibt die abgesagten Eintraege zurueck.
    /// </summary>
    /// <param name="truncated">Die Trefferliste ist am Zeilenlimit abgeschnitten — dann zaehlt NICHTS als verschwunden
    /// (die Warnung dazu schreibt der Aufrufer).</param>
    internal static List<TournamentDirectoryEntry> MarkVanished(List<DirectorySearchRows.CrawlerDirectoryRow> rows,
        List<TournamentDirectoryEntry> existing, DateTime now, bool truncated, int missedSweepsUntilRemoved)
    {
        var seen = rows.Select(r => r.ChessResultsId).ToHashSet(StringComparer.Ordinal);
        var removed = new List<TournamentDirectoryEntry>();
        // NUR Eintraege MIT chess-results-Nummer. Ein Eintrag ohne Nummer kann in dieser
        // Trefferliste per Definition nicht vorkommen — er stammt aus einem Verbandskalender oder
        // dem FIDE-Kalender, und genau das ist deren Sinn: sie fuehren Turniere, die chess-results
        // NICHT hat. Ohne diese Bedingung sammelte jeder solche Eintrag jede Nacht einen
        // Fehlschlag und galt beim zweiten als ABGESAGT — samt Benachrichtigung an Abonnenten.
        // Am 2026-09-09 live passiert: das „ANSBACHER 24 Stunden Blitzturnier" aus dem Kalender des
        // Deutschen Schachbunds stand nach zwei GER-Sweeps auf abgesagt, obwohl es stattfindet.
        // Ein Eintrag einer Zusatzquelle kann nur von DIESER Quelle zurueckgezogen werden
        // (ExternalDirectorySource.RetireVanishedAsync).
        foreach (var entry in truncated
                     ? []
                     : existing.Where(e => e.RemovedAt == null && e.ChessResultsId is not null
                                           && !seen.Contains(e.ChessResultsId)))
        {
            // Hoechstens ein Fehlschlag je Karenzfenster: der Zaehler soll NAECHTE messen, nicht
            // Laeufe. Zwei Durchgaenge koennen Minuten auseinanderliegen — ein Aufhol-Lauf nach
            // einem Deploy (auf Dev mehrmals am Tag), ein Handlauf ueber directory-runs.sh, das
            // bis zu elf Durchgaenge macht. Ohne diese Sperre war ein Turnier, das chess-results
            // kurz nicht auswies, binnen einer Stunde abgesagt — samt Meldung an Abonnenten.
            if (entry.LastMissAt is { } last
                && now - last < ExternalDirectorySource.MissCooldown) continue;

            entry.MissedSweeps++;
            entry.LastMissAt = now;
            entry.UpdatedAt = now;
            if (entry.MissedSweeps < missedSweepsUntilRemoved) continue;
            entry.RemovedAt = now;
            removed.Add(entry);
        }

        return removed;
    }

    /// <summary>
    /// Uebertraegt eine Trefferzeile auf den Eintrag. <paramref name="artMap"/> ist das Ergebnis
    /// der vier Turnierart-Durchgaenge; <c>null</c> heisst „konnte nicht geklaert werden" und
    /// laesst eine bereits bekannte Art UND ein bekanntes System ausdruecklich in Ruhe.
    /// </summary>
    private async Task ApplyAsync(DirectorySearchRows.CrawlerDirectoryRow row, TournamentDirectoryEntry entry, DateTime now,
        Dictionary<string, (TournamentKind Kind, TournamentSystem System)>? artMap,
        CancellationToken ct)
    {
        Map(row, entry, now, artMap);
        await TournamentDirectoryService.NoteSourceAsync(_db, entry, DirectorySourceKind.ChessResults, row.ChessResultsId, now, ct);
    }

    /// <summary>Die Felder einer Trefferzeile auf den Eintrag schreiben — ohne Herkunftsvermerk (den setzt
    /// <see cref="ApplyAsync"/>). Rein: keine Datenbank, kein Netz.</summary>
    internal static void Map(DirectorySearchRows.CrawlerDirectoryRow row, TournamentDirectoryEntry entry, DateTime now,
        Dictionary<string, (TournamentKind Kind, TournamentSystem System)>? artMap)
    {
        entry.Name = Truncate(row.Name, 500);
        entry.Federation = Truncate(row.Federation, 3);
        entry.State = Truncate(DirectoryText.NullIfPlaceholder(row.State), 100);   // „-“ → null (d9-t-region-platzhalter)
        entry.StartDate = row.StartDate;
        entry.EndDate = row.EndDate;
        entry.StartsOnWeekend = row.StartDate is { } d
            && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        entry.LocationText = Truncate(row.Location, 500);
        entry.TimeControlText = Truncate(row.TimeControl, 300);
        entry.Speed = TournamentSpeedClassifier.Classify(row.TimeControl);
        entry.Organizer = Truncate(row.Organizer, 300);
        entry.Director = Truncate(row.Director, 300);
        entry.ChiefArbiter = Truncate(row.ChiefArbiter, 300);
        entry.Rounds = row.Rounds;
        entry.PlayerCount = row.PlayerCount;
        entry.UpstreamUpdatedAt = row.LastUpdatedApproxUtc;
        entry.ChangeHash = DirectoryKeys.ComputeChangeHash(row.StartDate, row.EndDate, row.Location);

        // Nur wenn ALLE vier Durchgaenge standen. Ein Turnier, das in keinem davon auftauchte,
        // bleibt bewusst unangetastet: die Arten decken zwar alles ab, was die Suche anbietet,
        // aber „in keiner Liste" ist eine Aussage ueber die Quelle, nicht ueber das Turnier.
        if (artMap is not null && artMap.TryGetValue(row.ChessResultsId, out var art))
        {
            entry.Kind = art.Kind;
            entry.System = art.System;
        }
        // Publikum und Format haengen am Namen (Alter/Geschlecht) bzw. an Art und Dauer (Liga) —
        // beides also NACH den Feldern oben und nach der Turnierart auswerten.
        entry.AgeGroups = TournamentClassifier.AgeGroupsOf(entry.Name);
        entry.Gender = TournamentClassifier.GenderOf(entry.Name);
        entry.IsLeague = TournamentClassifier.LooksLikeLeague(
            entry.Name, entry.Kind, entry.StartDate, entry.EndDate);

        // Nach Name, Termin und Ort - der Gruppenschluessel liest genau diese Felder.
        DirectoryKeys.ApplyGrouping(entry);
        entry.LastSeenAt = now;
        entry.UpdatedAt = now;
    }
}
