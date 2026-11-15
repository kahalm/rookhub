using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.Games;

/// <summary>Die Partie hinter dem anonymen Link: nur Züge, Ergebnis und Blickrichtung — keine Namen, Elo, Daten, Quelle.</summary>
public sealed record AnonSharedGameDto(string Pgn, string? Result, string? OwnerSide);

/// <summary>Antwort auf „Anonym teilen": das Token und ob der Link einen Text zeigt (<c>false</c>: es gibt keinen, oder er nennt
/// einen Namen, der sich nicht sicher entfernen lässt).</summary>
public sealed record AnonShareDto(string Token, bool SummaryAvailable);

/// <summary>
/// Anonymer Teilen-Link der Zusammenfassung einer EIGENEN Partie (Wunsch 2026-11-15: „da fehlt mir ein teilen link (der auch
/// anonym teilt)"): eigenes Token (<see cref="SavedGame.AnonShareToken"/>, erst auf Zuruf angelegt), öffentlich unter
/// <c>/ga/{token}/summary</c>. Bewusst ein eigener, schmaler Weg statt einer Marke am normalen Link <c>/g/{token}</c>: an dem
/// hängen Partieseite, Bewertungen, Erklärungen, Roasts und die Link-Vorschau, die alle Namen tragen — der anonyme Link öffnet
/// NUR die zwei Lesewege hier. Widerrufen wird er zusammen mit dem normalen (<see cref="SavedGameService.RotateShareTokenAsync"/>).
/// <para>Namen verlassen den Dienst nie: die Partie kommt ohne Kopfdaten (PGN neu gebaut, nur Hauptvariante, ohne Kommentare),
/// der Text über <see cref="SafeSummary"/> — wie bei anonymisierten Vereinspartien (<see cref="ClubGameLinks.SafeSummary"/>):
/// lieber kein Text als ein Klarname.</para>
/// </summary>
public sealed class AnonGameShares(AppDbContext db)
{
    /// <summary>Link anlegen; gibt es schon einen, bleibt er. <c>null</c> = fremd/unbekannt.</summary>
    public async Task<AnonShareDto?> CreateAsync(int userId, int id, CancellationToken ct = default)
    {
        var g = await db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (g is null) return null;
        if (g.AnonShareToken is null)
        {
            // Über BEIDE Spalten eindeutig: ein anonymes Token darf nie zugleich ein normaler Link einer anderen Partie sein.
            g.AnonShareToken = await ShareTokens.NewUniqueAsync(t => db.SavedGames.AnyAsync(x => x.AnonShareToken == t || x.ShareToken == t, ct));
            await db.SaveChangesAsync(ct);
        }
        return new AnonShareDto(g.AnonShareToken, (await SummaryOfAsync(g, ct)).Status == "done");
    }

    private Task<SavedGame?> OpenAsync(string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token) ? Task.FromResult<SavedGame?>(null)
            : db.SavedGames.AsNoTracking().FirstOrDefaultAsync(x => x.AnonShareToken == token, ct);

