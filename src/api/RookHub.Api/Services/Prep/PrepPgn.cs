using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Ein Paket PGN in Partien des Bestands zerlegen: nur Kopfdaten und Hauptvariante, Kommentare/Varianten/NAGs fallen weg.
/// Verworfen (mit Grund) werden Partien, die nicht ab der Grundstellung beginnen, Varianten wie Chess960, Partien ohne
/// oder mit unlesbaren Zügen und solche ohne einen einzigen bekannten Spieler.
/// </summary>
public static partial class PrepPgn
{
    /// <summary>Gründe fürs Verwerfen — die Schlüssel in <see cref="PrepImport.DiscardReasons"/>.</summary>
    public const string ReasonFen = "fen", ReasonVariant = "variant", ReasonNoMoves = "noMoves", ReasonBadMoves = "badMoves",
        ReasonTooLong = "tooLong", ReasonNoPlayers = "noPlayers";

    /// <summary>Wie <c>MainlineBoard.MaxPlies</c>: die längste bekannte Turnierpartie hat 538 Halbzüge.</summary>
    public const int MaxPlies = 1000;

    public const int NameMax = 120, EventMax = 200, RoundMax = 12;

    // Ein SAN-Zug nach PgnParser.CleanSan (ohne +#!?, Rochade mit „O", Umwandlung „=Q").
    [GeneratedRegex(@"^(?:O-O(?:-O)?|[KQRBN][a-h]?[1-8]?x?[a-h][1-8]|[a-h](?:x[a-h])?[1-8](?:=[QRBN])?)$")]
    private static partial Regex SanRegex();
    [GeneratedRegex(@"^[A-E][0-9]{2}")]
    private static partial Regex EcoRegex();

    /// <summary>Eine Seite der Partie. <see cref="Surname"/> (klein, ä → ae) vergleicht Spieler über zwei Quellen hinweg,
    /// die den Namen verschieden schreiben („Robidoux, Michel" / „Robidoux Michel").</summary>
    public sealed record Side(string Name, string NameKey, string? FideId, string Surname)
    {
        /// <summary>Identität im Bestand: die FIDE-ID, sonst der Namensschlüssel.</summary>
        public string IdentityKey => FideId is null ? NameKey : "#" + FideId;
        public long KeyHash => Hash(IdentityKey);
    }

    public sealed record Game(Side? White, Side? Black, short? WhiteElo, short? BlackElo, byte Result, int? PlayedOn,
        string? Event, string? Site, string? Round, string? Eco, string Moves, short Plies, long MovesHash)
    {
        public short? Year => PlayedOn is { } d ? (short)(d / 10000) : null;
        public bool HasEvent => Event is not null || Site is not null;
        public long EventHash => Hash((Event ?? "") + "\u001f" + (Site ?? ""));
    }

    public sealed record Result(int Read, List<Game> Games, Dictionary<string, int> Discarded);

    /// <summary>Zerlegt ein Paket. <see cref="Result.Read"/> zählt jede Partie des Pakets, auch verworfene.</summary>
    public static Result Parse(string pgn)
    {
        var games = new List<Game>();
        var discarded = new Dictionary<string, int>(StringComparer.Ordinal);
        var read = 0;
        foreach (var (headers, moveText) in PgnParser.SplitGames(pgn))
        {
            read++;
            var reason = TryRead(headers, moveText, out var g);
            if (reason is null) games.Add(g!);
            else discarded[reason] = discarded.GetValueOrDefault(reason) + 1;
        }
        return new Result(read, games, discarded);
    }

    /// <summary>Eine Partie lesen → <c>null</c> und die Partie, oder der Grund, warum sie verworfen wird.</summary>
    public static string? TryRead(IReadOnlyDictionary<string, string> h, string moveText, out Game? game)
    {
        game = null;
        if (h.TryGetValue("Variant", out var variant) && !string.IsNullOrWhiteSpace(variant)
            && !variant.Trim().Equals("standard", StringComparison.OrdinalIgnoreCase)
            && !variant.Trim().Equals("chess", StringComparison.OrdinalIgnoreCase))
            return ReasonVariant;
        // Eine FEN-Zeile mit der Grundstellung (manche Programme schreiben sie immer) ist eine normale Partie.
        if (h.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen) && !PgnParser.IsStartPosition(fen.Trim()))
            return ReasonFen;

        var sans = PgnParser.ExtractMainlineSans(moveText);
        if (sans.Count == 0) return ReasonNoMoves;
        if (sans.Count > MaxPlies) return ReasonTooLong;
        foreach (var s in sans)
            if (!SanRegex().IsMatch(s)) return ReasonBadMoves;

        var white = ReadSide(h, "White");
        var black = ReadSide(h, "Black");
        if (white is null && black is null) return ReasonNoPlayers;

