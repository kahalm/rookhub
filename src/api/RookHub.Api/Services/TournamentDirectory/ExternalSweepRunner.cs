using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Was eine Zusatzquelle dem <see cref="ExternalSweepRunner"/> sagen muss: wie ihre Zeilen heissen,
/// wem sie gehoeren und wie eine Zeile auf den EIGENEN Eintrag abgebildet wird. Alles Uebrige —
/// Reihenfolge, Zaehlung, Zuordnung, Zwischenspeichern, Fehlerfang, Verschwunden-Erkennung — macht
/// der Runner fuer alle Quellen gleich.
/// </summary>
/// <typeparam name="TRow">Die Zeile, wie der Crawler-Abruf der Quelle sie liefert.</typeparam>
internal interface IDirectorySourceAdapter<in TRow>
{
    /// <summary>Die Quellenart der Herkunftsvermerke.</summary>
    DirectorySourceKind Kind { get; }

    /// <summary>Kurzname im Log („FRSah", „wcu") — Zuordnungs- und Zwischenstand-Meldung.</summary>
    string LogName { get; }

    /// <summary>Zwischenstand je so viele verarbeitete Zeilen (je Quelle, meist 25).</summary>
    int SaveEvery => ExternalSweepRunner.DefaultSaveEvery;

    /// <summary>
    /// Die Kennung der Zeile bei der Quelle, so wie sie in den Herkunftsvermerk und in die
    /// Verschwunden-Erkennung geht; <c>null</c> = Zeile ohne Kennung (wird ganz uebergangen).
    /// </summary>
    string? SourceKey(TRow row);

    /// <summary>Der Termin; <c>null</c> = nicht lesbar (die Zeile gilt trotzdem als geliefert).</summary>
    DateOnly? Start(TRow row);

    string Name(TRow row);

    /// <summary>Die <c>PublicId</c> des eigenen Eintrags.</summary>
    string PublicId(TRow row);

    /// <summary>
    /// Die Zeile ist kein (stattfindendes) Turnier — abgesagt, Verbandssitzung. Dann wird nichts
    /// angelegt und ein bestehender eigener Eintrag zurueckgezogen.
    /// </summary>
    bool Withdrawn(TRow row) => false;

    /// <summary>Die Foederation fuer den Abgleich und fuer einen neu angelegten Eintrag.</summary>
    string? Federation(TRow row);

    /// <summary>Der Ortstext fuer den Abgleich (<see cref="ExternalDirectorySource.MatchHint.Place"/>).</summary>
    string? MatchPlace(TRow row);

    /// <summary>Die Seite des Turniers bei der Quelle (Herkunftsvermerk).</summary>
    string? Url(TRow row);

    /// <summary>
    /// Laeuft einmal je Lauf, NACHDEM feststeht, dass es Zeilen gibt, und VOR der ersten Zeile
    /// (Chess Scotland stellt dort Altbestand-Vermerke um).
    /// </summary>
    Task BeforeLoopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Laeuft je Zeile VOR der Zuordnung (nach der Absage-Pruefung) — fuer eine Detailseite, die
    /// den exakten Zuordnungsschluessel traegt (ICU). Was dabei anfaellt, legt die Quelle an ihrer
    /// Zeile ab; ein Schluessel, der erst nach der Entscheidung eintrifft, waere wertlos.
    /// </summary>
    /// <returns>Ob die Zeile damit als „aktualisiert" zaehlt (ICU: Detailseite gelesen).</returns>
    Task<bool> BeforeMatchAsync(TRow row, CancellationToken ct) => Task.FromResult(false);

    /// <summary>
    /// Die chess-results-Nummer, die die Quelle SELBST nennt (chess.sk in der Liste, ICU auf der
    /// Detailseite oder, in spaeteren Naechten, ueber den Vermerk am frueher getroffenen Eintrag). Gibt es dazu einen Eintrag, ist das ein exakter Treffer und der
    /// Namensvergleich entfaellt; <c>null</c>/leer = nur der Namensvergleich.
    /// </summary>
    string? ChessResultsId(TRow row) => null;

