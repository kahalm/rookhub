using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Holt die SPIELTERMINE langlaufender Turniere.
///
/// <para><b>Das Problem.</b> Ein Verzeichniseintrag traegt Start und Ende, und der Kalender
/// zeichnet ein mehrtaegiges Turnier an jedem Tag dazwischen. Bei einem Wochenend-Open ist das
/// richtig. Bei einer Liga ist es falsch: „26.09.2026 bis 17.04.2027" sind elf Runden mit zwei
/// bis fuenf Wochen Abstand — die Liga stand damit an rund 200 Tagen im Kalender, an denen nichts
/// gespielt wird, und verdeckte die Turniere, die wirklich stattfinden. Am Dev-Stand gemessen:
/// 610 offene Eintraege laufen laenger als acht Tage, 527 davon ueber 40 Tage.</para>
///
/// <para><b>Die Auswahl haengt an der DAUER, nicht am Liga-Merkmal.</b> Ob ein Turnier eine Liga
/// ist, wird aus Name und Turnierart geraten; ob sein Zeitraum luegt, steht dagegen fest — ein
/// Turnier ueber mehr als acht Tage mit mehr als einer Runde hat Luecken, egal wie es heisst.
/// Eine monatelange Vereinsmeisterschaft ist keine Liga und braucht die Termine genauso.</para>
///
/// <para>Ein Durchgang kostet EINEN Seitenabruf je Turnier (chess-results art=14, ~17 kB) und ist
/// deshalb gedeckelt. <see cref="TournamentDirectoryEntry.RoundPlanCheckedAt"/> verhindert, dass
/// dieselben Seiten jede Nacht erneut geholt werden — auch die ohne Plan, denn genau das ist der
/// haeufige Fall und muss sich merken lassen.</para>
/// </summary>
public class TournamentRoundPlanService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TournamentRoundPlanService> _log;

    public TournamentRoundPlanService(
        AppDbContext db, IHttpClientFactory httpClientFactory,
        ILogger<TournamentRoundPlanService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    /// <summary>
    /// Ab welcher Dauer der Zeitraum als aussagelos gilt. Ein Turnier ueber eine Woche spielt an
    /// aufeinanderfolgenden Tagen; darueber beginnen die Luecken. Acht Tage lassen ein
    /// zehntaegiges Festival („jeden Tag eine Runde") in Ruhe und greifen ab dem, was eine Saison
    /// ist.
    /// </summary>
    internal int MinSpanDays { get; set; } = 8;

    /// <summary>Ergebnis eines Durchgangs — fuer Log und Admin-Antwort.</summary>
    public sealed record RoundPlanResult(int Checked, int WithPlan, int Failed);

    /// <summary>
    /// Nimmt die naechsten langlaufenden Eintraege ohne Rundenplan vor. Sortiert nach dem
    /// Startdatum, damit die naechstliegenden Turniere zuerst kommen — ein Plan fuer ein Turnier
    /// in achtzehn Monaten sieht niemand.
    /// </summary>
    public async Task<RoundPlanResult> RunAsync(int limit, CancellationToken ct = default)
    {
        if (limit <= 0) return new RoundPlanResult(0, 0, 0);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var candidates = await _db.TournamentDirectoryEntries
            .Include(e => e.RoundDates)
            .Where(e => e.RoundPlanCheckedAt == null
                        && e.RemovedAt == null
                        // Der Rundenplan steht auf der chess-results-Turnierseite — ohne Nummer
                        // dort ist er nicht erreichbar (FIDE-Eintraege).
                        && e.ChessResultsId != null
                        && e.Rounds > 1
                        && e.StartDate != null && e.EndDate != null
                        && e.EndDate >= today
                        // Kein DATEDIFF: der Provider uebersetzt die Differenz zweier DateOnly
                        // nicht. Der Vergleich gegen ein VERSCHOBENES Startdatum tut dasselbe und
                        // laeuft in SQL.
                        && e.EndDate > e.StartDate!.Value.AddDays(MinSpanDays))
            .OrderBy(e => e.StartDate)
            .Take(limit)
            .ToListAsync(ct);

        var withPlan = 0;
        var failed = 0;

        foreach (var entry in candidates)
        {
            List<CrawlerRoundDate> rounds;
            try
            {
                rounds = await FetchRoundPlanAsync(entry.ChessResultsId!, ct);
            }
            // Ein HttpClient-TIMEOUT kommt als TaskCanceledException, also als
            // OperationCanceledException, obwohl der Aufrufer nichts abgebrochen hat.
            // Durchgereicht wird nur, was der AUFRUFER abgebrochen hat.
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Der Vermerk bleibt LEER: ein Netzfehler ist keine Auskunft ueber das Turnier,
                // und beim naechsten Durchgang soll es wieder vorkommen.
                failed++;
                _log.LogWarning(ex, "Rundenplan {Id} konnte nicht geholt werden", entry.ChessResultsId);
                continue;
            }

            entry.RoundPlanCheckedAt = DateTime.UtcNow;
            if (rounds.Count == 0) continue;

            Replace(entry, rounds);
            withPlan++;
        }

        if (candidates.Count > 0) await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Rundenplan-Durchgang: {Checked} geprueft, {WithPlan} mit Plan, {Failed} fehlgeschlagen",
            candidates.Count, withPlan, failed);

        return new RoundPlanResult(candidates.Count, withPlan, failed);
    }

    /// <summary>
    /// Ersetzt die Spieltermine eines Eintrags. Ersetzen und nicht ergaenzen: ein verschobener
    /// Termin waere sonst zweimal im Kalender, einmal alt und einmal neu.
    ///
    /// <para>Termine AUSSERHALB des gemeldeten Zeitraums werden verworfen. chess-results fuehrt in
    /// manchen Ligen die Runden aller Gruppen in einer Tabelle; ein Plan, der ueber das Enddatum
    /// hinausreicht, gehoert dann nicht zu diesem Eintrag, und ein Turnier soll nicht an Tagen
    /// erscheinen, an denen es laut eigener Angabe schon vorbei ist.</para>
    /// </summary>
    private void Replace(TournamentDirectoryEntry entry, List<CrawlerRoundDate> rounds)
    {
        var from = entry.StartDate;
        var to = entry.EndDate;

        var kept = rounds
            .Where(r => (from is null || r.Date >= from) && (to is null || r.Date <= to))
            .GroupBy(r => r.Number)
            .Select(g => g.First())
            .OrderBy(r => r.Number)
            .ToList();
        if (kept.Count == 0) return;

        _db.TournamentDirectoryRounds.RemoveRange(entry.RoundDates);
        entry.RoundDates = kept
            .Select(r => new TournamentDirectoryRound
            {
                TournamentDirectoryEntryId = entry.Id,
                Number = r.Number,
                Date = r.Date,
                TimeText = r.Time is { Length: > 40 } long_ ? long_[..40] : r.Time,
            })
            .ToList();
    }

    private async Task<List<CrawlerRoundDate>> FetchRoundPlanAsync(string chessResultsId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);

        using var response = await client.GetAsync(
            $"/api/tournament-search/rounds?id={Uri.EscapeDataString(chessResultsId)}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<CrawlerRoundDate>>(json, JsonOptions) ?? [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Die Antwort des Crawlers — Datum als ISO-Text, hier geparst.</summary>
    internal sealed record CrawlerRoundDate(int Round, DateOnly Date, string? Time)
    {
        public int Number => Round;
    }
}
