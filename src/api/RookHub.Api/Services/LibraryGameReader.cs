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
            FirstCommentedPly = stats.FirstCommentedPly == 0 ? null : stats.FirstCommentedPly,
            // Eine Eroeffnungszeile beschreibt einen Weg AUS DER GRUNDSTELLUNG. Faengt die Partie
            // woanders an (Vorgabepartie, Chess960, Studie), ist ihr erster Zug kein Zug, den es in
            // der Grundstellung gibt — im Eroeffnungsbaum stand deshalb ein „Kc6" an der Wurzel, und
            // ein Klick darauf konnte nur mit „geht nicht" antworten. Ohne Zeile faellt die Partie
            // aus dem Baum, ueber die Namenssuche bleibt sie erreichbar.
            OpeningLine = StartsFromInitialPosition(Tag(headers, "FEN")) ? Cut(stats.OpeningLine, 200) : null,
            SearchText = SearchTextOf(Tag(headers, "White"), Tag(headers, "Black"),
                Tag(headers, "Event"), Tag(headers, "Annotator")),

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
    /// <param name="FirstCommentedPly">Der ERSTE Halbzug der Hauptvariante mit Kommentar (1-basiert,
    /// 0 = keiner). Das ist der Punkt, an dem der Kommentator die Partie fuer erklaerungsbeduerftig
    /// hielt — und damit ein Kandidat dafuer, wo eine Punktepartie anfangen sollte.</param>
    /// <param name="OpeningLine">Die ersten <see cref="OpeningPlies"/> Halbzuege normalisiert, durch
    /// Leerzeichen getrennt. Damit laesst sich in SQL fragen, wie viele Partien des Bestandes
    /// dieselbe Eroeffnung spielen (Praefix-Suche auf einer indizierten Spalte).</param>
    public readonly record struct GameStats(int PlyCount, int CommentCount, int CommentedPlies,
        int CommentChars, int NagCount, int VariationCount, string MovesHash,
        int FirstCommentedPly, string OpeningLine);

    /// <summary>So viele Halbzuege fasst <see cref="GameStats.OpeningLine"/>. Dreissig sind fuenfzehn
    /// volle Zuege — laenger ist keine Eroeffnung mehr, und die Spalte bliebe trotzdem indizierbar.</summary>
    public const int OpeningPlies = 30;

    /// <summary>Faengt die Partie in der Grundstellung an? Fehlender FEN-Kopf heisst ja (der
    /// Normalfall), sonst entscheidet die Brettstellung ohne Zaehler. Nur solche Partien bekommen
    /// eine <c>OpeningLine</c> — sie ist der Weg AUS DER GRUNDSTELLUNG und sonst sinnlos.</summary>
    public static bool StartsFromInitialPosition(string? startFen)
        => string.IsNullOrWhiteSpace(startFen) || PgnParser.IsStartPosition(startFen);

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
            return new GameStats(0, 0, 0, 0, 0, 0, string.Empty, 0, string.Empty);

        var s = moveText;
        var depth = 0;
        int plies = 0, comments = 0, commented = 0, chars = 0, nags = 0, variations = 0;
        var lastCommentedPly = -1;
        var firstCommentedPly = 0;
        var moves = new StringBuilder(s.Length / 4);
        var opening = new StringBuilder(OpeningPlies * 6);

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
                    if (firstCommentedPly == 0) firstCommentedPly = plies;
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
                    if (firstCommentedPly == 0) firstCommentedPly = plies;
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
                if (plies <= OpeningPlies)
                {
                    if (opening.Length > 0) opening.Append(' ');
                    AppendOpeningSan(opening, token);
                }
            }
        }

        var hash = plies == 0 ? string.Empty : Sha256(moves.ToString());
        return new GameStats(plies, comments, commented, chars, nags, variations, hash,
            firstCommentedPly, opening.ToString());
    }

    /// <summary>Was <see cref="RehashBareResult"/> mit einer gespeicherten Zeile gemacht hat.</summary>
    public enum BareResultRehash
    {
        /// <summary>Kein nacktes „1/2" im Zugteil, oder das Nachrechnen ergibt dieselben Werte.</summary>
        Unchanged,
        /// <summary>Die Zugspalten stehen jetzt so da, wie ein frisches Einlesen sie schriebe.</summary>
        Rehashed,
        /// <summary>Ohne das „1/2" bliebe kein Zug — ein frisches Einlesen haette die Partie gar nicht
        /// aufgenommen. Die Zeile bleibt unangetastet; von Hand ansehen.</summary>
        NoMovesLeft,
        /// <summary>Der Hash weicht ab, aber nicht so, wie das „1/2" es erklaert (die Halbzugzahl sinkt
        /// nicht). Eine andere Abweichung — nicht angefasst; von Hand ansehen.</summary>
        OtherDrift,
    }

    /// <summary>
    /// Nachtrag fuer den Altbestand (Codereview 2026-09-29, N11-004): bis 0.624.0 zaehlte
    /// <see cref="IsMove"/> ein nacktes „1/2" als Halbzug — PlyCount eins zu hoch, „1/2" im
    /// <see cref="GameStats.MovesHash"/> (und bei Miniaturen in der <see cref="GameStats.OpeningLine"/>).
    /// Der Import fasst bestehende Zeilen nie an, und ein erneutes Einlesen legte die Partie mit dem
    /// neuen Hash DOPPELT an; diese Zeilen muessen deshalb an Ort und Stelle nachgerechnet werden.
    ///
    /// <para>Rechnet aus dem gespeicherten PGN, was <see cref="From"/> aus dem Zugteil ableitet und
    /// was vom Halbzaehler abhaengt: PlyCount, MovesHash, OpeningLine, CommentedPlies,
    /// FirstCommentedPly. Angefasst wird nur, was das „1/2" erklaert: ein nacktes „1/2"-Token im
    /// Zugteil UND weniger Halbzuege als gespeichert. Alles andere bleibt, wie es ist — das Werkzeug
    /// soll genau diesen einen Fehler heilen und keine fremde Abweichung still ueberschreiben.</para>
    /// </summary>
    /// <param name="row">Die gespeicherte Zeile; wird bei <see cref="BareResultRehash.Rehashed"/> geaendert
    /// (inklusive <c>UpdatedAt</c>), sonst nicht.</param>
    public static BareResultRehash RehashBareResult(LibraryGame row)
    {
        var moveText = GuessStartPly.MoveTextOf(row.Pgn ?? string.Empty);
        if (!HasBareHalfToken(moveText)) return BareResultRehash.Unchanged;

        var stats = Analyse(moveText);
        if (stats.PlyCount == row.PlyCount && stats.MovesHash == row.MovesHash) return BareResultRehash.Unchanged;
        if (stats.PlyCount == 0) return BareResultRehash.NoMovesLeft;
        if (row.PlyCount is not int stored || stats.PlyCount >= stored) return BareResultRehash.OtherDrift;

        row.PlyCount = stats.PlyCount;
        row.MovesHash = stats.MovesHash;
        row.OpeningLine = StartsFromInitialPosition(row.StartFen) ? Cut(stats.OpeningLine, 200) : null;
        row.CommentedPlies = stats.CommentedPlies;
        row.FirstCommentedPly = stats.FirstCommentedPly == 0 ? null : stats.FirstCommentedPly;
        row.UpdatedAt = DateTime.UtcNow;
        return BareResultRehash.Rehashed;
    }

    /// <summary>Die Zeichen, an denen <see cref="Analyse"/> ein Wort beendet.</summary>
    private static readonly char[] TokenBreaks = [' ', '\t', '\r', '\n', '{', '}', '(', ')', ';', '$'];

    /// <summary>Steht „1/2" irgendwo als eigenes Wort im Zugteil? Grob mit Absicht (auch in einem
    /// Kommentar) — ob es die Zugspalten wirklich verfaelscht hat, entscheidet das Nachrechnen.</summary>
    private static bool HasBareHalfToken(string moveText)
        => moveText.Contains("1/2", StringComparison.Ordinal)
           && moveText.Split(TokenBreaks, StringSplitOptions.RemoveEmptyEntries).Contains("1/2", StringComparer.Ordinal);

    /// <summary>Zugnummern („12.", „12…"), Ergebnisse und Reste sind keine Zuege.</summary>
    private static bool IsMove(ReadOnlySpan<char> token)
    {
        if (token.Length == 0) return false;
        if (PgnTokens.IsResultToken(token)) return false;

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
    /// <summary>
    /// Ein Zug-Token so anhaengen, wie es in einer <c>OpeningLine</c> steht: ohne Zugnummer, ohne
    /// Bewertungs- UND ohne Schachzeichen.
    ///
    /// <para><b>Diese eine Funktion benutzen BEIDE Erzeuger der Zeile</b> — der Textdurchgang hier
    /// (Bibliothek) und der Brettweg in <see cref="GameAnalysisService.OpeningLineOf"/> (eigene
    /// Analysen). Der Eroeffnungsbaum der Punktepartie sucht per Praefix ueber beide Spalten und
    /// normalisiert die Anfrage seinerseits ohne <c>+</c>/<c>#</c>
    /// (<see cref="GuessOpeningTree.Normalize"/>); eine Seite, die sie BEHAELT, ist damit ab dem
    /// ersten Schachgebot unauffindbar. Genau das war bis 0.499.12 der Fall: 36 213 von 130 055
    /// Bibliothekspartien trugen ein <c>+</c> in der Zeile, der Ast endete dort mit „keine
    /// Partien", obwohl die Partien da waren.</para>
    /// </summary>
    public static void AppendOpeningSan(StringBuilder sb, ReadOnlySpan<char> token)
        => AppendNormalized(sb, token, stripChecks: true);

    /// <summary>
    /// Zug-Token ohne Zugnummer und ohne Bewertungszeichen anhaengen.
    ///
    /// <para><b>Ohne <paramref name="stripChecks"/> darf sich hier NICHTS aendern.</b> Diese Form
    /// speist auch die Zugliste, aus der <see cref="GameStats.MovesHash"/> entsteht — die
    /// Dubletten-Erkennung des Imports. Faellt dort ein Zeichen weg, hat jede bereits importierte
    /// Partie einen anderen Hash: Dubletten gelten als neu, und ein Re-Import legt sie ein zweites
    /// Mal an. Ein Test haelt den Hash deshalb auf einem literalen Wert fest.</para>
    /// </summary>
    private static void AppendNormalized(StringBuilder sb, ReadOnlySpan<char> token, bool stripChecks = false)
    {
        var from = 0;
        // „12.e4" / „12...Sf6" — alles bis zum letzten Punkt gehoert zur Zugnummer.
        var lastDot = token.LastIndexOf('.');
        if (lastDot >= 0) from = lastDot + 1;

        for (var i = from; i < token.Length; i++)
        {
            var ch = token[i];
            if (ch is '!' or '?') continue;
            if (stripChecks && ch is '+' or '#') continue;
            sb.Append(ch);
        }
    }

    /// <summary>Spieler, Turnier und Kommentator in EINER kleingeschriebenen Zeichenkette — das
    /// Feld, ueber das die Bestandssuche laeuft (Volltext-Index, siehe
    /// <see cref="LibraryGame.SearchText"/>).</summary>
    public static string? SearchTextOf(string? white, string? black, string? evt, string? annotator)
    {
        var text = string.Join(' ', new[] { white, black, evt, annotator }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()))
            .ToLowerInvariant();
        return Cut(text, 190);
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