        var moves = string.Join(' ', sans);
        game = new Game(white, black, Elo(h, "WhiteElo"), Elo(h, "BlackElo"), PrepResult.Parse(Get(h, "Result")),
            PlayedOn(Get(h, "Date")), Text(Get(h, "Event"), EventMax), Text(Get(h, "Site"), EventMax),
            Text(Get(h, "Round"), RoundMax), Eco(Get(h, "ECO")), moves, (short)sans.Count, Hash(moves));
        return null;
    }

    private static string? Get(IReadOnlyDictionary<string, string> h, string key) => h.TryGetValue(key, out var v) ? v : null;

    private static Side? ReadSide(IReadOnlyDictionary<string, string> h, string color)
    {
        var name = LeagueNames.Clean(Get(h, color));
        if (IsUnknownName(name)) return null;
        if (name.Length > NameMax) name = name[..NameMax];
        var key = NameKey(name);
        if (key.Length == 0) return null;
        return new Side(name, key, FideId(Get(h, color + "FideId")), Surname(name));
    }

    private static readonly HashSet<string> UnknownNames = new(StringComparer.Ordinal)
    {
        "", "?", "??", "-", "--", "nn", "n.n.", "n.n", "n. n.", "n n", "n,n", "n, n", "n.n.,", "unknown", "anonymous", "anonym",
    };

    public static bool IsUnknownName(string? name) => UnknownNames.Contains((name ?? "").Trim().ToLowerInvariant());

    /// <summary>„Grimm, Wolfgang, Dr." → „grimm, wolfgang" — klein, ohne Akzente und Titel (wie das Megabase-Verzeichnis,
    /// das ebenfalls keine Umlaute kennt).</summary>
    public static string NameKey(string? name)
    {
        var k = LeagueRosterIndex.Fold(LeagueNames.NameKey(LeagueNames.StripTitles(name)), false).Trim().TrimEnd(',', '.', ' ');
        return k.Length > NameMax ? k[..NameMax] : k;
    }

    /// <summary>Nachname zum Vergleich über Quellen: vor dem Komma, sonst das erste Wort; Umlaute als ae/oe/ue.</summary>
    public static string Surname(string? name) =>
        LeagueProfileBuilder.LastName(LeagueRosterIndex.Fold(LeagueNames.NameKey(LeagueNames.StripTitles(name)), true)).Trim('.', ' ');

    private static string? FideId(string? raw)
    {
        var f = (raw ?? "").Trim();
        if (f.Length is 0 or > 12 || !f.All(char.IsAsciiDigit)) return null;
        f = f.TrimStart('0');
        return f.Length == 0 ? null : f;
    }

    private static short? Elo(IReadOnlyDictionary<string, string> h, string key) =>
        int.TryParse(Get(h, key)?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var e) && e is > 0 and < 4000 ? (short)e : null;

    /// <summary>„1975.??.??" → 19750000, „2024.05.17" → 20240517; ohne Jahr <c>null</c>.</summary>
    public static int? PlayedOn(string? date)
    {
        var d = (date ?? "").Trim();
        if (d.Length < 4 || !int.TryParse(d.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y is < 1000 or > 2999)
            return null;
        var parts = d.Split('.', '-', '/');
        int Part(int i, int max) => parts.Length > i && int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            && v >= 1 && v <= max ? v : 0;
        var m = Part(1, 12);
        var day = m == 0 ? 0 : Part(2, 31);
        return y * 10000 + m * 100 + day;
    }

    private static string? Text(string? raw, int max)
    {
        var t = LeagueNames.Clean(raw);
        if (t is "" or "?" or "-" or "??") return null;
        return t.Length > max ? t[..max] : t;
    }

    private static string? Eco(string? raw)
    {
        var e = (raw ?? "").Trim().ToUpperInvariant();
        return EcoRegex().IsMatch(e) ? e[..3] : null;
    }

    /// <summary>Die ersten 8 Byte von SHA-256 über den Text.</summary>
    public static long Hash(string text) =>
        BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Dieselbe Seite? Beide unbekannt = ja; tragen beide eine FIDE-ID, entscheidet sie, sonst der Nachname.</summary>
    public static bool SameSide(Side? a, Side? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.FideId is not null && b.FideId is not null) return a.FideId == b.FideId;
        return a.Surname.Length > 0 && a.Surname == b.Surname;
    }

    /// <summary>Dieselbe Partie: gleiche Zugfolge UND dieselben zwei Spieler — kurze Remisen gibt es zwischen vielen Paaren.</summary>
    public static bool SameGame(Game a, Game b) =>
        a.MovesHash == b.MovesHash && a.Plies == b.Plies && SameSide(a.White, b.White) && SameSide(a.Black, b.Black);
}
