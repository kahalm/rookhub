using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Liest EINE Partie aus einem PGN und fuellt daraus die Spalten des Rohbestands
/// (<see cref="LibraryGame"/>) — Kopfdaten, Kommentar-Merkmale und den Dubletten-Griff.
///
/// <para><b>Warum ein eigener Durchgang und nicht <see cref="GamePlies"/>:</b> jenes zerlegt die
/// Partie, indem es sie NACHSPIELT — je Halbzug alle legalen Zuege erzeugen und den passenden
/// suchen. Das ist richtig, wenn hinterher Stellungen gebraucht werden, und viel zu teuer, wenn nur
/// gezaehlt werden soll: bei 130 679 Partien geht es um Minuten gegen Stunden. Hier laeuft deshalb
/// ein einziger Textdurchgang, der ueberhaupt kein Brett anfasst.</para>
///
/// <para>Der Preis: dieser Durchgang PRUEFT die Partie nicht. Ob die Zuege legal sind, entscheidet
/// erst die Uebernahme in eine <see cref="GameAnalysis"/> — dort laeuft <c>GamePlies.Parse</c>, und
/// dort gehoert die Pruefung auch hin. Im Rohbestand zu liegen heisst „eingelesen", nicht „gut".</para>
/// </summary>
public static class LibraryGameReader
{
    /// <summary>
    /// Baut die Bibliothekszeile zu einer Partie. <c>null</c>, wenn im Text kein einziger Zug steht
    /// — eine Zeile ohne Zuege waere Statistik ueber nichts.
    /// </summary>
    /// <param name="pgn">Das vollstaendige PGN GENAU EINER Partie (Kopfzeilen inklusive).</param>
    /// <param name="headers">Die schon zerlegten Kopfzeilen.</param>
    /// <param name="moveText">Der Zugteil derselben Partie.</param>
    /// <param name="sourceFile">Dateiname der Sammlung, aus der sie kam.</param>
    public static LibraryGame? From(string pgn, IReadOnlyDictionary<string, string> headers, string moveText,
        string? sourceFile = null)
    {
        var stats = Analyse(moveText);
        if (stats.PlyCount == 0) return null;

        return new LibraryGame
        {
            SourceFile = Cut(sourceFile, 200),
            SourceTitle = Cut(Tag(headers, "SourceTitle"), 200),
            SourceRef = Cut(Tag(headers, "Source"), 100),
            ExternalGameId = Cut(Tag(headers, "GameId"), 40),
            MovesHash = stats.MovesHash,

            White = Cut(Tag(headers, "White"), 120),
            Black = Cut(Tag(headers, "Black"), 120),
            WhiteElo = Number(Tag(headers, "WhiteElo")),
            BlackElo = Number(Tag(headers, "BlackElo")),
            Result = Cut(Tag(headers, "Result"), 16),
            Event = Cut(Tag(headers, "Event"), 200),
            Site = Cut(Tag(headers, "Site"), 120),
            Round = Cut(Tag(headers, "Round"), 20),
            PlayedOn = Date(Tag(headers, "Date")) ?? Date(Tag(headers, "EventDate")),
            Eco = Cut(Tag(headers, "ECO"), 8),
            StartFen = Cut(Tag(headers, "FEN"), 120),
            PlyCount = stats.PlyCount,

            Annotator = Cut(Tag(headers, "Annotator"), 200),
            CommentCount = stats.CommentCount,
            CommentedPlies = stats.CommentedPlies,
            CommentChars = stats.CommentChars,
            NagCount = stats.NagCount,
            VariationCount = stats.VariationCount,

            Pgn = pgn,
        };
    }

    /// <summary>Was ein Textdurchgang ueber den Zugteil hergibt.</summary>
    /// <param name="PlyCount">Halbzuege der HAUPTVARIANTE.</param>
    /// <param name="CommentCount">Kommentare in geschweiften Klammern, Nebenvarianten eingeschlossen.</param>
    /// <param name="CommentedPlies">Halbzuege der Hauptvariante, die einen Kommentar tragen. Zwei
    /// Kommentare hintereinander zaehlen einmal — gefragt ist, wie oft die Partie etwas zu sagen hat.</param>
    /// <param name="CommentChars">Zeichen Kommentartext insgesamt.</param>
    /// <param name="NagCount">Symbol-Bewertungen (<c>$1</c>, <c>$16</c>, …).</param>
    /// <param name="VariationCount">Geoeffnete Nebenvarianten.</param>
    /// <param name="MovesHash">SHA-256 ueber die normalisierte Zugfolge der Hauptvariante.</param>
    public readonly record struct GameStats(int PlyCount, int CommentCount, int CommentedPlies,
        int CommentChars, int NagCount, int VariationCount, string MovesHash);

