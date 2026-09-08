using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Lesezugriff aufs Turnierverzeichnis: Liste, Karte, Kalender, Detail. Gefuellt wird es vom
/// naechtlichen Sweep (<see cref="TournamentDirectoryService"/>); hier wird nichts gecrawlt.
/// </summary>
[ApiController]
[Route("api/tournament-directory")]
[Authorize]
public class TournamentDirectoryController : BaseApiController
{
    private const int MaxWindowDays = 3 * 366;

    private static readonly Regex FederationPattern = new(@"^[A-Za-z]{3}$", RegexOptions.Compiled);

    /// <summary>Die chess-results-Turniernummer ist rein numerisch.</summary>
    private static readonly Regex TournamentIdPattern = new(@"^\d{1,10}$", RegexOptions.Compiled);

    /// <summary>
    /// Die IDENTITAET eines Verzeichniseintrags: eine chess-results-Nummer oder ein
    /// Quellen-Kuerzel plus Nummer (<c>f14805</c> fuer den FIDE-Kalender). Bewusst eng — der Wert
    /// steht in Adressen und wird als Schluessel verglichen; hoechstens zehn Ziffern, wie die
    /// chess-results-Nummer sie hat.
    /// </summary>
    private static readonly Regex PublicIdPattern = new(@"^[a-z]?\d{1,10}$", RegexOptions.Compiled);

    /// <summary>Obergrenze der Turniere EINES Monats — jenseits davon meldet die Antwort `truncated`.</summary>
    private const int CalendarMaxTournaments = 1500;

    private readonly TournamentDirectoryQueryService _query;
    private readonly AppDbContext _db;
    private readonly AdminMessageService _messages;

    public TournamentDirectoryController(
        TournamentDirectoryQueryService query, AppDbContext db, AdminMessageService messages)
    {
        _query = query;
        _db = db;
        _messages = messages;
    }

