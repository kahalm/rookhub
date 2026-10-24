using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Controllers.TournamentDirectory;

/// <summary>
/// Wartung des Turnierverzeichnisses: Sweep von Hand ausloesen, Gazetteer importieren,
/// Geocoding-Qualitaet ansehen und einzelne Koordinaten korrigieren.
///
/// Gesamter Controller hinter <see cref="Permissions.TournamentsManage"/> - ein blosser
/// IsAdmin-Check im Methodenrumpf haette dieselbe Wirkung, aber die Berechtigung waere nicht
/// zuteilbar und der Endpunkt taeuchte in keiner Rechteuebersicht auf.
/// </summary>
[ApiController]
[Route("api/admin/tournament-directory")]
[Authorize]
[HasPermission(Permissions.TournamentsManage)]
public class AdminTournamentDirectoryController : BaseApiController
{
    private readonly TournamentDirectoryService _directory;
    private readonly TournamentDirectoryMaintenanceService _maintenance;
    private readonly GazetteerImportService _gazetteer;

    /// <summary>
    /// Nur die Dienste, die mehrere Endpunkte brauchen. Die Quellen-Ausloeser holen ihren Dienst
    /// je Aufruf ueber <c>[FromServices]</c> — vorher standen hier 25 Konstruktor-Abhaengigkeiten,
    /// und jede Anfrage baute alle Quellen-Dienste, um einen davon zu benutzen (Codereview Z8).
    /// </summary>
    public AdminTournamentDirectoryController(
        TournamentDirectoryService directory,
        TournamentDirectoryMaintenanceService maintenance,
        GazetteerImportService gazetteer)
    {
        _directory = directory;
        _maintenance = maintenance;
        _gazetteer = gazetteer;
    }

    /// <summary>
    /// Nimmt die naechsten Turniere vor, deren Spielort ueber die VEREINSNAMEN aufzuloesen ist —
    /// der Abkuerzungs-Fall („St.Veit" ist Tirol ODER, gemeint, „St. Veit an der Glan" in
    /// Kaernten). Kostet EINEN Seitenabruf je Turnier und ist deshalb gedeckelt; jedes Turnier
    /// wird nur einmal versucht (`TeamHintCheckedAt`).
    /// </summary>
    [HttpPost("disambiguate")]
    public async Task<IActionResult> Disambiguate([FromServices] VenueDisambiguationService disambiguation,
        [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await disambiguation.RunAsync(limit, ct);
        return Ok(new { result.Checked, result.Resolved, result.Failed });
    }

    /// <summary>Zustand je Foederation plus die Geocoding-Quote - der Gesundheitsblick.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var status = await _maintenance.StatusAsync(ct);
        return Ok(new
        {
            totalEntries = status.TotalEntries,
            geocoded = status.Geocoded,
            byGeoSource = status.ByGeoSource,
            gazetteerPlaces = status.GazetteerPlaces,
            sweeps = status.Sweeps.Select(s => new
            {
                s.Federation, s.LastSweptAt, s.LastAttemptedAt, s.LastRowCount,
                s.LastError, s.ConsecutiveFailures
            }),
        });
    }

    /// <summary>
    /// Sweep fuer die angegebenen Foederationen sofort ausfuehren. Laeuft im Anfrageweg (der
    /// Aufrufer soll das Ergebnis sehen) - bei vielen Foederationen dauert das entsprechend,
    /// weil der Crawler seine Anfragen ohnehin serialisiert.
    /// </summary>
    [HttpPost("sweep")]
    public async Task<IActionResult> Sweep([FromBody] SweepRequest request, CancellationToken ct)
    {
        var federations = (request.Federations ?? [])
            .Select(f => f.Trim().ToUpperInvariant())
            .Where(f => f.Length == 3 && f.All(char.IsAsciiLetterUpper))
            .Distinct()
            .Take(20)
            .ToList();

        if (federations.Count == 0)
            return BadRequest(new { message = "Provide 1-20 three-letter federation codes." });

        var results = await _directory.RunSweepAsync(federations, ct);
        return Ok(results);
    }