    /// <summary>
    /// Laeuft nach beiden Suchen (eigener Eintrag, Zuordnung) und vor jeder Entscheidung — FFE laedt
    /// dort die Herkunftsvermerke der Funde nach.
    /// </summary>
    Task AfterLookupAsync(TournamentDirectoryEntry? own, TournamentDirectoryEntry? match, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// Laeuft einmal, wenn der Runner den eigenen Eintrag NEU angelegt hat, vor
    /// <see cref="ApplyAsync"/> — fuer Felder, die nur ein neuer Eintrag bekommt (chess.sk: die
    /// chess-results-Nummer der Quelle, eine leere als <c>null</c>).
    /// </summary>
    void OnCreated(TournamentDirectoryEntry own, TRow row) { }

    /// <summary>
    /// Kennt chess-results das Turnier schon, darf die Quelle dort LUECKEN fuellen (Bedenkzeit,
    /// Rundenzahl …), nie vorhandene Werte ueberschreiben. Laeuft vor dem Herkunftsvermerk.
    /// </summary>
    /// <returns>Ob etwas ergaenzt wurde — zaehlt als „aktualisiert".</returns>
    bool FillMatch(TournamentDirectoryEntry match, TRow row) => false;

    /// <summary>
    /// Die Zeile auf den eigenen Eintrag abbilden — Felder, „gesehen", Klassifizierung,
    /// Herkunftsvermerk und Verortung, in der Reihenfolge der Quelle.
    /// </summary>
    /// <returns>Ob die Zeile als „aktualisiert" zaehlt (je Quelle: verortet, Bedenkzeit geklaert …).</returns>
    Task<bool> ApplyAsync(TournamentDirectoryEntry own, TRow row, DateOnly start, DateTime now, CancellationToken ct);

    /// <summary>Die Abschlusszeile im Log — Wortlaut und Platzhalter je Quelle.</summary>
    void LogSummary(ILogger log, int read, int added, int updated, int matched, int retired);
}

/// <summary>
/// Die EINE Abgleichschleife der Zusatzquellen (Codereview 2026-09-29, Z7). Vorher stand sie in
/// jedem <c>…DirectorySweepService</c> von Hand, mit 727 duplizierten Normzeilen; die Lehren aus
/// <c>.claude/rules/turnierverzeichnis.md</c> (geliefert vor lesbar, Herkunftsvermerk wandert,
/// Verschwunden-Erkennung mit Bremse) gelten damit an einer Stelle.
///
/// <para>Je Zeile: Kennung pruefen, als GELIEFERT vermerken, Termin und Namen pruefen, eigenen
/// Eintrag suchen, Absage/Sitzung zurueckziehen, sonst (nach <c>BeforeMatchAsync</c>) einem
/// chess-results-Eintrag zuordnen — per Nummer der Quelle, sonst per Namensvergleich; ein Treffer auf
/// den eigenen Eintrag selbst zaehlt nicht — und den eigenen dann zurueckziehen, oder den eigenen anlegen bzw. aktualisieren. Alle
/// <see cref="IDirectorySourceAdapter{TRow}.SaveEvery"/> Zeilen ein Zwischenstand; am Ende wird IMMER gespeichert, ein Fehler dabei
/// nur gewarnt. Danach die Abschlusszeile und die Verschwunden-Erkennung.</para>
/// </summary>
internal static class ExternalSweepRunner
{
    /// <summary>Zwischenstand je so viele verarbeitete Zeilen, wenn die Quelle nichts anderes sagt.</summary>
    internal const int DefaultSaveEvery = 25;

