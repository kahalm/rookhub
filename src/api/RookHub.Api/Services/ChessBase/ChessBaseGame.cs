using System.Text;

namespace RookHub.Api.Services.ChessBase;

/// <summary>Eine Partie aus einer ChessBase-Datenbank: Kopfdaten und die Hauptvariante in SAN. <see cref="Error"/> ist
/// gesetzt, wenn sich die Hauptvariante nicht lesen oder nicht spielen ließ — die Partie fehlt dann im PGN.</summary>
public sealed record ChessBaseGame
{
    /// <summary>Nummer in der Datenbank (1-basiert, wie ChessBase sie zeigt).</summary>
    public int Id { get; init; }
    public string White { get; init; } = "?";
    public string Black { get; init; } = "?";
    public string Event { get; init; } = "?";
    public string Site { get; init; } = "?";
    /// <summary>PGN-Datum „2025.12.05", unbekannte Teile als „??".</summary>
    public string Date { get; init; } = "????.??.??";
    public string Round { get; init; } = "?";
    public string Result { get; init; } = "*";
    public int WhiteElo { get; init; }
    public int BlackElo { get; init; }
    public string? Eco { get; init; }
    public string? Annotator { get; init; }
    /// <summary>FIDE-ID aus dem Spieler-Eintrag (nur 2CBH führt sie).</summary>
    public string? WhiteFideId { get; init; }
    public string? BlackFideId { get; init; }
    /// <summary>Startstellung, wenn die Partie nicht aus der Grundstellung beginnt.</summary>
    public string? StartFen { get; init; }
    public IReadOnlyList<string> Moves { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
}

/// <summary>Was ein Leser zurückgibt: die Partien in Datenbank-Reihenfolge und was er nicht mitgenommen hat.</summary>
public sealed record ChessBaseReadResult(
    ChessBaseFormat Format,
    IReadOnlyList<ChessBaseGame> Games,
    int Deleted,
    int Texts,
    bool Truncated);

/// <summary>Kodierungen, die beide Formate gleich halten (Datum, Ergebnis, ECO) — aus Morphys Formatbeschreibung.</summary>
internal static class ChessBaseFields
{
    /// <summary>Gepacktes Datum: Bits 0–4 Tag, 5–8 Monat, 9–20 Jahr; 0 = unbekannt.</summary>
    public static string Date(int packed)
    {
        var day = packed & 31;
        var month = (packed >> 5) & 15;
        var year = (packed >> 9) & 0xfff;
        return $"{(year == 0 ? "????" : year.ToString("0000"))}.{(month is >= 1 and <= 12 ? month.ToString("00") : "??")}." +
               $"{(day is >= 1 and <= 31 ? day.ToString("00") : "??")}";
    }

    /// <summary>0 = 0-1, 1 = remis, 2 = 1-0, 3 = Linie (unbeendet), 4–6 dieselben kampflos, 7 = beide verloren.</summary>
    public static string Result(int value) => value switch
    {
        0 or 4 => "0-1",
        1 or 5 => "1/2-1/2",
        2 or 6 => "1-0",
        _ => "*",
    };

    /// <summary><c>wert / 128 − 1</c> = ECO ab 0 (A00 = 0 … E99 = 499); 0 = keins, ab 64576 Chess960.</summary>
    public static string? Eco(int value)
    {
        if (value <= 0 || value >= 64576) return null;
        var code = value / 128 - 1;
        return code is < 0 or > 499 ? null : $"{(char)('A' + code / 100)}{code % 100:00}";
    }

    public static string Round(int round, int subround) =>
        round <= 0 ? "?" : subround > 0 ? $"{round}.{subround}" : round.ToString();

    public static string Player(string last, string first)
    {
        last = last.Trim();
        first = first.Trim();
        if (last.Length == 0 && first.Length == 0) return "?";
        return first.Length == 0 ? last : last.Length == 0 ? first : $"{last}, {first}";
    }

    public static string OrUnknown(string s) => string.IsNullOrWhiteSpace(s) ? "?" : s.Trim();

    /// <summary>Die Partien als PGN — Hauptvariante ohne Kommentare, Kopfzeilen wie im ChessBase-Export.</summary>
    public static string ToPgn(IEnumerable<ChessBaseGame> games)
    {
        var sb = new StringBuilder();
        foreach (var g in games.Where(g => g.Error == null))
        {
            sb.Append(PgnWriter.Tag("Event", g.Event)).Append(PgnWriter.Tag("Site", g.Site)).Append(PgnWriter.Tag("Date", g.Date))
              .Append(PgnWriter.Tag("Round", g.Round)).Append(PgnWriter.Tag("White", g.White)).Append(PgnWriter.Tag("Black", g.Black))
              .Append(PgnWriter.Tag("Result", g.Result));
            if (!string.IsNullOrWhiteSpace(g.Annotator)) sb.Append(PgnWriter.Tag("Annotator", g.Annotator));
            if (g.Eco != null) sb.Append(PgnWriter.Tag("ECO", g.Eco));
            if (g.WhiteElo > 0) sb.Append(PgnWriter.Tag("WhiteElo", g.WhiteElo.ToString()));
            if (g.BlackElo > 0) sb.Append(PgnWriter.Tag("BlackElo", g.BlackElo.ToString()));
            if (g.WhiteFideId != null) sb.Append(PgnWriter.Tag("WhiteFideId", g.WhiteFideId));
            if (g.BlackFideId != null) sb.Append(PgnWriter.Tag("BlackFideId", g.BlackFideId));
            if (g.StartFen != null) sb.Append(PgnWriter.Tag("SetUp", "1")).Append(PgnWriter.Tag("FEN", g.StartFen));
            sb.Append(PgnWriter.Tag("PlyCount", g.Moves.Count.ToString()));
            sb.Append('\n').Append(PgnWriter.MoveText(g.Moves, g.StartFen, result: g.Result)).Append("\n\n");
        }
        return sb.ToString();
    }
}
