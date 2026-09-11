using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Zerlegt einen ZWEISPRACHIGEN Kommentarblock in seine beiden Sprachen.
///
/// <para><b>Warum das ueberhaupt noetig ist:</b> das PGN-Format kennt keine Sprachauszeichnung, und
/// ChessBase haengt beim Export die Sprachen ohne Trennzeichen aneinander. Gemessen am 2026-09-11
/// ueber alle 130 572 Zeilen des Rohbestands: <b>kein einziger</b> <c>[%lang</c>-Marker. Ein Block
/// sieht so aus — erst der englische Absatz, direkt dahinter der deutsche:</para>
///
/// <code>{The day before both Arjun and myself had lost our games. … Am Tag zuvor hatten sowohl
/// Arjun als auch ich unsere Partien verloren. …}</code>
///
/// <para><b>Zwei Signale entscheiden</b>, und das zweite ist das schaerfere:</para>
/// <list type="number">
/// <item>Funktionswoerter je Sprache (dieselben Listen wie <see cref="CommentLanguage"/>).</item>
/// <item>Die FIGURENBUCHSTABEN: die englische Haelfte schreibt <c>Be3</c> und <c>Ng4</c>, die
/// deutsche <c>Le3</c> und <c>Sg4</c>. Gezaehlt werden nur Buchstaben, die in GENAU EINER der
/// beiden Sprachen vorkommen — bei <c>fr/nl</c> etwa sagt ein <c>D</c> (Dame/Dame) nichts.</item>
/// </list>
///
/// <para><b>Ein Block hat nicht zwei Haelften, sondern beliebig viele Abschnitte.</b> Die erste
/// Fassung suchte EINEN Schnitt und traf damit nur die Haelfte der langen Bloecke: sobald der
/// Kommentator eine Nebenvariante einschiebt (die beim Einlesen an den Kommentar angehaengt wird),
/// steht dort en-de-en-de. Zugeordnet wird deshalb SATZWEISE mit einem Wechselaufschlag
/// (<see cref="SwitchPenalty"/>) — ein Satz bleibt bei der Sprache seines Vorgaengers, solange die
/// Belege nicht klar dagegen sprechen. Am Bestand gemessen (2026-09-11, 60 Partien, 875 lange
/// Bloecke): 55 % getrennt mit einem Schnitt, 93 % mit der satzweisen Zuordnung.</para>
///
/// <para><b>Geschnitten wird nur an einem Satzende</b>, nie mitten im Satz: ein halbierter Satz ist
/// schlimmer als ein zweisprachiger Block. Und gar nicht geschnitten wird, wenn die Belege duenn
/// oder widerspruechlich sind — dann bleibt der Block ganz und zaehlt zur ersten Sprache.</para>
/// </summary>
public static class CommentSplit
{
    /// <summary>So viele eigene Treffer braucht JEDE Seite. Darunter ist die Zuordnung geraten:
    /// „Sehr gut!" enthaelt kein Funktionswort und keinen Zug.</summary>
    public const int MinMarkersPerSide = 2;

    /// <summary>Was ein Sprachwechsel „kostet". Ohne Aufschlag sprаenge die Zuordnung bei jedem
    /// belegfreien Satz hin und her; mit ihm bleibt ein Satz bei der Sprache seines Vorgaengers,
    /// solange nichts klar dagegen spricht.</summary>
    private const int SwitchPenalty = 2;

    /// <summary>Wie viele Treffer auf der falschen Seite stehen duerfen: hoechstens ein Drittel der
    /// richtigen. Ein Kommentator zitiert gelegentlich einen fremdsprachigen Satz — das darf den
    /// Schnitt nicht verhindern, aber ein Block mit durchmischten Sprachen ist keiner mit zwei
    /// Haelften und bleibt besser ganz.</summary>
    private const int WrongToRightRatio = 3;

