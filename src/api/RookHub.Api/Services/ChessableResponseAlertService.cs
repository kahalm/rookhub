using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using Serilog.Context;

namespace RookHub.Api.Services;

/// <summary>
/// Meldungen von RepCheck über eine UNERWARTETE Chessable-Antwort beim „Kurs holen“. RepCheck (ab 1.60.0) stoppt
/// dann den Abruf und bittet den Nutzer, vor einem erneuten Versuch Bescheid zu geben. Jede Meldung geht als Warnung
/// ins Log (Suchbegriff <c>ChessableUnexpectedResponse</c>, Tags <c>chessable,extension,crawl</c>) und wird
/// regelmäßig durchgesehen (TODO.md, „Periodisch“): erst am Wortlaut ist zu erkennen, ob Chessable gegen das
/// automatische Holen vorgeht oder bloß eine Antwort anders aussieht als erwartet.
///
/// <para>JEDE Meldung legt zusätzlich eine Admin-Nachricht im Thread des Nutzers an (seit 0.695.1, gewünscht 07.10.2026:
/// „damit ich das zeitnah prüfe“ — vorher nur bei einer Sperre, alles andere stand allein im Log und wurde erst beim
/// periodischen Durchsehen gefunden). Derselbe Kanal wie „falsches Turnier melden“, mit Glocke bei allen Admins und
/// einem Rückweg zum Nutzer. Gegen Fluten: eine Sperre höchstens einmal je Nutzer in <see cref="BanMessageCooldown"/>,
/// eine sonstige unerwartete Antwort höchstens einmal je Nutzer UND Kurs in <see cref="UnexpectedMessageCooldown"/> —
/// wer nach der Warnung trotzdem erneut holt, soll den Kanal nicht füllen. Anlass der Sperr-Regel: zu 31 Linien lieferte
/// Chessable am 30.06.2026 nur <c>{"error":{"message":"User is banned or deleted"}}</c>, aufgefallen erst im September.</para>
/// </summary>
public class ChessableResponseAlertService
{
    /// <summary>Erste Zeile jeder automatischen Sperr-Nachricht — daran erkennt die Sperrfrist die eigenen.</summary>
    public const string BanMessagePrefix = "[RepCheck] Chessable-Sperre gemeldet";

    public static readonly TimeSpan BanMessageCooldown = TimeSpan.FromHours(24);

    /// <summary>Erste Zeile jeder automatischen Nachricht zu einer unerwarteten Antwort, die nicht nach Sperre aussieht.</summary>
    public const string UnexpectedMessagePrefix = "[RepCheck] Unerwartete Chessable-Antwort";

    public static readonly TimeSpan UnexpectedMessageCooldown = TimeSpan.FromHours(1);

    /// <summary>So viel vom Antwort-Ausschnitt steht in der Admin-Nachricht (das Log bekommt ihn ganz).</summary>
    public const int MessageSnippetChars = 600;

