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
/// <para>Sieht die Antwort nach einer SPERRE aus, legt der Dienst zusätzlich eine Admin-Nachricht im Thread des
/// Nutzers an — derselbe Kanal wie „falsches Turnier melden“, mit Glocke bei allen Admins und einem Rückweg zum
/// Nutzer. Höchstens eine je Nutzer in <see cref="BanMessageCooldown"/>: wer nach der Warnung trotzdem erneut holt,
/// soll den Kanal nicht füllen. Anlass: zu 31 Linien lieferte Chessable am 30.06.2026 nur
/// <c>{"error":{"message":"User is banned or deleted"}}</c>, und aufgefallen ist das erst im September.</para>
/// </summary>
public class ChessableResponseAlertService
{
    /// <summary>Erste Zeile jeder automatischen Sperr-Nachricht — daran erkennt die Sperrfrist die eigenen.</summary>
    public const string BanMessagePrefix = "[RepCheck] Chessable-Sperre gemeldet";

    public static readonly TimeSpan BanMessageCooldown = TimeSpan.FromHours(24);

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

        if (!banned) return new ChessableUnexpectedResponseResultDto(false, false);

        var since = DateTime.UtcNow - BanMessageCooldown;
        var alreadySent = await _db.AdminMessages.AnyAsync(m => m.UserId == userId && !m.FromAdmin
            && m.CreatedAt >= since && m.Body.StartsWith(BanMessagePrefix), ct);
        if (!alreadySent)
            await _messages.SendFromUserAsync(userId, BuildBanMessage(dto));
        return new ChessableUnexpectedResponseResultDto(true, true);
    }

    internal static string BuildBanMessage(ChessableUnexpectedResponseInputDto dto)
    {
        var where = dto.Oid != null ? $" oid {dto.Oid}" : dto.Lid != null ? $" lid {dto.Lid}" : "";
        var course = string.IsNullOrWhiteSpace(dto.CourseName) ? "?" : dto.CourseName.Trim();
        var lines = new List<string>
        {
            BanMessagePrefix,
            "RepCheck hat beim „Kurs holen“ eine Antwort bekommen, die nach einer Sperre aussieht, und den Abruf gestoppt.",
            "",
            $"Kurs: {course} (bid {dto.Bid}) — https://www.chessable.com/course/{dto.Bid}/",
            $"Abruf: {dto.Endpoint}{where}, HTTP {dto.Status?.ToString() ?? "?"}",
        };
        if (!string.IsNullOrWhiteSpace(dto.Message))
            lines.Add($"Chessable: „{dto.Message.Trim()}“");
        else if (!string.IsNullOrWhiteSpace(dto.Snippet))
            lines.Add("Antwort (Ausschnitt): " + (dto.Snippet.Length > MessageSnippetChars
                ? dto.Snippet[..MessageSnippetChars] + " …"
                : dto.Snippet));
        lines.Add($"RepCheck {dto.ExtensionVersion ?? "?"}, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        return string.Join("\n", lines);
    }
}
