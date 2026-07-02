using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Liefert dem Schach-Bot den Trainings-/Puzzle-Fortschritt eines über die Discord-ID verknüpften
/// Spielers — Grundlage für den personalisierten Motivations-DM. Bündelt ausschließlich bestehende
/// Service-Logik (<see cref="TrainingGoalService"/>, <see cref="PuzzleService"/>); keine eigene
/// Aggregation, damit Bot- und Web-Ansicht denselben Fortschritt zeigen.
/// </summary>
public class BotStatsService
{
    private readonly AppDbContext _db;
    private readonly TrainingGoalService _goals;
    private readonly PuzzleStatsService _puzzles;
    private readonly WeeklyPostService _weekly;
    private readonly CrawlerProxyService _crawler;

    /// <summary>Bis so viele Tage VOR dem Termin gilt ein Turnier als "anstehend" (Motivation davor).</summary>
    internal int UpcomingWindowDays { get; set; } = 7;

    /// <summary>Bis so viele Tage NACH dem Termin gilt ein Turnier als "frisch beendet" (Ergebnis aufgreifen).</summary>
    internal int FinishedWindowDays { get; set; } = 5;

    /// <summary>Höchstens so viele Turniere in den DM aufnehmen (die zeitnächsten zuerst).</summary>
    internal int MaxTournaments { get; set; } = 5;

    public BotStatsService(AppDbContext db, TrainingGoalService goals, PuzzleStatsService puzzles,
        WeeklyPostService weekly, CrawlerProxyService crawler)
    {
        _db = db;
        _goals = goals;
        _puzzles = puzzles;
        _weekly = weekly;
        _crawler = crawler;
    }

    /// <summary>
    /// Fortschritt für die gegebene Discord-ID — oder <c>null</c>, wenn kein RookHub-Konto damit
    /// verknüpft ist (der Bot zeigt dann den Verknüpfungs-Hinweis statt einer Motivation).
    /// </summary>
    public async Task<BotPlayerProgressDto?> GetProgressByDiscordIdAsync(string discordId)
    {
        var user = await _db.AppUsers
            .Where(u => u.Profile != null && u.Profile.DiscordId == discordId)
            .Select(u => new { u.Id, u.Username, DisplayName = u.Profile!.DisplayName })
            .FirstOrDefaultAsync();
        if (user == null)
            return null;

        return new BotPlayerProgressDto
        {
            Username = user.Username,
            DisplayName = user.DisplayName,
            // vizLevel = null → Elo des meistgespielten Levels (wie Dashboard), nicht stur Level 0.
            Today = await _goals.GetTodayAsync(user.Id),
            Puzzles = await _puzzles.GetStatsAsync(user.Id, null),
            WeeklyPost = await GetWeeklyPostAsync(user.Id),
            Tournaments = await GetTournamentsAsync(user.Id),
        };
    }

    /// <summary>
    /// Anstehende / laufende / frisch beendete abonnierte Turniere des Users für den Motivations-DM.
    /// Zeit-Einordnung rein über das gespeicherte <see cref="Models.TournamentSubscription.EventDate"/>
    /// (keine Crawler-Calls); Ort + Ergebnis werden nur für die wenigen relevanten Turniere nachgeladen.
    /// Crawler-Fehler sind unkritisch — das Turnier wird dann ohne Ort/Ergebnis gemeldet.
    /// </summary>
    private async Task<List<BotTournamentDto>> GetTournamentsAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var subs = await _db.TournamentSubscriptions
            .Where(s => s.UserId == userId && s.EventDate != null)
            .Select(s => new { s.CrawlerTournamentId, s.TournamentName, s.EventDate })
            .ToListAsync();

        var relevant = subs
            .Select(s => (s.CrawlerTournamentId, s.TournamentName, Date: s.EventDate!.Value,
                          Days: s.EventDate!.Value.DayNumber - today.DayNumber))
            .Where(s => s.Days >= -FinishedWindowDays && s.Days <= UpcomingWindowDays)
            .OrderBy(s => Math.Abs(s.Days)) // laufend / gerade vorbei / bald zuerst
            .Take(MaxTournaments)
            .ToList();

