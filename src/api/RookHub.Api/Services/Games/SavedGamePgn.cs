using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Scoresheet;

namespace RookHub.Api.Services.Games;

/// <summary>
/// PGN gespeicherter Partien (Zerlegung Z13 aus <see cref="SavedGameService"/>): bauen (RepCheck-Save, vom Server
/// angelegt, Upload, Korrektur), Kopfdaten säubern, Datum/Elo/Bedenkzeit prüfen, Züge nachspielen und lexikalisch lesen.
/// Zeichengenau abgesichert durch <c>SavedGamePgnTests</c> und <c>SavedGamePgnGoldenTests</c>.
/// </summary>
internal static class SavedGamePgn
{
    internal static readonly HashSet<string> AllowedSources = new(StringComparer.OrdinalIgnoreCase)
        { "chess.com", "lichess" };

    internal static readonly HashSet<string> AllowedResults = new() { "1-0", "0-1", "1/2-1/2", "*" };

    /// <summary>Die sieben Pflicht-Header in ihrer PGN-Reihenfolge — beim Neuschreiben kommen sie zuerst.</summary>
    internal static readonly string[] SevenTags = { "Event", "Site", "Date", "Round", "White", "Black", "Result" };

    internal static bool IsStandardStart(string fen)
        => string.Join(' ', fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4))
           == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";

    internal static bool IsLoadableFen(string fen)
    {
        try { Chess.ChessBoard.LoadFromFen(fen); return true; } catch { return false; }
    }

    /// <summary>Die UCI-Hauptvariante als SAN in der Schreibweise des Bretts; bricht am ersten nicht spielbaren Zug ab.</summary>
    internal static List<string> SansOf(string fen, IReadOnlyList<string> uci)
    {
        var board = Chess.ChessBoard.LoadFromFen(fen);
        var sans = new List<string>(uci.Count);
        foreach (var u in uci)
        {
            var move = Array.Find(board.Moves(generateSan: true), m => GamePlies.ToUci(m) == u);
            if (move == null || !board.Move(move)) break;
            sans.Add(string.IsNullOrEmpty(move.San) ? u : move.San);
        }
        return sans;
    }

    /// <summary>Kennung einer hochgeladenen Partie: Hash über die Seven-Tag-Kopfdaten, die FEN und die Züge.</summary>
    internal static string ImportKey(IReadOnlyDictionary<string, string> headers, IReadOnlyList<string> sans)
    {
        string V(string k) => headers.TryGetValue(k, out var v) ? v.Trim() : "";
        var text = string.Join("|", SevenTags.Select(V).Append(V("FEN"))) + "|" + string.Join(' ', sans);
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..40].ToLowerInvariant();
    }

    /// <summary>Spielt die Züge nach und gibt sie in der Schreibweise des Bretts zurück; wirft beim ersten
    /// illegalen Zug (mit seiner Nummer).</summary>
    public static List<string> LegalSans(IReadOnlyList<string> sans, string? startFen = null)
    {
        var board = string.IsNullOrWhiteSpace(startFen) ? new Chess.ChessBoard() : Chess.ChessBoard.LoadFromFen(startFen);
        var result = new List<string>(sans.Count);
        for (var i = 0; i < sans.Count; i++)
        {
            var legal = board.Moves(generateSan: true);
            var key = ScoresheetNotation.Key(sans[i]);
            var move = legal.FirstOrDefault(m => ScoresheetNotation.Key(m.San ?? string.Empty) == key);
            if (move == null || !board.Move(move))
                throw new ArgumentException($"Illegal move {sans[i]} at ply {i + 1}.");
            result.Add(string.IsNullOrEmpty(move.San) ? sans[i] : move.San);
        }
        return result;
    }

    /// <summary>PGN aus vorhandenen Headern (bleiben, soweit hier nicht bearbeitet) + neuen Kopfdaten + Zügen.</summary>
    /// <param name="pgnEscaped">PGN-Upload: <paramref name="existing"/> trägt die Werte ROH aus dem Text (noch
    /// maskiert, <c>\"</c>), <paramref name="header"/> bereits entmaskierte (<see cref="UnescapeTag"/>). Geschrieben
    /// wird dann korrekt maskiert (<see cref="ImportHeader"/>) statt mit Apostroph — vorher wurde aus
    /// <c>"Landesliga \"A\""</c> ein <c>"Landesliga \'A\'"</c>. Ohne den Schalter (Korrektur, Vereinspartie, vom
    /// Server angelegt) bleibt es bei <see cref="Header"/>; bereits gespeicherte Partien bleiben, wie sie sind.</param>
    internal static string BuildHeaderedPgn(Dictionary<string, string> existing, GameHeaderInput header, string result,
        IReadOnlyList<string> sans, string? startFen, IReadOnlyDictionary<int, string>? comments, bool pgnEscaped = false)
    {
        Func<string?, string> seven = pgnEscaped ? ImportHeader : Header;
        Func<string?, string> kept = pgnEscaped ? v => ImportHeader(UnescapeTag(v)) : Header;
        var tags = new Dictionary<string, string>(existing, StringComparer.Ordinal)
        {
            ["Event"] = seven(header.Event),
            ["Site"] = seven(header.Site),
            ["Date"] = PgnDate(header.Date),
            ["Round"] = seven(header.Round),
            ["White"] = seven(header.White),
            ["Black"] = seven(header.Black),
            ["Result"] = result,
        };
        var sb = new StringBuilder();
        foreach (var t in SevenTags) sb.Append('[').Append(t).Append(" \"").Append(tags[t]).Append("\"]\n");
        foreach (var (k, v) in tags.Where(kv => !SevenTags.Contains(kv.Key)))
            sb.Append('[').Append(k).Append(" \"").Append(kept(v)).Append("\"]\n");
        sb.Append('\n');
        sb.Append(PgnWriter.MoveText(sans, startFen, comments, result));
        return sb.ToString();
    }

    /// <summary>Datum aus dem Formular: <c>2026-06-05</c>, <c>2026.06.05</c> oder <c>5.6.2026</c> → PGN
    /// <c>2026.06.05</c>; sonst <c>????.??.??</c>.</summary>
    internal static string PgnDate(string? date)
        => ParseDate(date) is { } d ? d.ToString("yyyy.MM.dd") : "????.??.??";

    internal static DateTime? ParseDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var formats = new[] { "yyyy-MM-dd", "yyyy.MM.dd", "d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "dd.MM.yy", "yyyy/MM/dd" };
        return DateTime.TryParseExact(date.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc)
            : null;
    }

    internal static string? Clip(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    /// <summary>Hosts der beiden Quellen (<see cref="AllowedSources"/>), jeweils samt Subdomains (www., m.).</summary>
    private static readonly string[] SourceUrlHosts = ["chess.com", "lichess.org"];

    /// <summary>
    /// Der Herkunfts-Link einer Partie, wie ihn die Oberflaechen als „Original oeffnen" rendern — auch auf der
    /// ANONYMEN Teilen-Seite <c>/g/{token}</c> (Codereview F4-014). Nur absolute http(s)-Adressen der beiden
    /// Quellen; alles andere wird <c>null</c>, statt unter dem Etikett der App auf eine beliebige Seite zu
    /// fuehren (javascript: neutralisiert Angular ohnehin, eine fremde https-Seite nicht). Gilt beim Speichern
    /// UND beim Ausliefern — so ist auch ein frueher gespeicherter Wert entschaerft.
    /// </summary>
    public static string? SafeSourceUrl(string? raw)
    {
        var s = Clip(raw, 1000);
        if (s is null || !Uri.TryCreate(s, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        var host = uri.Host;
        return SourceUrlHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)) ? s : null;
    }

    /// <summary>Baut ein PGN aus SAN-Zugliste + Headern (Seven-Tag-Roster, Best-Effort).</summary>
    public static string BuildPgn(List<string> moves, SaveGameInputDto dto, string result)
    {
        var sb = new StringBuilder();
        sb.Append("[Event \"RepCheck saved game\"]\n");
        sb.Append("[Site \"").Append(Header(dto.SourceUrl)).Append("\"]\n");
        sb.Append("[Date \"").Append(dto.PlayedAt?.ToString("yyyy.MM.dd") ?? "????.??.??").Append("\"]\n");
        sb.Append("[White \"").Append(Header(dto.White)).Append("\"]\n");
        sb.Append("[Black \"").Append(Header(dto.Black)).Append("\"]\n");
        sb.Append("[Result \"").Append(result).Append("\"]\n");
        // Elo/Rating nur ausgeben, wenn plausibel (100–4000) — sonst weglassen.
        if (IsPlausibleElo(dto.WhiteElo)) sb.Append("[WhiteElo \"").Append(dto.WhiteElo).Append("\"]\n");
        if (IsPlausibleElo(dto.BlackElo)) sb.Append("[BlackElo \"").Append(dto.BlackElo).Append("\"]\n");
        if (CleanTimeControl(dto.TimeControl) is string tc) sb.Append("[TimeControl \"").Append(tc).Append("\"]\n");
        sb.Append('\n');
        sb.Append(PgnWriter.MoveText(moves, result: result));
        return sb.ToString();
    }

    /// <summary>Plausibilitäts-Check für ein Elo/Rating (verhindert Müll-Header).</summary>
    internal static bool IsPlausibleElo(int? elo) => elo is >= 100 and <= 4000;

    /// <summary>Das Elo, wenn es plausibel ist — sonst <c>null</c> (für die Spalten).</summary>
    internal static int? PlausibleElo(int? elo) => IsPlausibleElo(elo) ? elo : null;

    /// <summary>
    /// Bedenkzeit in der Schreibweise, die das PGN kennt: <c>600</c>, <c>180+2</c>, <c>1/86400</c>
    /// (Fernschach) oder <c>-</c>. Alles andere wird verworfen statt gespeichert — der Wert geht
    /// ungeprüft in einen PGN-Header, und die Liste rechnet daraus „3 + 2".
    /// </summary>
    public static string? CleanTimeControl(string? raw)
    {
        var tc = raw?.Trim();
        if (string.IsNullOrEmpty(tc) || tc.Length > 32) return null;
        return Regex.IsMatch(tc, @"^(-|\d{1,6}(\+\d{1,4})?|\d{1,3}/\d{1,7})$") ? tc : null;
    }

    /// <summary>Liest ein Elo aus einem PGN-Header (z. B. <c>[WhiteElo "1832"]</c>); null wenn fehlt/unplausibel.</summary>
    public static int? ParseEloHeader(string pgn, string tag)
    {
        if (string.IsNullOrEmpty(pgn)) return null;
        var m = Regex.Match(pgn, $"\\[{Regex.Escape(tag)}\\s+\"(\\d{{1,4}})\"\\]");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var elo) && IsPlausibleElo(elo)) return elo;
        return null;
    }

    /// <summary>Header-Wert säubern: leere → "?", Anführungszeichen/Zeilenumbrüche entfernen.
    /// <para>Bewusst NICHT <see cref="PgnWriter.Escape"/>: hier wird das Anführungszeichen durch ein
    /// Apostroph ERSETZT, nicht maskiert — eine andere Entscheidung, und die gespeicherten Partien
    /// tragen sie bereits.</para></summary>
    internal static string Header(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "?";
        return value.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    /// <summary>Header-Wert eines PGN-Uploads (bereits entmaskiert): leere → "?", Zeilenumbrüche weg, dann
    /// korrekt maskiert wie <see cref="PgnWriter.Escape"/> (erst Backslash, dann Anführungszeichen).</summary>
    internal static string ImportHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "?";
        return PgnWriter.Escape(value.Replace("\n", " ").Replace("\r", " ").Trim());
    }

    /// <summary>Hebt die PGN-Maskierung eines rohen Tag-Werts auf (<c>\"</c> → <c>"</c>, <c>\\</c> → <c>\</c>); ein
    /// einzelner Backslash vor einem anderen Zeichen bleibt stehen.</summary>
    internal static string? UnescapeTag(string? value)
    {
        if (value is null || !value.Contains('\\')) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is '"' or '\\') { sb.Append(value[++i]); continue; }
            sb.Append(value[i]);
        }
        return sb.ToString();
    }

    /// <summary>Die Halbzüge eines gespeicherten PGN, rein lexikalisch (ohne Brett — auch alt gespeicherte,
    /// lückenhafte Partien lassen sich so lesen): Kommentare, NAGs, Zugnummern und das Ergebnis fallen weg.</summary>
    internal static List<string> StoredSans(string pgn)
    {
        var moveText = PgnParser.SplitGames(pgn ?? string.Empty).FirstOrDefault().MoveText ?? string.Empty;
        moveText = Regex.Replace(moveText, @"\{[^}]*\}", " ");
        var sans = new List<string>();
        foreach (var token in moveText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (char.IsDigit(token[0]) && (token.Contains('.') || token.Contains('…'))) continue;   // Zugnummer
            if (token[0] == '$' || AllowedResults.Contains(token)) continue;                        // NAG, Ergebnis
            sans.Add(token);
        }
        return sans;
    }

    /// <summary>Vergleichsform einer Zugliste: ohne die „…"-Platzhalter der alten DOM-Auslese, Schach-/
    /// Matt-Zeichen und Bewertungen am Zugende, Rochade mit O statt 0.</summary>
    internal static List<string> SanKeys(IEnumerable<string> sans)
        => sans.Select(s => s.Trim())
            .Where(s => s.Length > 0 && s.Any(c => c != '.' && c != '…'))
            .Select(s => s.TrimEnd('+', '#', '!', '?').Replace('0', 'O'))
            .ToList();

    /// <summary>Ist <paramref name="head"/> der Anfang von <paramref name="line"/> (gleich lang zählt mit)?</summary>
    internal static bool IsPrefixOf(IReadOnlyList<string> head, IReadOnlyList<string> line)
    {
        if (head.Count > line.Count) return false;
        for (var i = 0; i < head.Count; i++)
            if (!string.Equals(head[i], line[i], StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>Zählt die Halbzüge eines gebauten PGN (Movetext nach der Leerzeile).</summary>
    internal static int CountPlies(string pgn)
    {
        var idx = pgn.IndexOf("\n\n", StringComparison.Ordinal);
        var movetext = idx >= 0 ? pgn[(idx + 2)..] : pgn;
        var count = 0;
        foreach (var token in movetext.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 0) continue;
            if (char.IsDigit(token[0]) && token.Contains('.')) continue;   // Zugnummer "12."
            if (AllowedResults.Contains(token)) continue;                  // Ergebnis-Token
            count++;
        }
        return count;
    }
}
