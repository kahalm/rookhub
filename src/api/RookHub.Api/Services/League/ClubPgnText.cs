using System.Security.Cryptography;
using System.Text;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.League;

/// <summary>
/// Der PGN-Teil der Vereins-Datenbank (aus <see cref="LeagueClubService"/> gezogen, Zerlegung Z14): Text in Partien zerlegen
/// und nachspielen (<see cref="ParseAll"/>), Jahr und Tag aus dem Datum, die gespeicherte Form einer Vereinspartie
/// (<see cref="PgnOf"/>, <see cref="HashOf"/>) und was die Liste daraus liest (Eröffnung, UCI). Rein, ohne Datenbank.
/// </summary>
internal static class ClubPgnText
{
    public const int MaxImportGames = 500;
    public const int MaxImportChars = 5_000_000;
    public const int MaxPlies = 600;
    public const int ShortGamePlies = 20;
    internal const string StartPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";
    internal static readonly HashSet<string> Results = new(StringComparer.Ordinal) { "1-0", "0-1", "1/2-1/2", "*" };

    /// <summary>Der Tag einer Partie nur, wenn er ganz dasteht („2026.10.04", auch mit Bindestrichen) — „2026.??.??" ist kein Tag.</summary>
    internal static DateOnly? FullDateOf(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var t = date.Trim().Replace('-', '.');
        return DateOnly.TryParseExact(t, "yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>Eine Partie des PGN-Textes: die Kopfzeilen und die geprüfte Hauptvariante — oder warum sie sich gar nicht
    /// übernehmen lässt (<c>illegal</c>, <c>noMoves</c>, <c>tooLong</c>, <c>fromPosition</c>).</summary>
    internal sealed record Parsed(int Index, Dictionary<string, string> Headers, List<string>? Sans, string? Error, string MoveText = "")
    {
        /// <summary>Die Partie als eigener PGN-Text (0.590.0): Kopfzeilen ROH (der Leser entschlüsselt nichts, also wird
        /// auch nichts verschlüsselt) und der Zugtext unverändert — zurückgeschickt ergibt er genau diese Partie. Damit
        /// importiert die Seite portionsweise, ohne die ganze Datei je Portion noch einmal zu schicken.</summary>
        public string Pgn => string.Concat(Headers.Select(kv => $"[{kv.Key} \"{kv.Value}\"]\n")) + "\n" + MoveText.Trim() + "\n";

        public string? H(string key) => Headers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && v.Trim() != "?" ? v.Trim() : null;
        public int? Elo(string key) => int.TryParse(H(key), out var e) ? e : null;
    }

    /// <summary>Die Partien in Reihenfolge, 1-basiert nummeriert — Übersicht und Übernahme zählen gleich.</summary>
    internal static List<Parsed> ParseAll(string pgn, out bool truncated) => CheckAll(SplitAll(pgn, out truncated));

    /// <summary>Nur zerlegt (Kopfzeilen + Zugtext, nummeriert wie <see cref="ParseAll"/>), noch nicht nachgespielt —
    /// <see cref="Parsed.Sans"/> und <see cref="Parsed.Error"/> sind leer.</summary>
    internal static List<Parsed> SplitAll(string pgn, out bool truncated)
    {
        var games = PgnParser.SplitGames(pgn).Where(g => !string.IsNullOrWhiteSpace(g.MoveText)).ToList();
        truncated = games.Count > MaxImportGames;
        return games.Take(MaxImportGames)
            .Select((g, i) => new Parsed(i + 1, g.Headers ?? new Dictionary<string, string>(), null, null, g.MoveText)).ToList();
    }

    /// <summary>Die zerlegten Partien nachgespielt: Hauptvariante oder der Grund, warum sie sich nicht übernehmen lässt.</summary>
    internal static List<Parsed> CheckAll(List<Parsed> split)
    {
        var start = new Chess.ChessBoard().ToFen();
        var result = new List<Parsed>();
        foreach (var (index, h, _, _, moveText) in split)
        {
            string? Error()
            {
                if (h.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)
                    && string.Join(' ', fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4)) != StartPosition)
                    return "fromPosition";
                return null;
            }
            var err = Error();
            List<string>? sans = null;
            if (err == null)
            {
                try
                {
                    // Die Länge zählt VOR dem Nachspielen (wie bei <see cref="ClubPgnReader.AddAsync"/>): vorher spielte Gera.Chess eine überlange
                    // Hauptvariante erst GANZ nach, bevor „tooLong" griff — bis rund eine Million Halbzüge je 5-MB-Aufruf,
                    // auch ohne Konto über den Teilen-Link (Codereview 2026-09-29, N3-002).
                    var sanCount = PgnParser.ExtractMainlineSans(moveText).Count;
                    if (sanCount > MaxPlies) err = "tooLong";
                    else
                    {
                        // Nur ein Ergebnis („1-0", „*") ist KEINE Partie mit illegalem Zug — TryExtractUciMainline meldet beides null.
                        var uci = sanCount == 0 ? new List<string>() : PgnParser.TryExtractUciMainline(start, moveText);
                        if (uci == null) err = "illegal";
                        else if (uci.Count == 0) err = "noMoves";
                        else
                        {
                            sans = SavedGameService.SansOf(start, uci);
                            if (sans.Count != uci.Count) { err = "illegal"; sans = null; }
                        }
                    }
                }
                catch (Exception) { err = "illegal"; sans = null; }
            }
            result.Add(new Parsed(index, h, sans, err, moveText));
        }
        return result;
    }