        var list = new List<BotTournamentDto>();
        foreach (var s in relevant)
        {
            var dto = new BotTournamentDto
            {
                Name = s.TournamentName,
                Status = s.Days > 0 ? "upcoming" : s.Days == 0 ? "ongoing" : "finished",
                Date = s.Date,
                DaysUntil = s.Days,
                Location = await TryGetLocationAsync(s.CrawlerTournamentId),
            };

            if (dto.Status != "upcoming")
                await FillResultAsync(dto, userId, s.CrawlerTournamentId);

            list.Add(dto);
        }
        return list;
    }

    private async Task<string?> TryGetLocationAsync(string crawlerTournamentId)
    {
        try
        {
            var detail = await _crawler.GetAsync($"/api/tournaments/{Uri.EscapeDataString(crawlerTournamentId)}");
            if (detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("location", out var loc)
                && loc.ValueKind == JsonValueKind.String)
            {
                var s = loc.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
        }
        catch
        {
            // Crawler optional — Turnier trotzdem (ohne Ort) melden.
        }
        return null;
    }

    /// <summary>Lädt das Ergebnis des als Favorit gematchten Spielers (Punkte = Stand der höchsten Runde).</summary>
    private async Task FillResultAsync(BotTournamentDto dto, int userId, string crawlerTournamentId)
    {
        var snr = await _db.TournamentFavorites
            .Where(f => f.UserId == userId && f.CrawlerTournamentId == crawlerTournamentId && f.PlayerSnr != null)
            .Select(f => f.PlayerSnr!.Value)
            .FirstOrDefaultAsync();
        if (snr <= 0) return; // kein gematchter Spieler → kein Ergebnis

        JsonElement results;
        try
        {
            results = await _crawler.GetAsync(
                $"/api/tournaments/{Uri.EscapeDataString(crawlerTournamentId)}/players/{snr}/results");
        }
        catch
        {
            return;
        }
        if (results.ValueKind != JsonValueKind.Array) return;

        var games = 0;
        double? points = null;
        var maxRound = -1;
        foreach (var r in results.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;

            var resultStr = r.TryGetProperty("result", out var rs) && rs.ValueKind == JsonValueKind.String
                ? rs.GetString() : null;
            if (!string.IsNullOrWhiteSpace(resultStr)) games++;

            var round = r.TryGetProperty("roundNumber", out var rn) && rn.ValueKind == JsonValueKind.Number
                ? rn.GetInt32() : -1;
            var ptsStr = r.TryGetProperty("points", out var ps) && ps.ValueKind == JsonValueKind.String
                ? ps.GetString() : null;
            if (round >= maxRound && TryParsePoints(ptsStr, out var p))
            {
                points = p;
                maxRound = round;
            }
        }

        dto.ResultGames = games;
        dto.ResultPoints = points;
    }

    /// <summary>chess-results-Punkte parsen ("5,5" oder "5.5").</summary>
    private static bool TryParsePoints(string? raw, out double points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out points);
    }

    /// <summary>Jüngster fälliger Wochenpost + Fortschritt des Users (null, wenn keiner existiert/fällig ist).</summary>
    private async Task<BotWeeklyPostDto?> GetWeeklyPostAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var post = await _db.WeeklyPosts
            .Where(w => w.ScheduledAt <= now)
            .OrderByDescending(w => w.ScheduledAt)
            .Select(w => new { w.Id, w.Title, w.ScheduledAt })
            .FirstOrDefaultAsync();
        if (post == null)
            return null;

        var progress = await _weekly.GetProgressAsync(post.Id, userId);
        return new BotWeeklyPostDto
        {
            Id = post.Id,
            Title = post.Title,
            ScheduledAt = post.ScheduledAt,
            Total = progress.Total,
            PlayedCount = progress.PlayedCount,
            SolvedCount = progress.SolvedCount,
            Completed = progress.Completed,
        };
    }
}