    /// <summary>
    /// Die Figurenbuchstaben je Sprache. Der KOENIG steht ueberall dort, wo er „K" heisst, und sagt
    /// deshalb bei den meisten Paaren nichts — das faellt von selbst heraus, weil nur Buchstaben
    /// zaehlen, die in genau einer der beiden Sprachen vorkommen.
    /// </summary>
    private static readonly Dictionary<string, string> PieceLetters = new(StringComparer.Ordinal)
    {
        ["en"] = "KQRBN",   // King Queen Rook Bishop Knight
        ["de"] = "KDTLS",   // Koenig Dame Turm Laeufer Springer
        ["fr"] = "RDTFC",   // Roi Dame Tour Fou Cavalier
        ["nl"] = "KDTLP",   // Koning Dame Toren Loper Paard
        ["es"] = "RDTAC",   // Rey Dama Torre Alfil Caballo
        ["it"] = "RDTAC",   // Re Donna Torre Alfiere Cavallo
        ["pt"] = "RDTBC",   // Rei Dama Torre Bispo Cavalo
        ["pl"] = "KHWGS",   // Krol Hetman Wieza Goniec Skoczek
        ["cs"] = "KDVSJ",   // Kral Dama Vez Strelec Jezdec
        ["hu"] = "KVBFH",   // Kiraly Vezer Bastya Futo Huszar
        ["sv"] = "KDTLS",   // Kung Dam Torn Loepare Springare
    };


    /// <summary>
    /// Die Wortlisten fuer den Schnitt — deutlich laenger als die von <see cref="CommentLanguage"/>.
    ///
    /// <para>Der Grund ist der Massstab: dort wird eine GANZE Partie eingeordnet (hunderte Woerter,
    /// da genuegen zwanzig Marker), hier ein einzelner SATZ. Am echten Beispiel gemessen — „Natuerlich
    /// waren wir beide in dieser Partie auf einen Kampf aus." enthaelt von den zwanzig Markern der
    /// Sprachpruefung KEINEN einzigen, und der Block waere ungeteilt geblieben.</para>
    ///
    /// <para>Laengere Listen duerfen sie sein, weil hier nur ZWEI Sprachen gegeneinander stehen und
    /// alles, was in beiden vorkommt, vorher herausfaellt (<see cref="Vocabulary"/>). Genau deshalb
    /// stehen die bekannten Doppelgaenger („in", „so", „also", „man", „am", „war", „hat", „die")
    /// absichtlich in BEIDEN Listen: so heben sie sich auf, statt eine Seite zu verfaelschen.</para>
    /// </summary>
    private static readonly Dictionary<string, string[]> RichMarkers = new(StringComparer.Ordinal)
    {
        ["de"] =
        [
            "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "eines",
            "und", "oder", "aber", "auch", "noch", "nur", "schon", "nach", "vor", "bei", "mit",
            "von", "zum", "zur", "im", "am", "auf", "aus", "fur", "uber", "unter", "durch", "gegen",
            "ist", "sind", "war", "waren", "wird", "werden", "wurde", "hat", "hatte", "haben",
            "kann", "konnte", "muss", "mussen", "sollte", "ware", "nicht", "kein", "keine", "sich",
            "ich", "er", "wir", "man", "dieser", "diese", "dieses", "diesem", "damit", "weil",
            "dass", "wenn", "als", "wie", "so", "sehr", "gut", "besser", "schwarz", "weiss", "zug",
            "stellung", "partie", "figur", "bauer", "turm", "laufer", "springer", "dame", "konig",
            "feld", "hier", "dann", "immer", "wieder", "ohne", "sein", "seine", "in", "an", "zwei",
        ],
        ["en"] =
        [
            "the", "a", "an", "and", "or", "but", "also", "still", "only", "after", "before", "at",
            "with", "from", "to", "in", "on", "for", "over", "under", "through", "against", "is",
            "are", "was", "were", "will", "would", "has", "had", "have", "can", "could", "must",
            "should", "not", "no", "this", "that", "these", "those", "it", "he", "we", "one",
            "which", "because", "if", "as", "how", "so", "very", "good", "better", "black", "white",
            "move", "position", "game", "piece", "pawn", "rook", "bishop", "knight", "queen",
            "king", "square", "attack", "here", "then", "always", "again", "without", "his", "of",
            "by", "there", "their", "been", "did", "does", "man", "am", "war", "hat", "die", "two",
        ],
    };