    /// <summary>Postleitzahlen eines Landes (ISO-3166-1-alpha-2) aus dem GeoNames-Export laden.</summary>
    [HttpPost("gazetteer/postal/{iso2}")]
    public async Task<IActionResult> ImportPostal(string iso2, CancellationToken ct)
    {
        var result = await _gazetteer.ImportPostalCodesAsync(iso2, ct);
        return result.Error is null ? Ok(result) : StatusCode(502, result);
    }

    /// <summary>Weltweite Ortsliste (cities15000) laden - Grundlage fuer Laender ohne PLZ-Datensatz.</summary>
    [HttpPost("gazetteer/cities")]
    public async Task<IActionResult> ImportCities(CancellationToken ct)
    {
        var result = await _gazetteer.ImportCitiesAsync(ct);
        return result.Error is null ? Ok(result) : StatusCode(502, result);
    }

    /// <summary>
    /// Fuellt die zweite Schreibweise (<c>GeoPlace.NameTranscribed</c>) fuer den vorhandenen
    /// Bestand nach — EINMALIG nach dem Deploy von 0.456.0.
    ///
    /// <para>Warum es das ueberhaupt gibt: die Spalte entsteht beim Import, und der laedt die
    /// Daten bei GeoNames herunter. Der Bestand umfasst 24 Laender und ueber 200 000 Zeilen; sie
    /// nur wegen einer berechneten Spalte alle neu zu holen, waere eine halbe Stunde
    /// Fremdverkehr fuer eine Rechnung, die lokal in Sekunden laeuft. Die Schreibweise wird aus
    /// dem gespeicherten <c>Name</c> berechnet, es geht keine Anfrage nach draussen.</para>
    ///
    /// <para>Mehrfach ausfuehrbar: gefuellte Zeilen bleiben unberuehrt.</para>
    /// </summary>
    [HttpPost("gazetteer/transcribe")]
    public async Task<IActionResult> Transcribe(CancellationToken ct)
    {
        var written = await _maintenance.TranscribeGazetteerAsync(ct);
        return Ok(new { written });
    }

    /// <summary>Die Eintraege, die der Gazetteer nicht verorten konnte - Arbeitsliste fuer Korrekturen.</summary>
    [HttpGet("ungeocoded")]
    public async Task<IActionResult> Ungeocoded([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        return Ok(await _maintenance.UngeocodedAsync(limit, ct));
    }

    /// <summary>
    /// Koordinaten von Hand setzen. Die Quelle wird auf <see cref="GeoSource.Manual"/> gesetzt -
    /// damit ueberschreibt der naechtliche Sweep die Korrektur nicht wieder.
    /// </summary>
    [HttpPut("{id}/coordinates")]
    public async Task<IActionResult> SetCoordinates(
        string id, [FromBody] CoordinateInput input, CancellationToken ct)
    {
        if (input.Lat is < -90 or > 90 || input.Lon is < -180 or > 180)
            return BadRequest(new { message = "Coordinates out of range." });

        var entry = await _maintenance.SetCoordinatesAsync(id, input.Lat, input.Lon, input.PlaceName, ct);
        if (entry is null) return NotFound();

        return Ok(new { entry.PublicId, entry.ChessResultsId, entry.Lat, entry.Lon, GeoSource = entry.GeoSource.ToString() });
    }

    /// <summary>
    /// Den FIDE-Kalender sofort einlesen — sonst wartet er auf den naechtlichen Lauf.
    ///
    /// <para>Ein Abruf je Jahr. Die Jahre werden im Dienst AUFSTEIGEND abgearbeitet, weil die
    /// Jahres-Zuordnung eines Ereignisses ueber den Jahreswechsel daran haengt.</para>
    /// </summary>
    [HttpPost("fide")]
    public async Task<IActionResult> Fide([FromServices] FideDirectorySweepService fide,
        
        [FromQuery] string? years = null, CancellationToken ct = default)
    {
        var list = new List<int>();
        foreach (var part in (years ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var year) || year is < 2000 or > 2100)
                return BadRequest(new { message = "years must be a comma-separated list of years between 2000 and 2100." });
            list.Add(year);
        }
        // Ohne Angabe das laufende Jahr plus die zwei naechsten — weiter voraus fuehrt FIDE
        // praktisch nichts.
        if (list.Count == 0) list = [.. Enumerable.Range(DateTime.UtcNow.Year, 3)];

        var result = await fide.RunAsync(list, ct);
        return result.Succeeded
            ? Ok(new { result.Fetched, result.Added, result.Updated, result.MergedIntoExisting })
            : StatusCode(502, new { message = result.Error, result.Fetched, result.Added });
    }

