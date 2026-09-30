using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services;

/// <summary>
/// Crawl-Auftraege der Hintergrunddienste an den Crawler — und die Frage, WELCHES Turnier gemeint ist.
///
/// <para><b>Eine Turnier-Kennung, zwei Bedeutungen.</b> Die Turnierseite (<c>/tournaments/:id</c>)
/// traegt die Crawler-DB-Id, Kalender und Auto-Abo die chess-results-Nummer — und Monitor, Abo und
/// Favorit speichern, was der Einstiegsweg mitbrachte. <c>POST /api/crawl</c> deutet dagegen JEDE Zahl
/// als chess-results-Nummer: ein Monitor oder Abo mit der DB-Id 57 liess den Crawler tnr57 holen, ein
/// fremdes Uralt-Turnier, und das echte Turnier wurde nie nachgeladen (Codereview 2026-09-29, A5-001).
/// Vor jedem Crawl-Auftrag wird die Kennung deshalb ueber <see cref="ResolveAsync"/> aufgeloest: der
/// Crawler liest in <c>GET /api/tournaments/{id}</c> eine Zahl zuerst als DB-Id, dann als Nummer —
/// genau so, wie die Turnierseite sie gemeint hat.</para>
/// </summary>
public sealed partial class CrawlQueueClient(CrawlerProxyService proxy)
{
    /// <summary>Ein beim Crawler bekanntes Turnier: DB-Id und chess-results-Nummer (nur Ziffern).</summary>
    public sealed record ResolvedTournament(int DbId, string ChessResultsId);

    /// <summary>
    /// Loest eine gespeicherte Turnier-Kennung (DB-Id ODER chess-results-Nummer) zum Turnier auf.
    /// <c>null</c>, wenn der Crawler das Turnier nicht kennt (404) oder keine gueltige Nummer liefert —
    /// dann ist eine rein numerische Kennung eine noch nicht geholte chess-results-Nummer (eine DB-Id
    /// kennt der Crawler immer). Andere Crawler-Fehler werden weitergereicht.
    /// </summary>
    public async Task<ResolvedTournament?> ResolveAsync(string tournamentKey, CancellationToken ct)
    {
        JsonElement detail;
        try
        {
            detail = await proxy.GetAsync($"/api/tournaments/{Uri.EscapeDataString(tournamentKey)}", ct);
        }
        catch (CrawlerRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (detail.ValueKind != JsonValueKind.Object) return null;
        var number = detail.TryGetProperty("chessResultsId", out var cr) && cr.ValueKind == JsonValueKind.String
            ? NormalizeChessResultsId(cr.GetString())
            : null;
        if (number is null) return null;
        var dbId = detail.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id) ? id : 0;
        return new ResolvedTournament(dbId, number);
    }

    /// <summary>Ausgang eines angenommenen Crawl-Auftrags.</summary>
    public enum CrawlRequestOutcome
    {
        /// <summary>202: neu eingereiht.</summary>
        Queued,
        /// <summary>409: fuer dieses Turnier ist schon ein Auftrag eingereiht oder laeuft — der holt
        /// dieselben Seiten, das Ziel ist also erreicht.</summary>
        AlreadyRunning,
    }

    /// <summary>
    /// Reiht einen Crawl-Auftrag (<c>POST /api/crawl</c>) fuer eine chess-results-Nummer ein — mit EINER
    /// 409-Semantik fuer die Hintergrunddienste: „laeuft schon" ist kein Fehler (wie
    /// <c>TournamentHistoryService.RequestCrawlAsync</c>). Vorher warf der Monitor bei 409 und verschluckte
    /// damit die „neue Runde"-Meldung, der Abo-Abgleich meldete „fehlgeschlagen" (Codereview A5-007).
    /// Jede andere Ablehnung (429 volle Warteschlange, 400) wirft weiter <see cref="CrawlerRequestException"/>.
    /// </summary>
    public async Task<CrawlRequestOutcome> RequestAsync(string chessResultsId, string jobType, CancellationToken ct)
    {
        try
        {
            await proxy.PostJsonAsync("/api/crawl", new { chessResultsId, jobType }, ct);
            return CrawlRequestOutcome.Queued;
        }
        catch (CrawlerRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return CrawlRequestOutcome.AlreadyRunning;
        }
    }

    /// <summary>
    /// chess-results-Nummer wie der Crawler sie normalisiert (<c>CrawlController</c>): „tnr"-Praefix,
    /// URL-Teile und „.aspx" weg, danach nur 1–10 Ziffern — sonst <c>null</c>.
    /// </summary>
    public static string? NormalizeChessResultsId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = TnrPrefix().Replace(raw.Trim(), "").Replace(".aspx", "").Split('?')[0].Trim();
        return Digits().IsMatch(n) ? n : null;
    }

    [GeneratedRegex(@"^(.*tnr)", RegexOptions.IgnoreCase)]
    private static partial Regex TnrPrefix();

    [GeneratedRegex(@"^\d{1,10}$")]
    private static partial Regex Digits();
}