    /// <summary>Die Woerter, an denen <paramref name="lang"/> gegen <paramref name="other"/> zu
    /// erkennen ist: die eigene Liste OHNE alles, was auch in der anderen steht.</summary>
    private static HashSet<string> Vocabulary(string lang, string other)
    {
        var mine = RichMarkers.TryGetValue(lang, out var rich) ? rich : CommentLanguage.MarkersOf(lang);
        var theirs = RichMarkers.TryGetValue(other, out var otherRich) ? otherRich : CommentLanguage.MarkersOf(other);
        var set = new HashSet<string>(mine, StringComparer.Ordinal);
        set.ExceptWith(theirs);
        return set;
    }

    /// <summary>Das Ergebnis: je Sprache der Text. Enthaelt IMMER mindestens einen Eintrag.</summary>
    public static Dictionary<string, string> Split(string? text, IReadOnlyList<string> languages)
    {
        var whole = (text ?? string.Empty).Trim();
        var primary = languages.Count > 0 ? languages[0] : "und";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (whole.Length == 0) return result;
        if (languages.Count < 2) { result[primary] = whole; return result; }

        var a = languages[0];
        var b = languages[1];
        var sentences = Sentences(whole);
        if (sentences.Count < 2) { result[a] = whole; return result; }

        var (aHits, bHits) = Score(whole, sentences, a, b);
        var assignment = Assign(aHits, bHits);

        // Beide Sprachen muessen wirklich belegt sein, und die Zuordnung muss ueberwiegend
        // stimmen — sonst ist der Block nicht zweisprachig, sondern nur unklar.
        int aOwn = 0, aWrong = 0, bOwn = 0, bWrong = 0;
        for (var i = 0; i < assignment.Length; i++)
        {
            if (assignment[i] == 0) { aOwn += aHits[i]; aWrong += bHits[i]; }
            else { bOwn += bHits[i]; bWrong += aHits[i]; }
        }
        var usesBoth = assignment.Contains(0) && assignment.Contains(1);
        var confident = usesBoth
            && aOwn >= MinMarkersPerSide && bOwn >= MinMarkersPerSide
            && (aWrong + bWrong) * WrongToRightRatio <= aOwn + bOwn;
        if (!confident)
        {
            result[a] = whole;
            return result;
        }

        var partA = Join(whole, sentences, assignment, 0);
        var partB = Join(whole, sentences, assignment, 1);
        if (partA.Length == 0 || partB.Length == 0) { result[a] = whole; return result; }

        result[a] = partA;
        result[b] = partB;
        return result;
    }