    /// <summary>
    /// Den HERKUNFTSVERMERK fuer den Altbestand nachtragen: jeder bestehende Eintrag stammt aus
    /// der chess-results-Turniersuche, seine <c>ChessResultsId</c> ist die Nummer dort.
    ///
    /// <para>Der naechtliche Sweep vermerkt das von selbst — aber erst, wenn er die Foederation
    /// wieder vornimmt, und die Rotation braucht dafuer eine Woche. Braucht kein Netz.</para>
    /// </summary>
    [HttpPost("backfill-sources")]
    public async Task<IActionResult> BackfillSources(CancellationToken ct = default)
    {
        return Ok(new { added = await _maintenance.BackfillSourcesAsync(ct) });
    }

    /// <summary>
    /// Die SPIELTERMINE langlaufender Turniere nachtragen — von Hand auslösbar, damit der
    /// Bestand nicht auf die naechtlichen Chargen warten muss.
    ///
    /// <para>Ein Seitenabruf je Turnier (chess-results art=14), deshalb gedeckelt. Siehe
    /// <see cref="TournamentRoundPlanService"/>: Start und Ende einer Liga sagen nicht, wann
    /// gespielt wird, und der Kalender zeigte sie deshalb an rund 200 Tagen statt an ihren elf
    /// Spieltagen.</para>
    /// </summary>
    /// <param name="retryEmpty">
    /// Auch Eintraege erneut vornehmen, die als geprueft gelten, aber keinen Termin haben.
    /// Gebraucht, wenn das HOLEN kaputt war und deshalb lauter leere Plaene vermerkt wurden —
    /// ohne das bliebe die Behebung fuer den bestehenden Bestand wirkungslos.
    /// </param>
    [HttpPost("round-plans")]
    public async Task<IActionResult> RoundPlans([FromServices] TournamentRoundPlanService roundPlans,
        [FromQuery] int limit = 100,
        [FromQuery] bool retryEmpty = false, CancellationToken ct = default)
    {
        var result = await roundPlans.RunAsync(Math.Clamp(limit, 1, 1000), retryEmpty, ct);
        return Ok(new { result.Checked, result.WithPlan, result.Failed });
    }

