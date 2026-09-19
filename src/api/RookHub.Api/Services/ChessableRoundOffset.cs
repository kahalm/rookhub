using System.Globalization;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Verschiebt die KAPITELnummer in den <c>[Round "CCC.SSS"]</c>-Kopfzeilen eines Parser-PGN.
///
/// <para><b>Warum:</b> Beim laufenden Browser-Import wird jeder Chunk EINZELN geparst, und piratechess
/// zählt die Kapitel je Aufruf von vorn. Ohne Verschiebung trüge die erste Linie jedes Chunks wieder
/// <c>[Round "002.001"]</c> — und weil die LineId eines Kurs-Puzzles <c>&lt;datei&gt;:&lt;round&gt;</c>
/// ist, überschriebe Chunk 2 die Linien von Chunk 1, statt sie zu ergänzen. Jeder Chunk setzt deshalb
/// hinter der höchsten bisher vergebenen Kapitelnummer auf.</para>
///
/// <para>Die SSS-Stelle (Linie im Kapitel) bleibt unangetastet — sie zählt innerhalb des Kapitels.</para>
/// </summary>
public static class ChessableRoundOffset
{
    private static readonly Regex RoundRegex =
        new("\\[Round \"(\\d+)\\.(\\d+)\"\\]", RegexOptions.Compiled);

    /// <summary>Schiebt jede Kapitelnummer um <paramref name="chapterOffset"/> nach hinten (0 = unverändert).</summary>
    public static string Shift(string? pgn, int chapterOffset)
    {
        if (string.IsNullOrEmpty(pgn) || chapterOffset <= 0) return pgn ?? string.Empty;
        return RoundRegex.Replace(pgn, m =>
        {
            var chapter = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + chapterOffset;
            return $"[Round \"{chapter:000}.{m.Groups[2].Value}\"]";
        });
    }

    /// <summary>
    /// Versatz, damit der nächste Chunk NAHTLOS hinter der letzten Kapitelnummer weiterzählt. piratechess
    /// beginnt jeden Einzel-Aufruf bei derselben Nummer (im Bestand 002); ein Versatz um das bisherige
    /// Maximum ergäbe 002, 004, 006 … — kollisionsfrei, aber mit Lücken (so am 2026-09-19 im ersten
    /// Live-Import gesehen). Relativ zur KLEINSTEN Nummer des Chunks wird daraus 002, 003, 004 ….
    /// <c>0</c>, wenn es noch keine vorherige Nummer gibt oder der Chunk keine trägt.
    /// </summary>
    public static int NextOffset(int previousMaxChapter, string? chunkPgn)
    {
        if (previousMaxChapter <= 0) return 0;
        var min = MinChapter(chunkPgn);
        return min <= 0 ? 0 : Math.Max(0, previousMaxChapter - (min - 1));
    }

    /// <summary>Kleinste Kapitelnummer im PGN, <c>0</c> ohne Treffer.</summary>
    public static int MinChapter(string? pgn)
    {
        var min = 0;
        if (string.IsNullOrEmpty(pgn)) return min;
        foreach (Match m in RoundRegex.Matches(pgn))
        {
            var chapter = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (min == 0 || chapter < min) min = chapter;
        }
        return min;
    }

    /// <summary>
    /// Höchste Kapitelnummer im PGN, <c>0</c> ohne Treffer. Der nächste Chunk setzt darauf auf — bewusst
    /// aus dem ERGEBNIS gelesen und nicht mitgezählt: welche Nummer piratechess vergibt (der Bestand
    /// beginnt je nach Kurs bei 001 oder 002), ist dessen Sache und darf sich ändern.
    /// </summary>
    public static int MaxChapter(string? pgn)
    {
        var max = 0;
        if (string.IsNullOrEmpty(pgn)) return max;
        foreach (Match m in RoundRegex.Matches(pgn))
        {
            var chapter = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (chapter > max) max = chapter;
        }
        return max;
    }
}