    /// <summary>
    /// Der eine Textdurchgang. Zaehlt mit, was die Vorsortierung braucht, und sammelt nebenbei die
    /// Zuege der Hauptvariante fuer den Dubletten-Hash.
    ///
    /// <para><b>Kommentare in Nebenvarianten zaehlen NICHT als kommentierter Halbzug.</b> Die
    /// Punktepartie laeuft die Hauptvariante entlang und haelt dort an; was in einer Klammer steht,
    /// bekommt der Spielende nie zu sehen. Fuer <see cref="GameStats.CommentCount"/> zaehlen sie
    /// mit, denn dort ist die Frage „wie viel wurde geschrieben".</para>
    /// </summary>
    public static GameStats Analyse(string? moveText)
    {
        if (string.IsNullOrWhiteSpace(moveText))
            return new GameStats(0, 0, 0, 0, 0, 0, string.Empty);

        var s = moveText;
        var depth = 0;
        int plies = 0, comments = 0, commented = 0, chars = 0, nags = 0, variations = 0;
        var lastCommentedPly = -1;
        var moves = new StringBuilder(s.Length / 4);

        for (var i = 0; i < s.Length;)
        {
            var c = s[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '{')
            {
                var close = s.IndexOf('}', i + 1);
                if (close < 0) close = s.Length - 1;
                comments++;
                chars += close - i - 1;
                // Nur die Hauptvariante, und nur EINMAL je Halbzug: „{gut} {sehr gut}" ist eine Stelle.
                if (depth == 0 && plies > 0 && lastCommentedPly != plies)
                {
                    commented++;
                    lastCommentedPly = plies;
                }
                i = close + 1;
                continue;
            }

            // Kommentar bis Zeilenende — im Bestand selten, aber laut Norm erlaubt.
            if (c == ';')
            {
                var eol = s.IndexOf('\n', i);
                comments++;
                chars += (eol < 0 ? s.Length : eol) - i - 1;
                if (depth == 0 && plies > 0 && lastCommentedPly != plies)
                {
                    commented++;
                    lastCommentedPly = plies;
                }
                i = eol < 0 ? s.Length : eol + 1;
                continue;
            }

            if (c == '(') { depth++; variations++; i++; continue; }
            if (c == ')') { if (depth > 0) depth--; i++; continue; }

            if (c == '$')
            {
                nags++;
                i++;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                continue;
            }

            // Ein Wort bis zum naechsten Trennzeichen.
            var start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '(' or ')' or ';' or '$'))
                i++;
            var token = s.AsSpan(start, i - start);
            if (token.IsEmpty) { i++; continue; }
            if (!IsMove(token)) continue;

            if (depth == 0)
            {
                plies++;
                if (moves.Length > 0) moves.Append(' ');
                AppendNormalized(moves, token);
            }
        }

        var hash = plies == 0 ? string.Empty : Sha256(moves.ToString());
        return new GameStats(plies, comments, commented, chars, nags, variations, hash);
    }

    /// <summary>Zugnummern („12.", „12…"), Ergebnisse und Reste sind keine Zuege.</summary>
    private static bool IsMove(ReadOnlySpan<char> token)
    {
        if (token.Length == 0) return false;
        if (token is "1-0" or "0-1" or "1/2-1/2" or "*") return false;

        // Zugnummer: nur Ziffern und Punkte. „12.e4" (ohne Leerzeichen) faellt hier NICHT
        // durch — dort steht hinter den Punkten noch ein Buchstabe.
        var onlyDigitsAndDots = true;
        foreach (var ch in token)
            if (!char.IsAsciiDigit(ch) && ch != '.' && ch != '…') { onlyDigitsAndDots = false; break; }
        if (onlyDigitsAndDots) return false;

        // Ein Zug faengt mit einer Figur, einer Linie oder einer Rochade an.
        var first = token[0];
        return first is (>= 'a' and <= 'h') or 'K' or 'Q' or 'R' or 'B' or 'N' or 'O' or '0'
            || char.IsAsciiDigit(first);   // „12.e4" — die Zugnummer klebt am Zug
    }

    /// <summary>
    /// Die Schreibweise, unter der zwei Fassungen derselben Partie gleich aussehen sollen: fuehrende
    /// Zugnummer weg, Ausrufe- und Fragezeichen weg.
    ///
    /// <para>Die Bewertungszeichen MUESSEN weg — sie sind die Meinung des Kommentators, und genau
    /// die unterscheidet sich zwischen zwei Fassungen. Ohne das faende der Abgleich keine einzige
    /// Dublette, obwohl die Zuege dieselben sind. Schach und Matt (<c>+</c>, <c>#</c>) bleiben
    /// stehen: die stehen nicht zur Debatte, die folgen aus der Stellung.</para>
    /// </summary>
    private static void AppendNormalized(StringBuilder sb, ReadOnlySpan<char> token)
    {
        var from = 0;
        // „12.e4" / „12...Sf6" — alles bis zum letzten Punkt gehoert zur Zugnummer.
        var lastDot = token.LastIndexOf('.');
        if (lastDot >= 0) from = lastDot + 1;

        for (var i = from; i < token.Length; i++)
        {
            var ch = token[i];
            if (ch is '!' or '?') continue;
            sb.Append(ch);
        }
    }

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? Tag(IReadOnlyDictionary<string, string> headers, string name)
    {
        if (!headers.TryGetValue(name, out var value)) return null;
        value = value.Trim();
        // ChessBase schreibt Unbekanntes als „?" bzw. „????.??.??" — das ist kein Wert.
        return value.Length == 0 || value.All(c => c is '?' or '.' or '0') ? null : value;
    }

    private static int? Number(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

    /// <summary>PGN-Datum („2004.12.30"). Unvollstaendige Angaben („1904.??.??") ergeben
    /// <c>null</c>: ein auf den 1. Januar geratenes Datum waere schlechter als keines.</summary>
    private static DateOnly? Date(string? value)
        => DateOnly.TryParseExact(value, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static string? Cut(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : (value.Length <= max ? value.Trim() : value[..max].Trim());
}