    // Dieselbe Wortliste prüft RepCheck (extension/lib/chessable-crawl.js, looksBanned) für den Hinweis im Browser.
    // Nur gemeinsam ändern — sonst sagt der Browser „Admins benachrichtigt“, und hier kommt nichts an.
    private static readonly Regex BanPattern = new(@"\b(banned|suspended|blocked|deleted)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    private readonly AppDbContext _db;
    private readonly AdminMessageService _messages;
    private readonly ILogger<ChessableResponseAlertService> _logger;

    public ChessableResponseAlertService(AppDbContext db, AdminMessageService messages,
        ILogger<ChessableResponseAlertService> logger)
    {
        _db = db;
        _messages = messages;
        _logger = logger;
    }

    /// <summary>
    /// Sieht die Antwort nach einer Sperre aus? Mit Fehlermeldung zählt allein sie — Kursinhalte dürfen das Wort
    /// „deleted“ enthalten. Ohne Fehlermeldung nur ein Ausschnitt, der KEIN JSON ist (eine Sperrseite wie
    /// „Sorry, you have been blocked“). Dieselbe Regel wie in RepCheck.
    /// </summary>
    public static bool LooksBanned(string? message, string? snippet)
    {
        if (!string.IsNullOrWhiteSpace(message)) return Matches(message);
        var s = (snippet ?? "").TrimStart();
        return s.Length > 0 && s[0] != '{' && s[0] != '[' && Matches(s);
    }

    private static bool Matches(string text)
    {
        try { return BanPattern.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public async Task<ChessableUnexpectedResponseResultDto> ReportAsync(
        int userId, ChessableUnexpectedResponseInputDto dto, CancellationToken ct = default)
    {
        var banned = LooksBanned(dto.Message, dto.Snippet);

        using (LogContext.PushProperty("LogTags", "chessable,extension,crawl"))
            _logger.LogWarning(
                "ChessableUnexpectedResponse: user {UserId}, bid {Bid}, {Endpoint} lid {Lid} oid {Oid}, HTTP {Status}, " +
                "reason {Reason}, banned {Banned}, RepCheck {ExtensionVersion}: {ChessableMessage} | {Snippet}",
                userId, dto.Bid, dto.Endpoint, dto.Lid, dto.Oid, dto.Status, dto.Reason, banned,
                dto.ExtensionVersion, dto.Message, dto.Snippet);

        if (!banned)
        {
            // Je Nutzer UND Kurs entprellt: derselbe Kurs, gleich wieder versucht, ist eine Nachricht; ein anderer Kurs
            // ist ein neuer Fall. Die bid steht als „(bid N)“ in der Nachricht (BuildMessage).
            var seit = DateTime.UtcNow - UnexpectedMessageCooldown;
            var marke = $"(bid {dto.Bid})";
            var schonGemeldet = await _db.AdminMessages.AnyAsync(m => m.UserId == userId && !m.FromAdmin
                && m.CreatedAt >= seit && m.Body.StartsWith(UnexpectedMessagePrefix) && m.Body.Contains(marke), ct);
            if (!schonGemeldet)
                await _messages.SendFromUserAsync(userId, BuildMessage(dto, banned: false));
            return new ChessableUnexpectedResponseResultDto(false, true);
        }

        var since = DateTime.UtcNow - BanMessageCooldown;
        var alreadySent = await _db.AdminMessages.AnyAsync(m => m.UserId == userId && !m.FromAdmin
            && m.CreatedAt >= since && m.Body.StartsWith(BanMessagePrefix), ct);
        if (!alreadySent)
            await _messages.SendFromUserAsync(userId, BuildBanMessage(dto));
        return new ChessableUnexpectedResponseResultDto(true, true);
    }

    /// <summary>Erste Zeile jeder automatischen Nachricht zu einem abgebrochenen „Kurs holen“ (Fehler ohne Chessable-Anteil).</summary>
    public const string CrawlErrorMessagePrefix = "[RepCheck] Kurs holen abgebrochen";

    /// <summary>
    /// „Kurs holen“ ist mit einem Fehler abgebrochen, der keine unerwartete Chessable-Antwort war (RepCheck ≥ 1.73.0):
    /// Warnung im Log (<c>ChessableCrawlError</c>) und Admin-Nachricht, je Nutzer UND Kurs höchstens eine in
    /// <see cref="UnexpectedMessageCooldown"/>. Anlass 07.10.2026: in Firefox scheiterte jeder Versand an RookHub,
    /// der Nutzer sah „RepCheck was updated or reloaded“, und auf dem Server stand davon nichts.
    /// </summary>
    public async Task<ChessableCrawlErrorResultDto> ReportCrawlErrorAsync(int userId, ChessableCrawlErrorInputDto dto,
        CancellationToken ct = default)
    {
        using (LogContext.PushProperty("LogTags", "chessable,extension,crawl"))
            _logger.LogWarning(
                "ChessableCrawlError: user {UserId}, bid {Bid}, target {Target}, phase {Phase}, lines {LinesFetched} geholt / " +
                "{LinesSent} gesendet, RepCheck {ExtensionVersion}, {Browser}: {ErrorMessage}",
                userId, dto.Bid, dto.Target, dto.Phase, dto.LinesFetched, dto.LinesSent, dto.ExtensionVersion, dto.Browser,
                dto.Message);

        var seit = DateTime.UtcNow - UnexpectedMessageCooldown;
        var marke = $"(bid {dto.Bid})";
        var schonGemeldet = await _db.AdminMessages.AnyAsync(m => m.UserId == userId && !m.FromAdmin
            && m.CreatedAt >= seit && m.Body.StartsWith(CrawlErrorMessagePrefix) && m.Body.Contains(marke), ct);
        if (!schonGemeldet)
            await _messages.SendFromUserAsync(userId, BuildCrawlErrorMessage(dto));
        return new ChessableCrawlErrorResultDto(true);
    }

    internal static string BuildCrawlErrorMessage(ChessableCrawlErrorInputDto dto)
    {
        var course = string.IsNullOrWhiteSpace(dto.CourseName) ? "?" : dto.CourseName.Trim();
        var lines = new List<string>
        {
            CrawlErrorMessagePrefix,
            "„Kurs holen“ in RepCheck ist mit einem Fehler abgebrochen (keine unerwartete Chessable-Antwort, sondern ein Fehler "
                + "in der Erweiterung oder beim Senden an RookHub).",
            "",
            $"Kurs: {course} (bid {dto.Bid}) — https://www.chessable.com/course/{dto.Bid}/",
            $"Ziel: {dto.Target ?? "?"}, Phase: {dto.Phase ?? "?"}, Linien: {dto.LinesFetched?.ToString() ?? "?"} geholt / "
                + $"{dto.LinesSent?.ToString() ?? "?"} gesendet",
            $"Fehler: „{dto.Message.Trim()}“",
            $"RepCheck {dto.ExtensionVersion ?? "?"}, {dto.Browser ?? "Browser ?"}, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
        };
        return string.Join("\n", lines);
    }

    internal static string BuildBanMessage(ChessableUnexpectedResponseInputDto dto) => BuildMessage(dto, banned: true);

    internal static string BuildMessage(ChessableUnexpectedResponseInputDto dto, bool banned)
    {
        var where = dto.Oid != null ? $" oid {dto.Oid}" : dto.Lid != null ? $" lid {dto.Lid}" : "";
        var course = string.IsNullOrWhiteSpace(dto.CourseName) ? "?" : dto.CourseName.Trim();
        var lines = new List<string>
        {
            banned ? BanMessagePrefix : UnexpectedMessagePrefix,
            banned
                ? "RepCheck hat beim „Kurs holen“ eine Antwort bekommen, die nach einer Sperre aussieht, und den Abruf gestoppt."
                : "RepCheck hat beim „Kurs holen“ eine Antwort bekommen, die nicht die erwartete Form hat, und den Abruf "
                  + "gestoppt. Bitte prüfen, ob dahinter eine Anti-Crawling-Maßnahme steckt; der Nutzer wartet auf das Okay.",
            "",
            $"Kurs: {course} (bid {dto.Bid}) — https://www.chessable.com/course/{dto.Bid}/",
            $"Abruf: {dto.Endpoint}{where}, HTTP {dto.Status?.ToString() ?? "?"}"
                + (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $", Grund {dto.Reason}"),
        };
        if (!string.IsNullOrWhiteSpace(dto.Message))
            lines.Add($"Chessable: „{dto.Message.Trim()}“");
        // Ohne Sperr-Meldung zählt der Wortlaut der Antwort — er steht deshalb auch neben einer Fehlermeldung da.
        if (!string.IsNullOrWhiteSpace(dto.Snippet) && (string.IsNullOrWhiteSpace(dto.Message) || !banned))
            lines.Add("Antwort (Ausschnitt): " + (dto.Snippet.Length > MessageSnippetChars
                ? dto.Snippet[..MessageSnippetChars] + " …"
                : dto.Snippet));
        lines.Add($"RepCheck {dto.ExtensionVersion ?? "?"}, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        return string.Join("\n", lines);
    }
}