    /// <summary>Die Partie des Links; <c>null</c> = ungültig oder widerrufen.</summary>
    public async Task<AnonSharedGameDto?> ReadAsync(string token, CancellationToken ct = default)
    {
        if (await OpenAsync(token, ct) is not { } g) return null;
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == g.UserId, ct);
        return new AnonSharedGameDto(BarePgn(g.Pgn, g.Result), g.Result, SavedGameSharing.DetermineOwnerSide(g, profile));
    }

    /// <summary>Fertiger Text ohne Namen; ohne Text — oder wenn ein Name darin stehen bliebe — <c>none</c>. <c>null</c> = ungültiger Link.</summary>
    public async Task<SharedOwnGameSummaryDto?> SummaryAsync(string token, CancellationToken ct = default) =>
        await OpenAsync(token, ct) is { } g ? await SummaryOfAsync(g, ct) : null;

    private static readonly SharedOwnGameSummaryDto None = new("none", null, null, false, null);

    private async Task<SharedOwnGameSummaryDto> SummaryOfAsync(SavedGame g, CancellationToken ct)
    {
        var s = await db.OwnGameSummaries.AsNoTracking().FirstOrDefaultAsync(x => x.SavedGameId == g.Id, ct);
        if (s?.Markdown is null) return None;
        var text = SafeSummary(s.Markdown, await NamesAsync(g, ct), german: IsGerman(s.Markdown, g.ReviewLanguage));
        return text is null ? None
            : new SharedOwnGameSummaryDto("done", text, s.DoneAt, s.MovesHash != null && s.MovesHash != OwnGameSummaries.MovesHashOf(g.Pgn), s.Model);
    }

    /// <summary>Alle Namen, die im Text nicht stehen dürfen: beide Seiten (Spalten UND PGN-Kopf), der Besitzer (Konto- und
    /// Plattformnamen, Vor-/Nachname, Anzeigename) und die Namen einer verbundenen Vereinspartie samt ihrer internen Klarnamen.</summary>
    private async Task<(string?[] White, string?[] Black, string?[] Other)> NamesAsync(SavedGame g, CancellationToken ct)
    {
        var headers = PgnParser.SplitGames(g.Pgn).Select(x => x.Headers).FirstOrDefault();
        string? Header(string key) => headers != null && headers.TryGetValue(key, out var v) ? v : null;
        var owner = await db.AppUsers.AsNoTracking().Where(u => u.Id == g.UserId).Select(u => u.Username).FirstOrDefaultAsync(ct);
        var p = await db.UserProfiles.AsNoTracking().Where(x => x.UserId == g.UserId)
            .Select(x => new { x.FirstName, x.LastName, x.DisplayName, x.ChessComUsername, x.LichessUsername, x.DiscordUsername }).FirstOrDefaultAsync(ct);
        var club = g.LeagueClubGameId is int cid
            ? await db.LeagueClubGames.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == cid)
                .Select(c => new { c.White, c.Black, c.WhiteRealName, c.BlackRealName }).FirstOrDefaultAsync(ct)
            : null;
        return (new[] { g.White, Header("White"), club?.WhiteRealName },
                new[] { g.Black, Header("Black"), club?.BlackRealName },
                new[] { owner, p?.FirstName, p?.LastName, p?.DisplayName, p?.ChessComUsername, p?.LichessUsername, p?.DiscordUsername,
                        club?.White, club?.Black, Header("WhiteTeam"), Header("BlackTeam") });
    }

    /// <summary>Platzhalter deutsch oder englisch: nach der Sprache der Partie, sonst nach dem Text selbst.</summary>
    private static bool IsGerman(string markdown, string? lang) =>
        lang is not null ? lang.StartsWith("de", StringComparison.OrdinalIgnoreCase)
            : markdown.Contains("Weiß", StringComparison.Ordinal) || markdown.Contains("Schwarz", StringComparison.Ordinal);

    private static readonly Regex PairingMention = new(@"[\s,·–-]*\b(?:Runde|Round|Rd\.?|Brett|Board)\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HeadingLine = new(@"^#{1,6}[ \t].*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static IEnumerable<string> Tokens(string? name) =>
        string.IsNullOrWhiteSpace(name) ? [] : Regex.Split(name, @"[\s,.\-]+").Where(t => t.Any(char.IsLetter));

    /// <summary>Namensteile ab drei Zeichen (wie <see cref="ClubGameLinks.SafeSummary"/>; „Dr", „De", „?" fallen heraus). Hat ein
    /// Name NUR kürzere Teile („Li, Yu"), zählen bei ihm die ab zwei Zeichen, sonst alle — kein Name bleibt ganz ungeprüft.</summary>
    internal static List<string> NameParts(IEnumerable<string?> names) =>
        names.SelectMany(n =>
        {
            var tokens = Tokens(n).ToList();
            var min = tokens.Any(t => t.Length >= 3) ? 3 : tokens.Any(t => t.Length >= 2) ? 2 : 1;
            return tokens.Where(t => t.Length >= min);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Ein Wort ist zu Ende: kein Buchstabe, keine Ziffer, kein Unterstrich davor bzw. danach (Umlaute zählen als Buchstaben).</summary>
    private const string Before = @"(?<![\p{L}\p{N}_])", After = @"(?![\p{L}\p{N}_])";
    /// <summary>Genitiv am Namen („Müllers", „Miller's") zählt als der Name.</summary>
    private const string Genitive = @"(?:['’]?s)?";

    /// <summary>Titel vor oder hinter dem Namen („Kobold, Jonathan Dr.", „GM …") gehen mit weg.</summary>
    private const string Title = @"(?:Dr|Prof|Mag|Ing|DI|GM|IM|FM|CM|NM|WGM|WIM|WFM|WCM)\.?";

    /// <summary>Eine Folge von Namensteilen („Nachname, Vorname Dr.") als EIN Treffer. Kurze Teile desselben Namens („De Vreugt",
    /// „Chen, Yan Xi") gehen nur mit weg, wenn sie an einem langen hängen — allein wären „De" oder „An" gewöhnliche Wörter.</summary>
    private static Regex? RunOf(string?[] names)
    {
        var parts = NameParts(names);
        if (parts.Count == 0) return null;
        string Alt(IEnumerable<string> xs) => $"(?:{string.Join('|', xs.OrderByDescending(p => p.Length).Select(Regex.Escape))})";
        var shorts = names.SelectMany(Tokens).Where(t => !parts.Contains(t, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var any = Alt(parts);
        var filler = shorts.Count == 0 ? Title : $"(?:{Title}|{Alt(shorts)}\\.?)";
        return new Regex($@"(?:{Before}{filler}[\s,.\-]+)*{Before}{any}(?:[\s,.\-]+(?:{any}|{filler}(?![\p{{L}}\p{{N}}_])))*{Genitive}{After}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Der Text für den anonymen Link. Die Zusammenfassungen nennen die Spieler in der Überschrift („# Nachname, Vorname –
    /// Nachname, Vorname (1-0)"), im Fließtext heißen die Seiten „Weiß" und „Schwarz" (Bestand 2026-11-15: 45 von 45). Darum:
    /// in den ÜBERSCHRIFTEN werden die Namen der Seiten durch „Weiß"/„Schwarz" (bzw. „White"/„Black") ersetzt, „Runde N"/„Brett N"
    /// fällt überall weg (wie beim Link der Vereinspartie — Runde + Brett + Gegner ergäben den Spieler). Steht danach noch
    /// irgendwo ein Namensteil (ab drei Zeichen, als ganzes Wort, auch im Genitiv: „Müllers") einer Seite, des Besitzers oder der verbundenen
    /// Vereinspartie, gibt es keinen Text (<c>null</c>). Im Fließtext wird bewusst NICHT ersetzt: ein Spieler „Bauer" machte aus
    /// jedem Bauern einen „Weiß" — dann lieber keine Zusammenfassung.
    /// </summary>
    internal static string? SafeSummary(string markdown, (string?[] White, string?[] Black, string?[] Other) names, bool german)
    {
        var (whiteRun, blackRun) = (RunOf(names.White), RunOf(names.Black));
        var text = HeadingLine.Replace(markdown, m =>
        {
            var line = m.Value;
            if (whiteRun is not null) line = whiteRun.Replace(line, german ? "Weiß" : "White");
            if (blackRun is not null) line = blackRun.Replace(line, german ? "Schwarz" : "Black");
            return line;
        });
        foreach (var part in NameParts(names.White.Concat(names.Black).Concat(names.Other)))
            if (Regex.IsMatch(text, $"{Before}{Regex.Escape(part)}{Genitive}{After}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;
        return PairingMention.Replace(text, "");
    }

    /// <summary>Nur die Züge der Hauptvariante und das Ergebnis; eine abweichende Ausgangsstellung bleibt (sonst wären die Züge
    /// nicht lesbar). Keine Namen, Elo, Daten, Orte, Kommentare, Varianten.</summary>
    internal static string BarePgn(string pgn, string? result)
    {
        var game = PgnParser.SplitGames(pgn).FirstOrDefault();
        var sans = PgnParser.ExtractMainlineSans(game.MoveText ?? string.Empty);
        var res = result is "1-0" or "0-1" or "1/2-1/2" ? result : "*";
        string? fen = game.Headers != null && game.Headers.TryGetValue("FEN", out var f) && !string.IsNullOrWhiteSpace(f)
            && !SavedGamePgn.IsStandardStart(f) ? f : null;
        var sb = new StringBuilder();
        sb.Append(PgnWriter.Tag("Result", res));
        if (fen is not null) sb.Append(PgnWriter.Tag("SetUp", "1")).Append(PgnWriter.Tag("FEN", fen));
        return sb.Append('\n').Append(PgnWriter.MoveText(sans, fen, result: res)).ToString();
    }
}
