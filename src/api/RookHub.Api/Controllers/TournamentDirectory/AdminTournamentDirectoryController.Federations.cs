using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Controllers.TournamentDirectory;

/// <summary>
/// Die Ausloeser der VERBANDSKALENDER (SZS, ICU, FFE, NOR, Schottland, FRSah, WCU, CFC, KNSB, ECF,
/// Schachbund, chessarbiter, chess.cz, chess.hu, chess.sk, FSI): je Quelle ein Endpunkt, der den Lauf sofort ausfuehrt.
/// FIDE, Rundenplaene, Ankuendigungskalender und FIDE-Details stehen in der Hauptdatei.
///
/// <para>Teil von <see cref="AdminTournamentDirectoryController"/> (Partial, zweite Zerlegungsrunde W7b): Route-Praefix,
/// <c>[Authorize]</c> und die Berechtigung <c>TournamentsManage</c> stehen an der Hauptdatei und gelten fuer jeden
/// Endpunkt hier unveraendert. Jeder Ausloeser holt seinen Dienst per <c>[FromServices]</c>; die Antwortformen
/// unterscheiden sich je Quelle bewusst (z. B. <c>Located</c>, <c>Details</c>, <c>RoundDates</c>) und bleiben je
/// Endpunkt ausgeschrieben.</para>
/// </summary>
public partial class AdminTournamentDirectoryController
{
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
}