    /// <summary>Die Saetze EINER Sprache, in ihrer urspruenglichen Reihenfolge aneinandergehaengt.</summary>
    private static string Join(string text, List<Sentence> sentences, int[] assignment, int state)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < sentences.Count; i++)
        {
            if (assignment[i] != state) continue;
            var part = text.AsSpan(sentences[i].Start, sentences[i].Length).Trim();
            if (part.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(part);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Je Satz die Sprache (<c>0</c> = die erste, <c>1</c> = die zweite) — die beste Folge bei
    /// einem Aufschlag je Wechsel. Das ist ein gewoehnliches Viterbi ueber zwei Zustaende: fuer
    /// jeden Satz wird gemerkt, was der beste Weg bis hierhin kostet, wenn er in Sprache X endet.
    /// </summary>
    private static int[] Assign(IReadOnlyList<int> aHits, IReadOnlyList<int> bHits)
    {
        var n = aHits.Count;
        var best = new int[n, 2];
        var from = new int[n, 2];

        best[0, 0] = aHits[0] - bHits[0];
        best[0, 1] = bHits[0] - aHits[0];

        for (var i = 1; i < n; i++)
        {
            for (var state = 0; state < 2; state++)
            {
                var stay = best[i - 1, state];
                var switched = best[i - 1, 1 - state] - SwitchPenalty;
                var better = stay >= switched ? state : 1 - state;
                best[i, state] = (state == 0 ? aHits[i] - bHits[i] : bHits[i] - aHits[i])
                                 + Math.Max(stay, switched);
                from[i, state] = better;
            }
        }

        var last = best[n - 1, 0] >= best[n - 1, 1] ? 0 : 1;
        var path = new int[n];
        for (var i = n - 1; i >= 0; i--)
        {
            path[i] = last;
            if (i > 0) last = from[i, last];
        }
        return path;
    }

    /// <summary>Je Satz: wie viele Belege fuer Sprache A, wie viele fuer B.</summary>
    private static (List<int> A, List<int> B) Score(string text, List<Sentence> sentences, string a, string b)
    {
        var aLetters = DistinctLetters(a, b);
        var bLetters = DistinctLetters(b, a);
        var aWords = Vocabulary(a, b);
        var bWords = Vocabulary(b, a);
        var aHits = new List<int>(sentences.Count);
        var bHits = new List<int>(sentences.Count);

        foreach (var s in sentences)
        {
            var span = text.AsSpan(s.Start, s.Length);
            int ha = 0, hb = 0;
            foreach (var token in Tokens(span))
            {
                if (token.Word.Length > 0)
                {
                    if (aWords.Contains(token.Word)) ha++;
                    else if (bWords.Contains(token.Word)) hb++;
                }
                if (token.PieceLetter != '\0')
                {
                    if (aLetters.IndexOf(token.PieceLetter) >= 0) ha++;
                    else if (bLetters.IndexOf(token.PieceLetter) >= 0) hb++;
                }
            }
            aHits.Add(ha);
            bHits.Add(hb);
        }
        return (aHits, bHits);
    }

    /// <summary>Die Figurenbuchstaben, die <paramref name="lang"/> hat und <paramref name="other"/>
    /// NICHT — nur die unterscheiden.</summary>
    private static string DistinctLetters(string lang, string other)
    {
        if (!PieceLetters.TryGetValue(lang, out var mine)) return string.Empty;
        PieceLetters.TryGetValue(other, out var theirs);
        theirs ??= string.Empty;
        var sb = new StringBuilder(5);
        foreach (var c in mine)
            if (theirs.IndexOf(c) < 0) sb.Append(c);
        return sb.ToString();
    }

    private readonly record struct Sentence(int Start, int Length);
    private readonly record struct Token(string Word, char PieceLetter);

    /// <summary>
    /// Saetze — mit einer schachtauglichen Regel: ein Satzende ist ein <c>. ! ? : ;</c> NACH einem
    /// Buchstaben (oder einem Feld), gefolgt von Leerraum.
    ///
    /// <para>Bewusst GROSSZUEGIG: der Doppelpunkt zaehlt mit, und der naechste Buchstabe darf klein
    /// sein. Beides kommt in diesen Texten vor — „…will lose the game quickly: wird die Partie
    /// schnell verlieren:" und „…without Nc3. fuehrt zu einer beliebten Variante…" sind echte
    /// Uebersetzungspaare mitten im Block. Zu FEIN zu trennen kostet nichts: die Zuordnung
    /// (<see cref="Assign"/>) legt benachbarte Saetze derselben Sprache ohnehin wieder zusammen,
    /// waehrend ein VERPASSTES Satzende zwei Sprachen fuer immer aneinanderkettet.</para>
    ///
    /// <para>Die Bedingung „nach einem BUCHSTABEN" ist der ganze Trick: „8. Ng4" und „1...e5" haben
    /// eine Ziffer davor und sind damit kein Satzende, obwohl dort ein Punkt, ein Leerzeichen und
    /// ein Grossbuchstabe stehen. Umgekehrt ist eine Abkuerzung („z.B. Weiss") faelschlich ein
    /// Satzende — das ist unschaedlich: ein zu frueh getrennter Satz landet trotzdem auf der
    /// richtigen Seite, waehrend ein VERPASSTES Satzende nur die Aufloesung verkleinert.</para>
    /// </summary>
    private static List<Sentence> Sentences(string text)
    {
        var list = new List<Sentence>();
        var start = 0;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or ':' or ';')) continue;
            if (!EndsAWord(text, i)) continue;

            var j = i + 1;
            while (j < text.Length && (text[j] is '"' or '\'' or ')' or ']' or '.' or '!' or '?')) j++;
            if (j >= text.Length || !char.IsWhiteSpace(text[j])) continue;
            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
            if (j >= text.Length) continue;

            list.Add(new Sentence(start, j - start));
            start = j;
            i = j - 1;
        }
        if (start < text.Length) list.Add(new Sentence(start, text.Length - start));
        return list;
    }

    /// <summary>
    /// Steht vor dem Satzzeichen an <paramref name="i"/> etwas, das einen Satz beenden kann?
    ///
    /// <para>Ein Buchstabe reicht nicht als Bedingung: die englische Haelfte endet auffallend oft
    /// auf einem ZUG („…well met by Ng4."), und davor steht eine Ziffer. Ein Zug endet aber auf
    /// einem FELD — Linie plus Reihe —, und daran ist er von einer Zugnummer zu unterscheiden:
    /// „8. Ng4" hat vor dem Punkt nur die 8 und ist deshalb weiterhin kein Satzende.</para>
    /// </summary>
    private static bool EndsAWord(string text, int i)
    {
        var j = i - 1;
        while (j >= 0 && text[j] is '!' or '?' or '+' or '#') j--;
        if (j < 0) return false;
        if (char.IsLetter(text[j])) return true;
        return text[j] is >= '1' and <= '8' && j > 0 && text[j - 1] is >= 'a' and <= 'h';
    }

    /// <summary>
    /// Die Woerter eines Satzes, jeweils gefaltet (wie <see cref="CommentLanguage"/>) — und dazu,
    /// falls das Wort ein ZUG ist, sein Figurenbuchstabe.
    /// </summary>
    private static IEnumerable<Token> Tokens(ReadOnlySpan<char> span)
    {
        var tokens = new List<Token>();
        var raw = new StringBuilder(24);
        var folded = new StringBuilder(24);

        void Flush()
        {
            if (raw.Length > 0)
            {
                tokens.Add(new Token(folded.ToString(), PieceLetterOf(raw.ToString())));
                raw.Clear();
                folded.Clear();
            }
        }

        foreach (var c in span)
        {
            // Der ROHE Text traegt Gross-/Kleinschreibung und Ziffern (fuer den Zug), der gefaltete
            // ist die Form der Wortlisten.
            if (char.IsLetterOrDigit(c))
            {
                raw.Append(c);
                var f = CommentLanguage.FoldChar(c);
                if (f is >= 'a' and <= 'z') folded.Append(f);
                continue;
            }
            if (c is 'x' or '+' or '#') { raw.Append(c); continue; }
            Flush();
        }
        Flush();
        return tokens;
    }

    /// <summary>Der Figurenbuchstabe eines Zuges (<c>Sg4</c>, <c>Lxe3</c>, <c>Txf7</c>), sonst
    /// <c>'\0'</c>. Ein Bauernzug (<c>e4</c>) und eine Rochade tragen keinen.</summary>
    private static char PieceLetterOf(string token)
    {
        if (token.Length < 3 || !char.IsUpper(token[0])) return '\0';

        // Hinter dem Figurenbuchstaben: optionale Herkunft (Linie/Reihe), optionales x, dann das Feld.
        var i = 1;
        if (i < token.Length && (token[i] is >= 'a' and <= 'h' or >= '1' and <= '8')) i++;
        if (i < token.Length && token[i] == 'x') i++;
        if (i + 1 >= token.Length) return '\0';
        if (token[i] is < 'a' or > 'h') return '\0';
        if (token[i + 1] is < '1' or > '8') return '\0';
        return token[0];
    }
}
