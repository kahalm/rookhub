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

    /// <summary>
    /// Schiebt jede Kapitelnummer um <paramref name="chapterOffset"/> und jede LINIENnummer um
    /// <paramref name="lineOffset"/> nach hinten (je 0 = unverändert).
    ///
    /// <para><paramref name="lineOffset"/> wird gebraucht, wenn ein ZU GROSSES Kapitel auf mehrere Chunks
    /// verteilt ankommt (siehe <c>ChessableIngestChunk</c>): piratechess nummeriert die Linien nach ihrer
    /// Position in <c>list.data</c>, und der Assembler wirft dort die Einträge heraus, für die dieser Teil
    /// keinen Inhalt mitbringt. Teil 2 begänne also wieder bei <c>.002</c> und überschriebe — weil die LineId
    /// <c>&lt;datei&gt;:&lt;round&gt;</c> ist — genau die Linien von Teil 1.</para>
    /// </summary>
    public static string Shift(string? pgn, int chapterOffset, int lineOffset = 0)
    {
        if (string.IsNullOrEmpty(pgn) || (chapterOffset <= 0 && lineOffset <= 0)) return pgn ?? string.Empty;
        return RoundRegex.Replace(pgn, m =>
        {
            var chapter = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + Math.Max(0, chapterOffset);
            var line = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + Math.Max(0, lineOffset);
            // Die ursprüngliche Stellenzahl der Linie beibehalten (mindestens 3) — sonst wechselte die
            // Schreibweise mitten im Kurs und LineIds aus zwei Chunks sähen verschieden aus.
            var width = Math.Max(3, m.Groups[2].Value.Length);
            return $"[Round \"{chapter:000}.{line.ToString(new string('0', width), CultureInfo.InvariantCulture)}\"]";
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

    /// <summary>
    /// Versatz, damit ein FORTSETZUNGS-Chunk im SELBEN Kapitel bleibt (<paramref name="currentChapter"/>)
    /// statt ein neues aufzumachen. Gegenstück zu <see cref="NextOffset"/>.
    /// </summary>
    public static int SameChapterOffset(int currentChapter, string? chunkPgn)
    {
        if (currentChapter <= 0) return 0;
        var min = MinChapter(chunkPgn);
        return min <= 0 ? 0 : Math.Max(0, currentChapter - min);
    }

    /// <summary>
    /// Versatz, damit der NÄCHSTE Teil desselben Kapitels hinter <paramref name="previousMaxLine"/>
    /// weiterzählt. Relativ zur kleinsten Nummer des neuen Teils gerechnet — welche Nummer piratechess
    /// als erste vergibt (001 oder 002), ist dessen Sache und darf sich ändern.
    /// <c>0</c>, wenn es noch keine vorherige Nummer gibt oder der Teil keine trägt.
    /// </summary>
    public static int NextLineOffset(int previousMaxLine, string? chunkPgn)
    {
        if (previousMaxLine <= 0) return 0;
        var min = MinLine(chunkPgn);
        return min <= 0 ? 0 : Math.Max(0, previousMaxLine - (min - 1));
    }

    /// <summary>Kleinste Liniennummer (SSS) im PGN, <c>0</c> ohne Treffer.</summary>
    public static int MinLine(string? pgn)
    {
        var min = 0;
        if (string.IsNullOrEmpty(pgn)) return min;
        foreach (Match m in RoundRegex.Matches(pgn))
        {
            var line = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (min == 0 || line < min) min = line;
        }
        return min;
    }

    /// <summary>Höchste Liniennummer (SSS) im PGN, <c>0</c> ohne Treffer.</summary>
    public static int MaxLine(string? pgn)
    {
        var max = 0;
        if (string.IsNullOrEmpty(pgn)) return max;
        foreach (Match m in RoundRegex.Matches(pgn))
        {
            var line = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (line > max) max = line;
        }
        return max;
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