    /// <summary>
    /// Den Kalender des slowenischen Verbands (SZS) lesen.
    ///
    /// <para>Das krasseste Verhaeltnis aller geprueften Quellen: 78 kuenftige Turniere gegen 7 auf
    /// chess-results. Und nicht bloss Vorlauf — im Rueckblick auf einen abgeschlossenen Monat
    /// erscheinen 36 von 88 Eintraegen dort NIE.</para>
    ///
    /// <para>Sein Sonderwert: die Trefferliste traegt die POSTLEITZAHL, die Verortung braucht also
    /// keinen Abruf je Turnier. Abgesagte Turniere (im Namen gekennzeichnet) werden nicht angelegt
    /// und bestehende zurueckgezogen.</para>
    /// </summary>
    [HttpPost("szs")]
    public async Task<IActionResult> Szs([FromServices] SzsDirectorySweepService szs,
        CancellationToken ct = default)
    {
        var result = await szs.RunAsync(ct);
        return Ok(new { result.Read, result.Added, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Kalender der Irish Chess Union lesen — 81 kuenftige Turniere, 94 % davon nicht auf
    /// chess-results.
    ///
    /// <para>30 der 81 bringen ihre Koordinaten aus der Trefferseite mit. <paramref name="details"/>
    /// deckelt den teuren Teil: die Detailseite kauft nur den exakten Zuordnungsschluessel und die
    /// Meldezahl, keine weiteren Koordinaten. Mit 0 bleibt sie ganz aus.</para>
    /// </summary>
    [HttpPost("icu")]
    public async Task<IActionResult> Icu([FromServices] IcuDirectorySweepService icu,
        [FromQuery] int? details = null, CancellationToken ct = default)
    {
        var result = await icu.RunAsync(Clamp(details), ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Kalender des franzoesischen Verbands (FFE) lesen — der groesste Ertrag der dritten
    /// Runde: von 40 gegengeprueften Turnieren stehen ZWEI auf chess-results.
    ///
    /// <para><paramref name="months"/> Monatsseiten (Vorgabe 12) in EINEM Durchgang; die
    /// Turnierseite je Turnier genau einmal, gedeckelt ueber <paramref name="details"/>. Sie wird
    /// gebraucht: Enddatum, Bedenkzeit und Postleitzahl gibt es nur dort.</para>
    /// </summary>
    [HttpPost("ffe")]
    public async Task<IActionResult> Ffe([FromServices] FfeDirectorySweepService ffe,
        [FromQuery] int months = 12,
        [FromQuery] int? details = null, CancellationToken ct = default)
    {
        var result = await ffe.RunAsync(Math.Clamp(months, 1, 24), Clamp(details), ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Aktivitaeten-Feed des norwegischen Verbands lesen — chess-results kennt fuer NOR NULL
    /// kuenftige Turniere, der Feed 81.
    ///
    /// <para><b>Die Verortung ist die Schwaeche dieser Quelle</b>: der Feed hat kein Adressfeld,
    /// der Ort steht nur auf der Detailseite und dort nur bei einem Fuenftel. Gemessen sind rund
    /// 25 % verortbar — nach einem norwegischen Postleitzahl-Import etwa ein Drittel.</para>
    /// </summary>
    [HttpPost("sjakk")]
    public async Task<IActionResult> Sjakk([FromServices] SjakkDirectorySweepService sjakk,
        [FromQuery] int? details = null, CancellationToken ct = default)
    {
        var result = await sjakk.RunAsync(Clamp(details), ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Kalender von Chess Scotland lesen — 44 kuenftige Turniere bis 2028 in EINEM Abruf,
    /// mit strukturierter Bedenkzeit-Klasse.
    ///
    /// <para><b>Auch hier ist der Ort die Schwaeche</b>: die Liste nennt keinen, und in den
    /// Ausschreibungen der Detailseiten steht bei knapp einem Fuenftel eine Postleitzahl. Solange
    /// das Lexikon fuer Grossbritannien keine Postleitzahlen kennt, traegt allein ein Ortsname im
    /// selben Satz.</para>
    /// </summary>
    [HttpPost("chess-scotland")]
    public async Task<IActionResult> ChessScotland([FromServices] ChessScotlandDirectorySweepService chessScotland,
        [FromQuery] int? details = null,
        CancellationToken ct = default)
    {
        var result = await chessScotland.RunAsync(Clamp(details), ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Kalender des rumaenischen Verbands (FRSah) lesen — EIN Abruf, 31 kuenftige Turniere.
    ///
    /// <para>Anders als bei England steckt die Spielstaette schon im Ereignis; der zweite Abruf
    /// auf den Spielstaetten-Endpunkt entfaellt. Die fuenf Eintraege OHNE Spielstaette sind genau
    /// die nationalen Mannschaftsligen — und genau die fehlen auf chess-results.</para>
    /// </summary>
    [HttpPost("frsah")]
    public async Task<IActionResult> Frsah([FromServices] FrsahDirectorySweepService frsah,
        CancellationToken ct = default)
    {
        var result = await frsah.RunAsync(ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Saisonkalender der Welsh Chess Union lesen — EIN Abruf, die billigste aller Quellen.
    ///
    /// <para>30 der 38 Turniere nennen eine vollstaendige Postleitzahl. Die Quelle hat KEINE
    /// eigene Kennung; sie wird aus Termin und Anschrift gebildet, bewusst ohne den Namen (der
    /// wird dort nachweislich nachtraeglich korrigiert).</para>
    /// </summary>
    [HttpPost("wcu")]
    public async Task<IActionResult> Wcu([FromServices] WcuDirectorySweepService wcu,
        CancellationToken ct = default)
    {
        var result = await wcu.RunAsync(ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Ankuendigungskalender der Chess Federation of Canada lesen — 171 kuenftige Eintraege
    /// gegen 68 auf chess-results, in zwei Abrufen (Seite fuer den Dateinamen, dann die Datei).
    ///
    /// <para><b>Der Spielort ist eine Stadt ohne Anschrift</b>, es gibt weder Strasse noch
    /// Postleitzahl — der genaueste Weg des Geocoders faellt hier also aus. Dafuer kommt das
    /// Provinz-Kuerzel strukturiert mit und traegt den Rueckfall auf die Regionsmitte.</para>
    ///
    /// <para>Auslands- und Online-Termine siebt schon der Crawler aus, und zwar ueber die PROVINZ:
    /// ein FIDE-Turnier in Usbekistan steht dort als Turnierart „OTB" und nur die Provinz „FO"
    /// verraet es.</para>
    /// </summary>
    [HttpPost("cfc")]
    public async Task<IActionResult> Cfc([FromServices] CfcDirectorySweepService cfc,
        CancellationToken ct = default)
    {
        var result = await cfc.RunAsync(ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Terminkalender des niederlaendischen Verbands lesen — 177 kuenftige Eintraege gegen 12
    /// auf chess-results, in zwei Abrufen.
    ///
    /// <para><b>Diese Quelle nennt keinen Spielort</b>, und zwar strukturell: die Liste hat kein
    /// Ortsfeld, er stuende nur auf der Detailseite. Bei einer geforderten Wartezeit von 15
    /// Sekunden waeren das 45 Minuten fuer den Erstbestand — bewusst nicht gebaut. Die Eintraege
    /// erscheinen deshalb in Liste und Kalender, aber nicht auf der Karte. Die Bedenkzeit-Klasse
    /// kommt dafuer strukturiert aus der Quelle.</para>
    /// </summary>
    [HttpPost("knsb")]
    public async Task<IActionResult> Knsb([FromServices] KnsbDirectorySweepService knsb,
        CancellationToken ct = default)
    {
        var result = await knsb.RunAsync(ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Der Deckel fuer die Detailabrufe: <c>null</c> heisst „nimm die Vorgabe aus der
    /// Konfiguration", ein Wert wird auf 0..1000 begrenzt. 0 schaltet den teuren Teil ab.
    /// </summary>
    private static int? Clamp(int? details) =>
        details is { } value ? Math.Clamp(value, 0, 1000) : null;

    /// <summary>
    /// Den Kalender des englischen Verbands (ECF) lesen.
    ///
    /// <para>256 kuenftige Turniere, 86 % davon nicht auf chess-results — und als einzige Quelle
    /// liefert sie KOORDINATEN mit (fuer zwei Drittel der Turniere entfaellt das Geocoding
    /// damit). Ein Durchgang holt zwei geblaetterte Endpunkte mit der Wartezeit aus der
    /// robots.txt der Quelle und dauert rund drei Minuten.</para>
    ///
    /// <para><c>Updated</c> in der Antwort zaehlt die VERORTETEN Eintraege.</para>
    /// </summary>
    [HttpPost("ecf")]
    public async Task<IActionResult> Ecf([FromServices] EcfDirectorySweepService ecf,
        CancellationToken ct = default)
    {
        var result = await ecf.RunAsync(ct);
        return Ok(new
        {
            result.Read, result.Added, result.Matched, result.Retired, Located = result.Updated,
        });
    }

    /// <summary>
    /// Die Turnierdatenbank des Deutschen Schachbunds lesen.
    ///
    /// <para>Ihr Wert ist nicht die Menge (rund 104 Eintraege), sondern die ART: ein reines
    /// Meldesystem ohne Ergebnismeldung, in dem Vereins-Abendturniere, Jugend-Cups, Fernschach,
    /// Problemschach und Schach960 stehen — Kategorien, die chess-results praktisch nie
    /// fuehrt.</para>
    ///
    /// <para>Der Durchgang dauert einige Minuten: zwei Abrufe je Region mit der Wartezeit, die
    /// die Quelle in ihrer robots.txt nennt.</para>
    /// </summary>
    [HttpPost("schachbund")]
    public async Task<IActionResult> Schachbund([FromServices] SchachbundDirectorySweepService schachbund,
        CancellationToken ct = default)
    {
        var result = await schachbund.RunAsync(ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Kalender des polnischen Verbands (chessarbiter.com) lesen — die ergiebigste
    /// Einzelquelle: 611 kuenftige Turniere in EINEM Abruf.
    ///
    /// <para>Zweistufig: die Liste kostet einen Abruf, die Detailseite (Enddatum, Bedenkzeit,
    /// Rundenzahl, System, Teilnehmerzahl) einen JE TURNIER. Sie wird nur fuer noch unbekannte
    /// Turniere geholt und ist mit <paramref name="details"/> gedeckelt (Vorgabe:
    /// <c>TournamentDirectory:ChessArbiterDetailBatchSize</c>, 150). <c>Updated</c> in der
    /// Antwort zaehlt die gelesenen Detailseiten.</para>
    /// </summary>
    [HttpPost("chess-arbiter")]
    public async Task<IActionResult> ChessArbiter([FromServices] ChessArbiterDirectorySweepService chessArbiter,
        [FromQuery] int? details = null,
        CancellationToken ct = default)
    {
        var result = await chessArbiter.RunAsync(
            details is { } limit ? Math.Clamp(limit, 0, 1000) : null, ct);
        return Ok(new
        {
            result.Read, result.Added, result.Matched, result.Retired, Details = result.Updated,
        });
    }

    /// <summary>
    /// Den Terminkalender des tschechischen Verbands (chess.cz) lesen.
    ///
    /// <para>Ein Abruf. Sein Wert liegt weniger im Volumen als in den LIGARUNDEN: aus elf
    /// Kalenderzeilen „2. ligy – N. kolo" wird EIN Eintrag mit elf Spielterminen — Termine, fuer
    /// die sonst je Turnier eine eigene chess-results-Seite geholt wird. Ergaenzt werden nur
    /// FEHLENDE Runden; was von chess-results kommt, bleibt stehen.</para>
    ///
    /// <para><c>Updated</c> in der Antwort zaehlt hier die ergaenzten SPIELTERMINE.</para>
    /// </summary>
    [HttpPost("chess-cz")]
    public async Task<IActionResult> ChessCz([FromServices] ChessCzDirectorySweepService chessCz,
        CancellationToken ct = default)
    {
        var result = await chessCz.RunAsync(ct);
        return Ok(new
        {
            result.Read, result.Added, result.Matched, result.Retired, RoundDates = result.Updated,
        });
    }

    /// <summary>
    /// Den Kalender des ungarischen Verbands (chess.hu) lesen.
    ///
    /// <para>Ein Abruf fuer den ganzen Kalender; sein Ertrag ist vor allem VORLAUF (ab November
    /// 2026: 41 Turniere dort gegen 5 auf chess-results). Der Abruf dauert lange — die Quelle
    /// braucht fuer ihre 31 kB rund 75 Sekunden.</para>
    /// </summary>
    [HttpPost("chess-hu")]
    public async Task<IActionResult> ChessHu([FromServices] ChessHuDirectorySweepService chessHu,
        CancellationToken ct = default)
    {
        var result = await chessHu.RunAsync(ct);
        return Ok(new { result.Read, result.Added, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Kalender des slowakischen Verbands (chess.sk) lesen — die reichhaltigste Zusatzquelle.
    ///
    /// <para>Sie liefert als einzige alles auf einmal: Anschrift MIT Postleitzahl, Bedenkzeit,
    /// Rundenzahl, Turniersystem und die Bedenkzeit-Klasse als ausdrueckliche Angabe. Und sie
    /// nennt bei einem Drittel der Eintraege die chess-results-NUMMER selbst — die Zuordnung zum
    /// bestehenden Bestand ist dort exakt statt ueber Namen geraten.</para>
    ///
    /// <para><paramref name="details"/> steuert den teuren Teil: mit <c>false</c> bleibt es bei
    /// dem EINEN Abruf der Schnittstelle (Name, Termin, Ort), ohne Anschrift und Bedenkzeit.</para>
    /// </summary>
    [HttpPost("chess-sk")]
    public async Task<IActionResult> ChessSk([FromServices] ChessSkDirectorySweepService chessSk,
        [FromQuery] bool details = true,
        CancellationToken ct = default)
    {
        var result = await chessSk.RunAsync(details, ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den Kalender des italienischen Verbands (FSI) lesen — die wichtigste Zusatzquelle.
    ///
    /// <para>Italien faehrt sein Turnierwesen auf Vega/vesus, nicht auf chess-results: von 285
    /// Eintraegen des FSI-Kalenders verlinkt KEIN EINZIGER dorthin, und eine Namensstichprobe von
    /// 15 fand nur 3. Rund vier Fuenftel der italienischen Turniere fehlen dort, dauerhaft.</para>
    ///
    /// <para>Ein Abruf fuer alles — Name, Termin, Region, Provinz, Ort, Bedenkzeit und Rundenzahl
    /// stehen inline. Kennt chess-results ein Turnier schon, fuellt die FSI nur Luecken; sie
    /// ueberschreibt keinen vorhandenen Wert.</para>
    /// </summary>
    [HttpPost("fsi")]
    public async Task<IActionResult> Fsi([FromServices] FsiDirectorySweepService fsi,
        [FromQuery] int months = 18, CancellationToken ct = default)
    {
        var result = await fsi.RunAsync(Math.Clamp(months, 1, 36), ct);
        return Ok(new { result.Read, result.Added, result.Updated, result.Matched, result.Retired });
    }

    /// <summary>
    /// Den ANKUENDIGUNGS-Kalender von chess-results lesen — den zweiten Datenbestand derselben
    /// Seite. Ein Abruf fuer alle 16 Foederationen, die ihn benutzen.
    ///
    /// <para>Die Turniersuche, aus der das Verzeichnis lebt, fuellt sich erst beim
    /// Swiss-Manager-Upload — typisch Tage bis Wochen vorher. Der Kalender wird VORAB gepflegt:
    /// fuer AUT am 2026-09-07 gemessen 143 kuenftige Eintraege, <b>93 davon fehlten in der
    /// Suche</b>.</para>
    ///
    /// <para><c>fed</c> leer oder <c>-</c> nimmt alle auf einmal.</para>
    /// </summary>
    [HttpPost("calendar")]
    public async Task<IActionResult> Calendar([FromServices] TournamentCalendarSweepService calendar,
        [FromQuery] string? fed = null, CancellationToken ct = default)
    {
        var federation = string.IsNullOrWhiteSpace(fed) ? "-" : fed.Trim().ToUpperInvariant();
        var result = await calendar.RunAsync(federation, ct);
        return Ok(new { result.Read, result.Added, result.Matched, result.Merged });
    }

    /// <summary>
    /// Die DETAILangaben der FIDE-Eintraege nachtragen — Bedenkzeit, Turniersystem, Runden- und
    /// Teilnehmerzahl und die Anschrift des Spielorts. Ein Abruf je Ereignis, deshalb gedeckelt.
    ///
    /// <para>Die FIDE-Jahresansicht, aus der der Sweep liest, traegt je Ereignis nur Name, Termin
    /// und Ort. Am Dev-Stand hiess das: von 144 FIDE-eigenen Eintraegen hatte KEIN EINZIGER eine
    /// Bedenkzeit oder eine Rundenzahl. Der groesste Gewinn ist dabei die Anschrift — sie traegt
    /// oft eine Postleitzahl, und das ist der genaueste Weg des Geocoders.</para>
    ///
    /// <para><c>retryEmpty=true</c> nimmt auch Eintraege vor, die als geprueft gelten und dennoch
    /// keine Bedenkzeit tragen; bewusst opt-in, weil ein Ereignis ohne gepflegte Angaben der
    /// haeufige Fall ist und jeder erneute Versuch wieder einen Abruf kostet.</para>
    /// </summary>
    [HttpPost("fide-details")]
    public async Task<IActionResult> FideDetails([FromServices] FideEventDetailService fideDetails,
        [FromQuery] int limit = 100,
        [FromQuery] bool retryEmpty = false, CancellationToken ct = default)
    {
        var result = await fideDetails.RunAsync(Math.Clamp(limit, 1, 1000), retryEmpty, ct);
        return Ok(new { result.Checked, result.WithDetails, result.Geocoded });
    }

    /// <summary>
    /// Publikum und Format des GANZEN Bestands aus den Turniernamen neu ableiten (Jugendklasse,
    /// Geschlechtsklasse, Liga).
    ///
    /// <para>Braucht kein Netz — die drei Merkmale stehen im Namen, der schon in der Datenbank
    /// liegt. Genau deshalb gibt es diesen Knopf: nach einem Deploy waeren die 4000 bestehenden
    /// Eintraege sonst bis zum naechsten naechtlichen Sweep unklassifiziert, und der Filter
    /// „nur Erwachsene" liesse eine halb leere Liste zurueck. Und wenn eine Wortliste im
    /// <see cref="TournamentClassifier"/> nachgeruestet wird (etwa „Schachrallye" = Nachwuchs),
    /// ist das der Weg, sie auf den Bestand anzuwenden.</para>
    ///
    /// <para>Die Turnier<b>art</b> (Einzel/Mannschaft) bleibt unangetastet: die kommt aus einer
    /// zweiten chess-results-Abfrage, nicht aus dem Namen — sie fuellt der naechste Sweep.</para>
    /// </summary>
    [HttpPost("classify")]
    public async Task<IActionResult> Classify(CancellationToken ct = default)
    {
        var result = await _maintenance.ClassifyAsync(ct);
        return Ok(new
        {
            examined = result.Examined,
            changed = result.Changed,
            youth = result.Youth,
            female = result.Female,
            male = result.Male,
            leagues = result.Leagues,
            teams = result.Teams,
        });
    }

    /// <summary>
    /// Alle noch nicht verorteten Eintraege erneut durch den Gazetteer schicken - nach einem
    /// frischen Import der eigentliche Nutzen: die Zeilen von gestern bekommen ihre Pins.
    ///
    /// <para><b>Nimmt ohne <paramref name="limit"/> den GANZEN Bestand vor</b>, und das ist Absicht:
    /// der Lauf braucht kein Netz (lokales Ortslexikon, 6598 Eintraege in 14 s), also gibt es
    /// nichts zu portionieren. Ein Deckel liefert KEINE zweite Portion — die Auswahl hat keine
    /// Fortschrittsmarke, ein zweiter Aufruf sieht wieder dieselben ersten N.</para>
    /// </summary>
    [HttpPost("geocode-missing")]
    public async Task<IActionResult> GeocodeMissing(
        [FromQuery] int limit = 0, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        var result = await _maintenance.GeocodeMissingAsync(limit, force, ct);
        return Ok(new { examined = result.Examined, resolved = result.Resolved, cleared = result.Cleared });
    }

    public class SweepRequest
    {
        public List<string>? Federations { get; set; }
    }

    public class CoordinateInput
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? PlaceName { get; set; }
    }
}