    /// <param name="unreadable">
    /// Kennungen von Zeilen, die schon der Abruf nicht lesen konnte (KNSB: kein lesbarer Termin
    /// im Zeilentyp). Sie gelten als GELIEFERT, laufen aber nicht durch die Schleife.
    /// </param>
    public static async Task<ExternalSweepResult> RunAsync<TRow>(AppDbContext db, ILogger log,
        IDirectorySourceAdapter<TRow> source, IReadOnlyCollection<TRow> rows, CancellationToken ct,
        IReadOnlyCollection<string>? unreadable = null)
    {
        unreadable ??= [];
        if (rows.Count == 0 && unreadable.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        int added = 0, updated = 0, matched = 0, retired = 0, processed = 0;
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten.
        var delivered = new List<string>(unreadable);
        await source.BeforeLoopAsync(ct);

        try
        {
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                if (source.SourceKey(row) is not { Length: > 0 } key) continue;
                // Geliefert ist geliefert: die Quelle FUEHRT diese Zeile. Ob WIR sie lesen
                // koennen, ist eine andere Frage. Stand das Eintragen erst hinter den Pruefungen,
                // galt eine Zeile mit unlesbarem Termin als verschwunden und war nach zwei Laeufen
                // abgesagt — und ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle.
                delivered.Add(key);
                if (source.Start(row) is not { } start || source.Name(row).Length == 0) continue;

                processed++;
                var publicId = source.PublicId(row);
                var own = await ExternalDirectorySource.FindOwnAsync(db, publicId, ct);

                if (source.Withdrawn(row))
                {
                    if (own is not null && own.RemovedAt is null)
                    {
                        own.RemovedAt = now;
                        retired++;
                    }
                    continue;
                }

                if (await source.BeforeMatchAsync(row, ct)) updated++;

                var federation = source.Federation(row);
                // Erst die Nummer, die die Quelle selbst nennt, dann der Name: nur die Nummer muss
                // nicht raten. Ohne Nummer stellt FindByChessResultsIdAsync keine Abfrage.
                var match = await ExternalDirectorySource.FindByChessResultsIdAsync(db, source.ChessResultsId(row), ct)
                            ?? await ExternalDirectorySource.FindMatchAsync(db, federation, start, source.Name(row),
                                new ExternalDirectorySource.MatchHint(source.Kind, key, source.MatchPlace(row)), ct);
                // Ein eigener Eintrag, der die chess-results-Nummer selbst traegt (chess.sk: Nummer
                // genannt, Turniersuche kennt sie noch nicht), wird von beiden Suchen wiedergefunden.
                // Das ist keine Zuordnung: er nimmt den normalen Weg, sonst kaemen Termin- und
                // Ortsaenderungen der Quelle nie mehr an.
                if (IsSelf(own, match)) match = null;
                await source.AfterLookupAsync(own, match, ct);

                if (match is not null)
                {
                    var filled = source.FillMatch(match, row);
                    await ExternalDirectorySource.NoteSourceAsync(db, match,
                        source.Kind, key, source.Url(row), now, ct);
                    matched++;
                    if (filled) updated++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        // Die Vorlage beginnt mit dem Kurznamen, wie in den handgeschriebenen Schleifen —
                        // die Log-Vorlage (nicht nur der Text) bleibt damit je Quelle dieselbe.
                        log.LogInformation(source.LogName + ": {PublicId} geht in {Target} auf ({Name})",
                            own!.PublicId, match.PublicId, match.Name);
                    }
                    if (processed % source.SaveEvery == 0) await db.SaveChangesAsync(ct);
                    continue;
                }

                if (own is null)
                {
                    own = new TournamentDirectoryEntry
                    {
                        PublicId = publicId,
                        ChessResultsId = null,
                        Federation = federation,
                        FirstSeenAt = now,
                    };
                    db.TournamentDirectoryEntries.Add(own);
                    source.OnCreated(own, row);
                    added++;
                }

                if (await source.ApplyAsync(own, row, start, now, ct)) updated++;

                if (processed % source.SaveEvery == 0) await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { log.LogWarning(ex, source.LogName + ": Zwischenstand konnte nicht gespeichert werden"); }
        }

        source.LogSummary(log, rows.Count, added, updated, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(db, source.Kind, delivered, now, log, ct);

        return new ExternalSweepResult(rows.Count, added, updated, matched, retired);
    }

    /// <summary>Ob der Treffer der eigene Eintrag selbst ist.</summary>
    private static bool IsSelf(TournamentDirectoryEntry? own, TournamentDirectoryEntry? match) =>
        own is not null && match is not null && (ReferenceEquals(own, match) || (own.Id != 0 && own.Id == match.Id));

    /// <summary>
    /// Ein Eintrag wurde in diesem Lauf von seiner Quelle geliefert: zuletzt gesehen jetzt, kein
    /// Fehlschlag, nicht zurueckgezogen.
    /// </summary>
    public static void MarkSeen(TournamentDirectoryEntry own, DateTime now)
    {
        own.LastSeenAt = now;
        own.MissedSweeps = 0;
        own.RemovedAt = null;
    }

    /// <summary>
    /// Die Verortung der Quellen ohne eigene Koordinaten (WCU, CFC, chess.hu …): nur mit Ortstext, nur wenn er
    /// sich geaendert hat oder noch kein Pin da ist, und ein mehrdeutiger Treffer setzt nichts.
    /// </summary>
    /// <returns>Ob ein Pin gesetzt wurde.</returns>
    public static async Task<bool> LocateAsync(GeocodingService geocoding, TournamentDirectoryEntry own,
        string? location, string? state, string? federation, bool locationChanged, CancellationToken ct)
    {
        if (location is not { Length: > 0 } || !(locationChanged || own.Lat is null)) return false;

        var hit = await geocoding.ResolveAsync(location, state, federation, ct);
        if (hit is null || hit.Source == GeoSource.Ambiguous) return false;

        own.Lat = hit.Lat;
        own.Lon = hit.Lon;
        own.GeoSource = hit.Source;
        own.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
        return true;
    }

    /// <summary>
    /// Bedenkzeit und Rundenzahl an einem chess-results-Eintrag ergaenzen, wo sie FEHLEN (FSI,
    /// schachbund) — beides fehlt dort oefter, als man denkt. Die Bedenkzeit-Klasse wird nur
    /// nachgezogen, wenn sie noch unbekannt ist.
    /// </summary>
    /// <returns>Ob etwas ergaenzt wurde.</returns>
    public static bool FillTimeControlGaps(TournamentDirectoryEntry match, string? timeControl, int? rounds)
    {
        var text = match.TimeControlText;
        var known = match.Rounds;
        var changed = ExternalDirectorySource.FillIfEmpty(timeControl, ref text, 300);
        changed |= ExternalDirectorySource.FillIfEmpty(rounds, ref known);
        match.TimeControlText = text;
        match.Rounds = known;
        if (changed && match.Speed == TournamentSpeed.Unknown)
            match.Speed = TournamentSpeedClassifier.Classify(match.TimeControlText);
        return changed;
    }
}