    internal static string HashOf(IReadOnlyList<string> sans) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', sans)))).ToLowerInvariant();

    internal static string PgnOf(LeagueClubGame g, IReadOnlyList<string> sans)
    {
        var sb = new StringBuilder();
        sb.Append(PgnWriter.Tag("Event", g.Event ?? "?"));
        sb.Append(PgnWriter.Tag("Site", "?"));
        sb.Append(PgnWriter.Tag("Date", g.Year is { } y ? $"{y}.??.??" : "????.??.??"));
        sb.Append(PgnWriter.Tag("Round", "?"));
        sb.Append(PgnWriter.Tag("White", g.White));
        sb.Append(PgnWriter.Tag("Black", g.Black));
        sb.Append(PgnWriter.Tag("Result", g.Result));
        if (g.WhiteElo is { } we) sb.Append(PgnWriter.Tag("WhiteElo", we.ToString()));
        if (g.BlackElo is { } be) sb.Append(PgnWriter.Tag("BlackElo", be.ToString()));
        // Dieselben Namen wie bei Lumbra — die Spielerkarte erkennt die Farbe daran (LeagueProfileBuilder.ColorOf).
        if (g.WhiteFide != null) sb.Append(PgnWriter.Tag("WhiteFideId", g.WhiteFide));
        if (g.BlackFide != null) sb.Append(PgnWriter.Tag("BlackFideId", g.BlackFide));
        sb.Append('\n').Append(PgnWriter.MoveText(sans, null, null, g.Result)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Jahr aus einem PGN-Datum („2024.05.12", „2024.??.??"); unplausibel = unbekannt.</summary>
    internal static int? YearOf(string? date, DateTime now) =>
        date is { Length: >= 4 } && int.TryParse(date[..4], out var y) && y >= 1900 && y <= now.Year + 1 ? y : null;

    internal static IReadOnlyList<string> SansOf(string pgn) =>
        PgnParser.SplitGames(pgn).FirstOrDefault() is { } g ? PgnParser.ExtractMainlineSans(g.MoveText) : Array.Empty<string>();

    /// <summary>Die Hauptvariante als UCI („e2e4 e7e5 …") — für den Knopf „Analyse", der RookHubs Analysebrett mit
    /// <c>?moves=</c> öffnet (Wunsch 2026-09-28). Gespeichert wird nur ab der Grundstellung.</summary>
    internal static string UciOf(string pgn)
    {
        var moveText = PgnParser.SplitGames(pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty;
        try { return string.Join(' ', PgnParser.TryExtractUciMainline(new Chess.ChessBoard().ToFen(), moveText) ?? new List<string>()); }
        catch (Exception) { return string.Empty; }
    }

    internal static string OpeningOf(string pgn)
    {
        var moveText = PgnParser.SplitGames(pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty;
        return OpeningOfSans(PgnParser.ExtractMainlineSans(moveText));
    }

    internal static string OpeningOfSans(IEnumerable<string> all)
    {
        var sans = all.Take(6).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < sans.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append('.');
            sb.Append(sans[i]);
        }
        return sb.ToString();
    }
}