    /// <summary>
    /// Turnierliste mit Zeitraum-, Umkreis- und Textfilter. Ohne <c>profileId</c> gelten die
    /// einzelnen Query-Parameter; mit <c>profileId</c> werden Mittelpunkt, Radius und Filter aus dem
    /// gespeicherten Suchprofil des Nutzers uebernommen (explizite Parameter stechen sie nicht aus -
    /// ein Profil soll reproduzierbar dasselbe liefern wie die naechtliche Benachrichtigung).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DirectoryPageDto>> Search(
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] double? lat = null, [FromQuery] double? lon = null, [FromQuery] int? radiusKm = null,
        [FromQuery] string? fed = null, [FromQuery] string? speed = null, [FromQuery] string? q = null,
        [FromQuery] bool weekendOnly = false, [FromQuery] int? minPlayers = null,
        [FromQuery] int? profileId = null,
        [FromQuery] DirectoryAudienceQuery? audience = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var parsed = await BuildQueryAsync(from, to, lat, lon, radiusKm, fed, speed, q,
            weekendOnly, minPlayers, profileId, audience, page, pageSize, ct);
        if (parsed.Error is not null) return BadRequest(new { message = parsed.Error });

        var result = await _query.SearchAsync(parsed.Query!, ct);
        var subscribed = await SubscribedIdsAsync(
            result.Items.SelectMany(i => i.Members).Select(m => m.ChessResultsId), ct);
        var ignored = await IgnoredIdsAsync(
            result.Items.Select(i => i.Entry.PublicId), parsed.Query!.IncludeIgnored, ct);

        return Ok(new DirectoryPageDto
        {
            Items = result.Items
                .Select(i => DirectoryEntryDto.FromEntity(i.Entry, i.DistanceKm,
                    i.Members.Any(m => m.ChessResultsId is not null && subscribed.Contains(m.ChessResultsId)), i.Members,
                    ignored.Contains(i.Entry.PublicId)))
                .ToList(),
            Total = result.Total,
            Truncated = result.Truncated,
        });
    }

    /// <summary>
    /// Kartenmarker fuer den sichtbaren Ausschnitt. <c>bbox</c> ist "minLat,minLon,maxLat,maxLon".
    /// </summary>
    [HttpGet("map")]
    public async Task<ActionResult<List<DirectoryEntryDto>>> Map(
        [FromQuery] string bbox,
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] string? fed = null, [FromQuery] string? speed = null, [FromQuery] string? q = null,
        [FromQuery] bool weekendOnly = false, [FromQuery] int? minPlayers = null,
        [FromQuery] int? profileId = null, [FromQuery] DirectoryAudienceQuery? audience = null,
        [FromQuery] int limit = 2000,
        CancellationToken ct = default)
    {
        if (!TryParseBoundingBox(bbox, out var box))
            return BadRequest(new { message = "bbox must be minLat,minLon,maxLat,maxLon." });

        var parsed = await BuildQueryAsync(from, to, null, null, null, fed, speed, q,
            weekendOnly, minPlayers, profileId, audience, 1, 1, ct);
        if (parsed.Error is not null) return BadRequest(new { message = parsed.Error });

        var pins = await _query.MapPinsAsync(parsed.Query!, box.MinLat, box.MaxLat, box.MinLon, box.MaxLon, limit, ct);
        // Ueber ALLE Gruppen eines Turniers: gemerkt ist es, wenn eine davon abonniert ist — das
        // Abo haengt an der chess-results-Nummer der einzelnen Gruppe, der Punkt am Turnier.
        var pinSubscribed = await SubscribedIdsAsync(
            pins.SelectMany(p => p.Members).Select(m => m.ChessResultsId), ct);
        var pinIgnored = await IgnoredIdsAsync(
            pins.Select(p => p.Entry.PublicId), parsed.Query!.IncludeIgnored, ct);

        // Die Karte braucht Haken und Ausblend-Merkmal jetzt ebenfalls: ihr Punkt-Fenster ist
        // dieselbe Karte wie in Liste und Kalender und zeigt dieselben Schaltflaechen.
        return Ok(pins
            .Select(p => DirectoryEntryDto.FromEntity(p.Entry, null,
                p.Members.Any(m => m.ChessResultsId is not null && pinSubscribed.Contains(m.ChessResultsId)),
                p.Members, pinIgnored.Contains(p.Entry.PublicId)))
            .ToList());
    }

    /// <summary>
    /// Kalendermonat: je Tag die an diesem Tag LAUFENDEN Turniere. Ein mehrtaegiges Open steht
    /// deshalb an jedem seiner Tage - genau so, wie ein Kalender es zeigen soll.
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<DirectoryCalendarDto>> Calendar(
        [FromQuery] int year, [FromQuery] int month,
        [FromQuery] double? lat = null, [FromQuery] double? lon = null, [FromQuery] int? radiusKm = null,
        [FromQuery] string? fed = null, [FromQuery] string? speed = null, [FromQuery] string? q = null,
        [FromQuery] bool weekendOnly = false, [FromQuery] int? minPlayers = null,
        [FromQuery] int? profileId = null, [FromQuery] DirectoryAudienceQuery? audience = null,
        CancellationToken ct = default)
    {
        if (year is < 1990 or > 2100 || month is < 1 or > 12)
            return BadRequest(new { message = "year/month out of range." });

        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var parsed = await BuildQueryAsync(first.ToString("yyyy-MM-dd"), last.ToString("yyyy-MM-dd"),
            lat, lon, radiusKm, fed, speed, q, weekendOnly, minPlayers, profileId, audience, 1, 200, ct);
        if (parsed.Error is not null) return BadRequest(new { message = parsed.Error });

        // Ein Monat OHNE Umkreis umfasst alle Foederationen — auf dem Dev-Server sind das
        // dreistellig viele Turniere, und der Kalender ist die Startseite der Turnierseite, wird
        // also regelmaessig ungefiltert aufgerufen. Der frueher hier stehende Deckel von 200 war
        // deshalb keine grosszuegige Reserve, sondern der Grund, warum genau 200 gezaehlt wurden.
        // Was jenseits der Grenze liegt, wird gemeldet statt verschwiegen (`Truncated`).
        var result = await _query.SearchAsync(parsed.Query! with { Page = 1, PageSize = CalendarMaxTournaments }, ct);
        var subscribed = await SubscribedIdsAsync(
            result.Items.SelectMany(i => i.Members).Select(m => m.ChessResultsId), ct);

        // Die Turniere EINMAL, die Tage nur mit ihren Nummern — ein mehrtaegiges Turnier stand
        // vorher an jedem seiner Tage voll ausgeschrieben da (siehe DirectoryCalendarDto).
        var calendarIgnored = await IgnoredIdsAsync(
            result.Items.Select(i => i.Entry.PublicId), parsed.Query!.IncludeIgnored, ct);

        var tournaments = result.Items
            .Select(i => DirectoryEntryDto.FromEntity(i.Entry, i.DistanceKm,
                i.Members.Any(m => m.ChessResultsId is not null && subscribed.Contains(m.ChessResultsId)), i.Members,
                calendarIgnored.Contains(i.Entry.PublicId)))
            .ToList();

        var days = new List<DirectoryCalendarDayDto>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            days.Add(new DirectoryCalendarDayDto
            {
                Date = day,
                Ids = result.Items.Where(i => Covers(i.Entry, day))
                    .Select(i => i.Entry.PublicId).ToList(),
            });
        }
        return Ok(new DirectoryCalendarDto
        {
            Tournaments = tournaments,
            Days = days,
            Truncated = result.Truncated || result.Total > tournaments.Count,
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DirectoryEntryDto>> Get(string id, CancellationToken ct)
    {
        if (!PublicIdPattern.IsMatch(id ?? ""))
            return BadRequest(new { message = "Invalid tournament id." });

        var item = await _query.GetAsync(id, ct);
        if (item is null) return NotFound();

        var subscribed = await SubscribedIdsAsync(item.Members.Select(m => m.ChessResultsId), ct);
        var isIgnored = await IgnoredIdsAsync([item.Entry.PublicId], true, ct);
        return Ok(DirectoryEntryDto.FromEntity(item.Entry, null, subscribed.Count > 0, item.Members,
            isIgnored.Contains(item.Entry.PublicId)));
    }

    /// <summary>Ortsvorschlaege (PLZ oder Name) fuer das Suchprofil-Formular.</summary>
    [HttpGet("places")]
    public async Task<ActionResult<List<GeoPlaceSuggestionDto>>> Places(
        [FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new List<GeoPlaceSuggestionDto>());

        var places = await _query.SuggestPlacesAsync(q, 10, ct);
        return Ok(places.Select(p => new GeoPlaceSuggestionDto
        {
            Label = p.PostalCode is null ? $"{p.Name} ({p.Country})" : $"{p.PostalCode} {p.Name} ({p.Country})",
            Country = p.Country,
            PostalCode = p.PostalCode,
            Lat = p.Lat,
            Lon = p.Lon,
        }).ToList());
    }

    /// <summary>
    /// Ein Turnier fuer MICH ausblenden. Es verschwindet aus Liste, Karte und Kalender — und aus
    /// der naechtlichen Umkreis-Meldung, denn eine Meldung ueber ein weggeklicktes Turnier ist
    /// genau die Art Benachrichtigung, die einen dazu bringt, alle abzuschalten.
    ///
    /// <para>Idempotent: zweimal ausblenden ist dasselbe wie einmal.</para>
    /// </summary>
    [HttpPost("{id}/ignore")]
    public async Task<IActionResult> Ignore(string id, CancellationToken ct)
    {
        id = (id ?? "").Trim();
        if (!PublicIdPattern.IsMatch(id)) return BadRequest(new { message = "Invalid tournament ID." });

        var userId = GetUserId();
        if (await _db.TournamentDirectoryIgnores
                .AnyAsync(i => i.UserId == userId && i.PublicId == id, ct))
        {
            return NoContent();
        }

        _db.TournamentDirectoryIgnores.Add(new TournamentDirectoryIgnore
        {
            UserId = userId,
            PublicId = id,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        // Zwei gleichzeitige Klicks laufen in den Unique-Index. Das Ergebnis ist genau das
        // gewuenschte, also kein Fehler.
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            return NoContent();
        }
        return NoContent();
    }

    /// <summary>Ein ausgeblendetes Turnier wieder zeigen (idempotent).</summary>
    [HttpDelete("{id}/ignore")]
    public async Task<IActionResult> Unignore(string id, CancellationToken ct)
    {
        id = (id ?? "").Trim();
        if (!PublicIdPattern.IsMatch(id)) return BadRequest(new { message = "Invalid tournament ID." });

        var userId = GetUserId();
        var row = await _db.TournamentDirectoryIgnores
            .FirstOrDefaultAsync(i => i.UserId == userId && i.PublicId == id, ct);
        if (row is not null)
        {
            _db.TournamentDirectoryIgnores.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>
    /// „Dieses Turnier ist falsch eingeordnet" — Rueckmeldung eines Nutzers zu einem Eintrag.
    ///
    /// <para>Landet ABSICHTLICH im bestehenden Admin-Nachrichtenkanal und nicht in einer eigenen
    /// Tabelle: dort gibt es schon eine Oberflaeche (Admin-Tab „Nachrichten"), eine Glocke bei
    /// allen Admins und — das Entscheidende — einen Rueckweg. Eine Meldung „der Ort ist falsch"
    /// braucht haeufig eine Rueckfrage, und eine Tabelle ohne Antwortmoeglichkeit haette die
    /// nicht. Die vorgeschlagenen Korrekturen stehen als lesbarer Text mit drin; angewandt werden
    /// sie ueber die Admin-Endpunkte des Verzeichnisses, mit Augenmass.</para>
    ///
    /// <para>Alle Felder sind freiwillig — auch der Text. Ein Knopfdruck ohne ein Wort ist eine
    /// gueltige Meldung („hier stimmt was nicht"), und die Huerde soll niedrig sein.</para>
    /// </summary>
    [HttpPost("{id}/report")]
    public async Task<IActionResult> Report(
        string id, [FromBody] DirectoryReportDto dto, CancellationToken ct)
    {
        if (!PublicIdPattern.IsMatch((id ?? "").Trim()))
            return BadRequest(new { message = "Invalid tournament ID." });

        var entry = await _db.TournamentDirectoryEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.PublicId == id, ct);
        if (entry is null) return NotFound();

        await _messages.SendFromUserAsync(GetUserId(), BuildReportBody(entry, dto));
        return NoContent();
    }

    /// <summary>
    /// „Mein Turnier fehlt" — ein Hinweis auf eine QUELLE, die dieses Verzeichnis noch nicht kennt.
    ///
    /// <para>Das Verzeichnis speist sich aus der chess-results-Turniersuche. Wer dort nicht
    /// ausschreibt, kommt hier nicht vor — und genau diese Turniere sind die Luecke, die niemand
    /// von innen sehen kann. Wichtig ist deshalb nicht der Turniername, sondern der LINK: eine
    /// Verbandsseite oder ein Vereinskalender laesst sich zusaetzlich crawlen, eine Aufzaehlung
    /// einzelner Termine nicht. Der Link ist Pflicht, der Text nicht.</para>
    ///
    /// <para>Geht denselben Weg wie die Falschmeldung: Admin-Nachrichtenkanal, damit
    /// Rueckfragen moeglich sind.</para>
    /// </summary>
    [HttpPost("suggest-source")]
    public async Task<IActionResult> SuggestSource([FromBody] DirectorySourceSuggestionDto dto)
    {
        var link = (dto.Link ?? "").Trim();
        if (link.Length == 0)
            return BadRequest(new { message = "A link to the source is required." });

        // Nur http/https: ein „javascript:"- oder „data:"-Link waere hier nichts als eine Falle
        // fuer den Admin, der die Meldung spaeter anklickt.
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { message = "The link must be an absolute http(s) URL." });
        }

        var lines = new List<string>
        {
            "Hinweis auf eine fehlende Turnierquelle",
            uri.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(dto.Message))
        {
            lines.Add("");
            lines.Add(dto.Message.Trim());
        }

        var body = string.Join("\n", lines);
        await _messages.SendFromUserAsync(GetUserId(), body.Length > 4000 ? body[..4000] : body);
        return NoContent();
    }

    /// <summary>
    /// Die Meldung als lesbare Nachricht. Bewusst mit dem IST-Stand daneben: ohne ihn muesste der
    /// Admin fuer jede Meldung erst nachsehen, was das Verzeichnis ueberhaupt behauptet.
    /// </summary>
    internal static string BuildReportBody(TournamentDirectoryEntry entry, DirectoryReportDto dto)
    {
        var lines = new List<string>
        {
            $"Meldung zum Turnierverzeichnis: {entry.Name} ({entry.PublicId})",
            entry.ChessResultsId is null
                ? "(nicht auf chess-results ausgeschrieben)"
                : $"https://chess-results.com/tnr{entry.ChessResultsId}.aspx?lan=1",
            "",
            $"Ist-Stand: Ort „{entry.LocationText}\" ({entry.GeoPlaceName ?? "nicht verortet"}, {entry.GeoSource}), " +
            $"Art {entry.Kind}, {(entry.IsLeague ? "Liga" : "kein Liga-Wettbewerb")}, " +
            $"Klassen {(entry.AgeGroups == TournamentAgeGroups.None ? "keine" : entry.AgeGroups.ToString())}, " +
            $"Geschlecht {entry.Gender}, Bedenkzeit {entry.Speed}",
        };

        // Die beiden Lern-Antworten getrennt und benannt: die eine kann in die Wortliste des
        // Klassifizierers wandern, die andere in die Quellenliste. Im Freitext untergegangen
        // waeren sie beim Durchsehen von 50 Meldungen nicht wiederzufinden.
        if (!string.IsNullOrWhiteSpace(dto.NamePattern))
        {
            lines.Add("");
            lines.Add($"Solche Turniere heissen dort: {dto.NamePattern.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(dto.SourceLink))
        {
            lines.Add("");
            lines.Add($"Besser ersichtlich auf: {dto.SourceLink.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(dto.Message))
        {
            lines.Add("");
            lines.Add(dto.Message.Trim());
        }

        // AdminMessage.Body ist auf 4000 Zeichen begrenzt; der Freitext ist im DTO schon
        // gedeckelt, der Rest kann durch einen langen Turniernamen theoretisch anlaufen.
        var body = string.Join("\n", lines);
        return body.Length > 4000 ? body[..4000] : body;
    }

    /// <summary>
    /// Der naechstgelegene Ort zu Koordinaten — fuer das Ortsfeld, wenn der Browser den Standort
    /// liefert. Aufgeloest gegen den lokalen Gazetteer; die Koordinaten des Nutzers verlassen den
    /// Server nicht. 204, wenn im Umkreis von 200 km kein Ort im Lexikon liegt (dann bleibt das
    /// Feld leer, aber die Koordinaten gelten trotzdem).
    /// </summary>
    [HttpGet("places/nearest")]
    public async Task<ActionResult<GeoPlaceSuggestionDto>> NearestPlace(
        [FromQuery] double lat, [FromQuery] double lon, CancellationToken ct)
    {
        if (lat is < -90 or > 90) return BadRequest(new { message = "lat out of range." });
        if (lon is < -180 or > 180) return BadRequest(new { message = "lon out of range." });

        var place = await _query.NearestPlaceAsync(lat, lon, ct);
        if (place is null) return NoContent();

        return Ok(new GeoPlaceSuggestionDto
        {
            Label = place.PostalCode is null
                ? $"{place.Name} ({place.Country})"
                : $"{place.PostalCode} {place.Name} ({place.Country})",
            Country = place.Country,
            PostalCode = place.PostalCode,
            Lat = place.Lat,
            Lon = place.Lon,
        });
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Ab wann ein Zeitraum OHNE hinterlegte Spieltermine nicht mehr als durchgehend gespielt
    /// gilt. Drei Wochen sind bewusst grosszuegig: ein Open ueber zwei Wochen gibt es wirklich,
    /// ein Turnier, an dem einundvierzig Tage am Stueck gespielt wird, nicht.
    /// </summary>
    internal const int MaxSpreadDays = 21;

    /// <summary>
    /// Dieselbe Grenze fuer SCHNELL- und BLITZturniere — und viel enger, weil die Turnierart die
    /// Dauer schon beantwortet: ein Blitzturnier ueber sechs Wochen ist ein Widerspruch, gemeldet
    /// an tnr1474416 („II Vipiteno Chess Festival - 2° torneo rapid", 10min + 5sec, 11.08. bis
    /// 20.09.). Acht Tage, wie beim Rundenplan-Dienst (<see cref="TournamentRoundPlanService"/>).
    /// </summary>
    internal const int MaxFastSpreadDays = 8;

    /// <summary>
    /// Findet dieses Turnier an diesem Tag statt?
    ///
    /// <para>Sind SPIELTERMINE bekannt, gelten NUR sie. Das ist der Unterschied zwischen elf
    /// Spieltagen und rund 200 Tagen Kalenderrauschen: eine Liga laeuft von September bis April,
    /// gespielt wird an elf Terminen mit Wochen Abstand (siehe
    /// <see cref="TournamentDirectoryRound"/>).</para>
    ///
    /// <para><b>Ohne Termine gilt der Zeitraum nur, solange er plausibel ist.</b> Gemeldet an
    /// tnr1474416: ein RAPID-Turnier mit dem Zeitraum 11.08. bis 20.09. belegte 41 Kalendertage
    /// und verdeckte, was an diesen Tagen wirklich gespielt wird. Die Angabe stammt so von
    /// chess-results, und einen Rundenplan gibt es dort nicht (die Abfrage liefert eine leere
    /// Liste) — es ist also nicht „noch nicht geholt", sondern nicht vorhanden. Am Dev-Stand sind
    /// 715 Eintraege in dieser Lage.</para>
    ///
    /// <para><b>Warum nicht schlicht „laenger als acht Tage".</b> Das traefe auch das ehrliche
    /// mehrtaegige Open: neun Tage, eine Runde pro Tag, kein hinterlegter Plan — es verschwaende
    /// an acht von neun Tagen, und das faellt weniger auf als der heutige Fehler. Die TURNIERART
    /// trennt die Faelle: ein Schnell- oder Blitzturnier ueber mehr als eine Woche kann nicht
    /// durchgehend sein, ein Turnierschach-Open ueber zwei Wochen sehr wohl. Am Dev-Stand
    /// gemessen: 603 der 715 Eintraege werden damit auf ihren Starttag zusammengezogen, und die
    /// 112 mehrtaegigen Standard-Turniere zwischen neun und einundzwanzig Tagen bleiben
    /// unangetastet.</para>
    /// </summary>
    private static bool Covers(TournamentDirectoryEntry entry, DateOnly day)
    {
        if (entry.RoundDates.Count > 0) return entry.RoundDates.Any(r => r.Date == day);

        var start = entry.StartDate ?? entry.EndDate;
        var end = entry.EndDate ?? entry.StartDate;
        if (start is null || end is null) return false;

        // Unplausibel lang: dann steht der Eintrag nur an seinem STARTtag. Ihn ganz wegzulassen
        // waere falsch — er findet ja statt, nur eben nicht an jedem Tag dazwischen.
        return SpreadsOverItsWholeSpan(entry, start.Value, end.Value)
            ? day >= start && day <= end
            : day == start;
    }

    /// <summary>
    /// Darf dieser Eintrag ohne Spieltermine ueber seinen ganzen Zeitraum gezeigt werden?
    /// </summary>
    internal static bool SpreadsOverItsWholeSpan(
        TournamentDirectoryEntry entry, DateOnly start, DateOnly end)
    {
        var days = end.DayNumber - start.DayNumber + 1;
        var limit = entry.Speed is TournamentSpeed.Rapid or TournamentSpeed.Blitz
            ? MaxFastSpreadDays
            : MaxSpreadDays;
        return days <= limit;
    }

    /// <summary>
    /// Welche dieser Turniere hat der Nutzer ausgeblendet? Nur gebraucht, wenn der Filter
    /// ausgeblendete MITanzeigt — sonst sind sie ohnehin nicht in der Antwort, und die Abfrage
    /// waere verschwendet.
    /// </summary>
    private async Task<HashSet<string>> IgnoredIdsAsync(
        IEnumerable<string> ids, bool needed, CancellationToken ct)
    {
        if (!needed) return [];

        var list = ids.Where(id => id is not null).Select(id => id!).Distinct().ToList();
        if (list.Count == 0) return [];

        var userId = GetUserId();
        return (await _db.TournamentDirectoryIgnores
                .Where(i => i.UserId == userId && list.Contains(i.PublicId))
                .Select(i => i.PublicId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Welche dieser Turniere hat der Nutzer gemerkt? Der Abgleich laeuft ueber die
    /// chess-results-NUMMER, weil das Abo sie traegt und der Refresh-Crawl sie braucht — ein
    /// FIDE-Eintrag ohne Nummer kann deshalb nicht gemerkt werden und faellt hier heraus.
    /// </summary>
    private async Task<HashSet<string>> SubscribedIdsAsync(IEnumerable<string?> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        var userId = GetUserId();
        var found = await _db.TournamentSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && list.Contains(s.CrawlerTournamentId))
            .Select(s => s.CrawlerTournamentId)
            .ToListAsync(ct);
        return found.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<(DirectorySearchQuery? Query, string? Error)> BuildQueryAsync(
        string? from, string? to, double? lat, double? lon, int? radiusKm,
        string? fed, string? speed, string? text, bool weekendOnly, int? minPlayers,
        int? profileId, DirectoryAudienceQuery? audience, int page, int pageSize, CancellationToken ct)
    {
        DateOnly? fromDate = null, toDate = null;
        if (from is not null && !TryParseIsoDate(from, out fromDate)) return (null, "from must be yyyy-MM-dd.");
        if (to is not null && !TryParseIsoDate(to, out toDate)) return (null, "to must be yyyy-MM-dd.");
        if (fromDate is { } f && toDate is { } t)
        {
            if (t < f) return (null, "to must not be before from.");
            if (t.DayNumber - f.DayNumber > MaxWindowDays) return (null, "Date window too large.");
        }

        if (fed is not null && !FederationPattern.IsMatch(fed.Trim()))
            return (null, "fed must be a 3-letter federation code.");

        TournamentSpeed? parsedSpeed = null;
        if (!string.IsNullOrWhiteSpace(speed))
        {
            if (!Enum.TryParse<TournamentSpeed>(speed.Trim(), ignoreCase: true, out var value))
                return (null, "speed must be one of standard, rapid, blitz, unknown.");
            parsedSpeed = value;
        }

        if (radiusKm is { } r && r is < 1 or > 2000) return (null, "radiusKm must be between 1 and 2000.");
        if (lat is { } la && la is < -90 or > 90) return (null, "lat out of range.");
        if (lon is { } lo && lo is < -180 or > 180) return (null, "lon out of range.");

        if (!TryParseEnumCsv<TournamentKind>(audience?.Kinds, out var kinds))
            return (null, "kinds must be a comma-separated list of individual, team, unknown.");
        if (!TryParseEnumCsv<TournamentGender>(audience?.Genders, out var genders))
            return (null, "genders must be a comma-separated list of open, female, male.");
        if (!TryParseEnumCsv<TournamentAgeGroups>(audience?.AgeGroups, out var ageList))
            return (null, "ageGroups must be a comma-separated list of u8, u10, u12, u14, u16, u18, u20, youthUnspecified, senior.");

        var ageGroups = TournamentAgeGroups.None;
        foreach (var group in ageList) ageGroups |= group;

        List<string> profileFeds = [];
        List<TournamentSpeed> profileSpeeds = [];

        if (profileId is { } id)
        {
            var profile = await _db.TournamentSearchProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == GetUserId(), ct);
            if (profile is null) return (null, "Unknown search profile.");

            lat = profile.Lat;
            lon = profile.Lon;
            radiusKm = profile.RadiusKm;
            weekendOnly = profile.WeekendOnly;
            minPlayers = profile.MinPlayers;

            // ALLE Werte des Profils, nicht nur der eine Sonderfall. Frueher wurde ein Profil mit
            // mehreren Foederationen/Bedenkzeiten beim Filtern uebergangen — die naechtliche
            // Meldung wertet sie aber vollstaendig aus (MatchesProfile). Ein Profil soll
            // reproduzierbar dasselbe liefern wie die Meldung, sonst ist es zwei Dinge.
            if (fed is null) profileFeds = TournamentDirectoryService.SplitCsv(profile.Federations);

            if (parsedSpeed is null)
            {
                profileSpeeds = TournamentDirectoryService.SplitCsv(profile.Speeds)
                    .Select(v => Enum.TryParse<TournamentSpeed>(v, ignoreCase: true, out var s) ? s : (TournamentSpeed?)null)
                    .Where(v => v is not null)
                    .Select(v => v!.Value)
                    .ToList();
            }
        }

        return (new DirectorySearchQuery
        {
            From = fromDate,
            To = toDate,
            Lat = lat,
            Lon = lon,
            RadiusKm = radiusKm,
            Federation = fed,
            Speed = parsedSpeed,
            Federations = profileFeds.Count > 0 ? profileFeds : null,
            Speeds = profileSpeeds.Count > 0 ? profileSpeeds : null,
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            WeekendOnly = weekendOnly,
            MinPlayers = minPlayers,
            // Ausgeblendete Turniere gehoeren dem NUTZER, nicht dem Filter — deshalb wandert die
            // Kennung mit in die Abfrage und nicht eine fertige Liste von Nummern.
            ForUserId = GetUserId(),
            IncludeIgnored = audience?.IncludeIgnored ?? false,
            Kinds = kinds.Count > 0 ? kinds : null,
            Genders = genders.Count > 0 ? genders : null,
            AgeGroups = ageGroups,
            AdultsOnly = audience?.AdultsOnly ?? false,
            HideLeagues = audience?.HideLeagues ?? false,
            Page = page,
            PageSize = pageSize,
        }, null);
    }

    /// <summary>
    /// Eine kommagetrennte Liste von Enum-Namen. Ein unbekannter Name ist ein FEHLER, kein
    /// Grund zum Ueberspringen: wer „ageGroups=u13" schickt, soll das erfahren und nicht eine
    /// stillschweigend ungefilterte Liste bekommen.
    /// </summary>
    internal static bool TryParseEnumCsv<T>(string? csv, out List<T> values) where T : struct, Enum
    {
        values = [];
        if (string.IsNullOrWhiteSpace(csv)) return true;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<T>(part, ignoreCase: true, out var value)) return false;
            if (!values.Contains(value)) values.Add(value);
        }
        return true;
    }

    internal static bool TryParseBoundingBox(string? bbox,
        out (double MinLat, double MinLon, double MaxLat, double MaxLon) box)
    {
        box = default;
        var parts = (bbox ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;

        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }
        if (values[0] is < -90 or > 90 || values[2] is < -90 or > 90) return false;
        if (values[1] is < -180 or > 180 || values[3] is < -180 or > 180) return false;
        if (values[2] < values[0]) return false;

        box = (values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool TryParseIsoDate(string? text, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)) return false;
        date = parsed;
        return true;
    }
}
