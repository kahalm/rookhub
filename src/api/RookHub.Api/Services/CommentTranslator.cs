using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>Wofuer eine Uebersetzung laeuft — bestimmt Auftrag, Pruefungen und die Log-Meldungen.</summary>
public enum TranslationSubject
{
    /// <summary>Die Anmerkungen einer Partie (<see cref="CommentTranslationService"/>). Verhalten und
    /// Log-Meldungen sind WOERTLICH die von vor dem Umbau (Kibana/log-watcher haengen an den Vorlagen).</summary>
    Game,
    /// <summary>Die Texte EINER Kurs-Linie (<see cref="CourseTranslationService"/>): Zug-Kommentare,
    /// Einleitung, Titel und ggf. Kapitelname, Schluessel nach <see cref="CourseTextSlots"/>.</summary>
    CourseLine,
    /// <summary>Alle Kapitelnamen EINES Kurses in einer Fuhre — damit dasselbe Kapitel ueberall gleich heisst.</summary>
    CourseChapters,
}

/// <summary>
/// Der ankerunabhaengige Kern jeder Kommentar-Uebersetzung: <c>(Schluessel, Text)</c>-Liste plus Quell- und
/// Zielsprache hinein, <c>Schluessel → Uebersetzung</c> heraus — oder <c>null</c> = Fehlschlag, der
/// Aufrufer schreibt NICHTS.
///
/// <para>Herausgeloest aus <see cref="CommentTranslationService"/> (0.547.0), damit Partien und Kurse
/// dieselben Regeln teilen: Fuhren von hoechstens <see cref="ChunkChars"/> Zeichen, der Auftrag mit den
/// Figurenbuchstaben, <see cref="PieceLetters.Convert"/> auf das Ergebnis, die Laengenpruefung
/// (<see cref="MinLengthShare"/>) und die Sprachpruefung (<see cref="CommentLanguage"/>). Er schreibt nichts
/// in die Datenbank und kennt keinen Anker.</para>
///
/// <para><b>Unterschiede je <see cref="TranslationSubject"/>:</b> Bei Partien bleibt alles, wie es war. Bei
/// Kursen (1) zaehlen fuer Laenge und Sprache nur die PROSA-Schluessel (<see cref="CourseTextSlots.IsProse"/>),
/// und die Laenge erst ab <see cref="MinProseCharsForLengthCheck"/> Zeichen — „Stark!" fuer „Excellent!" sind
/// 60 % und trotzdem richtig; (2) muss JEDE Fuhre JEDEN Schluessel beantworten, sonst ist es ein Fehlschlag —
/// ein Kurs wird inkrementell uebersetzt, der naechste Lauf holt die Linie nach; (3) der Auftrag nennt die
/// Regeln fuer Partie-Zitate, Namen und Ueberschriften; (4) ist die Quellsprache unbekannt (<c>und</c>), heisst
/// es „from the language it is written in", und die Figurenbuchstaben bleiben, wie sie sind. KAPITELNAMEN
/// (<see cref="TranslationSubject.CourseChapters"/>) bekommen weder Laengen- noch Sprachpruefung — lauter kurze
/// Ueberschriften voller Namen; „jeder Eintrag beantwortet" gilt auch fuer sie.</para>
/// </summary>
public sealed class CommentTranslator
{
    /// <summary>So viel von der Quelllaenge muss eine Uebersetzung mindestens haben. Deutsch ist
    /// eher laenger als Englisch — liegt das Ergebnis deutlich darunter, fehlt Text.</summary>
    public const double MinLengthShare = 0.7;

    /// <summary>So viele Zeichen gehen hoechstens in EINE Fuhre. Grosszuegig, weil die Einheitlichkeit
    /// der Begriffe an der gemeinsamen Fuhre haengt; die Antwort ist etwa so lang wie die Vorlage.</summary>
    public const int ChunkChars = 8000;

    /// <summary>Kurs-Linien: unter so vielen Zeichen Prosa wird die Laenge nicht geprueft — kurze Saetze
    /// schwanken zwischen den Sprachen zu stark, als dass 70 % etwas aussagten.</summary>
    public const int MinProseCharsForLengthCheck = 150;

    private readonly IClaudeJsonClient _llm;
    private readonly ILogger _logger;

    /// <param name="logger">Der Logger des AUFRUFERS — damit die Kategorie bleibt, unter der die
    /// Meldungen bisher standen (<c>CommentTranslationService</c>).</param>
    public CommentTranslator(IClaudeJsonClient llm, ILogger logger)
    {
        _llm = llm;
        _logger = logger;
    }

    /// <summary>Ein Text-Modell ist konfiguriert (eigene Hardware oder <c>Anthropic:TextApiKey</c>).</summary>
    public bool IsAvailable => _llm.IsConfigured;

    /// <summary>Womit uebersetzt wird — gehoert an den gespeicherten Satz.</summary>
    public string ModelName => _llm.TranslationModel;

    /// <summary>
    /// Uebersetzt <paramref name="items"/> von <paramref name="from"/> nach <paramref name="to"/>.
    /// </summary>
    /// <param name="subjectId">Wofuer (Partie, Linie, Kurs) — nur fuer die Log-Meldungen.</param>
    /// <returns><c>null</c> = Fehlschlag (abgebrochen, zu kurz, falsche Sprache) — nichts schreiben. Leer =
    /// das Modell hat nichts Brauchbares geliefert (bei Partien wie bisher kein Fehlschlag, sondern „nichts").</returns>
    public async Task<Dictionary<int, string>?> TranslateAsync(IReadOnlyList<(int Key, string Text)> items,
        string? from, string to, TranslationSubject subject, int? subjectId, CancellationToken ct = default)
    {
        var translated = new Dictionary<int, string>();
        if (items.Count == 0) return translated;
        var strict = subject != TranslationSubject.Game;

        foreach (var chunk in Chunks(items))
        {
            var json = await _llm.TranslateCommentsJsonAsync(SystemPrompt(from, to, subject), UserPrompt(chunk), ct);
            if (json is null)
            {
                LogAborted(subject, subjectId, to);
                return null;   // lieber gar kein Satz als ein halber
            }
            var parsed = Parse(json);
            if (strict)
            {
                // Kurse: eine Fuhre, die nicht JEDEN Schluessel beantwortet, ist ein Fehlschlag — und was
                // nicht gefragt war, wird nicht uebernommen. Bei Partien bleibt es wie bisher (was kommt,
                // zaehlt; die Laengenpruefung faengt den Rest).
                var asked = chunk.Select(c => c.Key).ToHashSet();
                parsed = parsed.Where(p => asked.Contains(p.Ply)).ToList();
                var answered = parsed.Select(p => p.Ply).ToHashSet();
                if (!asked.All(answered.Contains))
                {
                    LogIncomplete(subject, subjectId, to, answered.Count, asked.Count);
                    return null;
                }
            }
            foreach (var (ply, text) in parsed)
                // Die Figurenbuchstaben stehen zwar im Auftrag, aber welcher Buchstabe zu welcher
                // Figur gehoert, ist nichts, was ein Modell entscheiden muss — siehe PieceLetters.
                // Unbekannte Quelle („und") → Convert laesst den Text, wie er ist.
                translated[ply] = PieceLetters.Convert(text, from, to);
        }
        if (translated.Count == 0) return translated;

        // Welche Schluessel fuer Laenge und Sprache zaehlen: bei Partien alle, bei Kursen nur die Prosa.
        bool Counts(int key) => subject switch
        {
            TranslationSubject.Game => true,
            TranslationSubject.CourseLine => CourseTextSlots.IsProse(key),
            _ => false,   // Kapitelnamen: lauter Ueberschriften
        };

        // DIE LAENGE PRUEFEN, bevor irgendetwas gespeichert wird. Am 2026-09-11 an echten Partien
        // erlebt: ein sparsameres Modell lieferte 18 bis 53 Prozent der Quelllaenge — Saetze mitten
        // im Absatz abgeschnitten, und zwar lautlos. Struktur und Zuege stimmten dabei, es fehlte
        // nur Prosa, und genau die ist die Lehre der Partie.
        //
        // Die Grenze liegt bei 70 Prozent und nicht hoeher, weil manche Quell-Saetze selbst noch
        // zweisprachig sind: dort wirft die Uebersetzung die doppelte Haelfte zu Recht weg
        // (gemessen 53 bis 55 Prozent). Lieber ein paar Faelle von Hand nachsehen als stumme
        // Luecken im Bestand.
        var quellLaenge = items.Where(i => Counts(i.Key)).Sum(i => i.Text.Length);
        var zielLaenge = translated.Where(t => Counts(t.Key)).Sum(t => t.Value.Length);
        var minimum = subject == TranslationSubject.Game ? 1 : MinProseCharsForLengthCheck;
        if (quellLaenge >= minimum && zielLaenge < quellLaenge * MinLengthShare)
        {
            LogTooShort(subject, subjectId, to, zielLaenge * 100 / quellLaenge);
            return null;
        }

        // IST ES UEBERHAUPT DIE ZIELSPRACHE? Ist die Quelle mehrsprachig — und im Rohbestand ist sie
        // das oft: eine franzoesische Anmerkung mit englischen Einschueben —, laesst ein Modell gern
        // ganze Absaetze stehen, wie sie waren. Die Laengenpruefung sieht davon nichts: der Text IST
        // ja da. Gemessen 2026-09-26 an Partie 129683: halb franzoesisch, halb englisch zurueck.
        // Geprueft wird nur, wenn wir die Zielsprache an Funktionswoertern erkennen koennen, und es
        // muss die BESTE Erklaerung fuer den Text sein — sonst reicht ein deutscher Halbsatz in einem
        // franzoesischen Absatz.
        //
        // KAPITELNAMEN pruefen wir NICHT: sie sind kurz und voller Namen, und der Auftrag sagt ausdruecklich,
        // Namen ohne uebliche Form zu behalten. Eine Liste deutscher Ueberschriften mit englischen Eroeffnungsnamen
        // („The Najdorf: the main line") liest sich sonst als englisch — und das Kapitel bliebe fuer immer offen.
        if (subject != TranslationSubject.CourseChapters && CommentLanguage.MarkersOf(to).Length > 0)
        {
            var geprueft = translated.Where(t => Counts(t.Key)).Select(t => t.Value);
            var erkannt = CommentLanguage.Detect(string.Join(" ", geprueft));
            if (erkannt is not null && !string.Equals(erkannt.Split(',')[0], to, StringComparison.Ordinal))
            {
                LogWrongLanguage(subject, subjectId, to, erkannt);
                return null;
            }
        }

        return translated;
    }

    // ── Log-Meldungen: je Gegenstand eine FESTE Vorlage (die der Partien woertlich wie vor dem Umbau) ──

    private void LogAborted(TranslationSubject subject, int? id, string to)
    {
        switch (subject)
        {
            case TranslationSubject.Game:
                _logger.LogWarning("Uebersetzung der Partie {Id} nach {Lang} abgebrochen.", id, to);
                break;
            case TranslationSubject.CourseLine:
                _logger.LogWarning("Uebersetzung der Kurs-Linie {Id} nach {Lang} abgebrochen.", id, to);
                break;
            default:
                _logger.LogWarning("Uebersetzung der Kapitelnamen von Kurs {Id} nach {Lang} abgebrochen.", id, to);
                break;
        }
    }

    private void LogIncomplete(TranslationSubject subject, int? id, string to, int got, int asked)
    {
        if (subject == TranslationSubject.CourseLine)
            _logger.LogWarning(
                "Uebersetzung der Kurs-Linie {Id} nach {Lang} verworfen: {Got} von {Asked} Texten beantwortet.",
                id, to, got, asked);
        else
            _logger.LogWarning(
                "Uebersetzung der Kapitelnamen von Kurs {Id} nach {Lang} verworfen: {Got} von {Asked} Texten beantwortet.",
                id, to, got, asked);
    }

    private void LogTooShort(TranslationSubject subject, int? id, string to, int share)
    {
        if (subject == TranslationSubject.Game)
            _logger.LogWarning(
                "Uebersetzung der Partie {Id} nach {Lang} verworfen: {Anteil} % der Quelllaenge — es fehlt Text.",
                id, to, share);
        else
            _logger.LogWarning(
                "Uebersetzung der Kurs-Linie {Id} nach {Lang} verworfen: {Anteil} % der Quelllaenge — es fehlt Text.",
                id, to, share);
    }

    private void LogWrongLanguage(TranslationSubject subject, int? id, string to, string detected)
    {
        if (subject == TranslationSubject.Game)
            _logger.LogWarning("Uebersetzung der Partie {Id} nach {Lang} verworfen: liest sich als {Erkannt}.",
                id, to, detected);
        else
            _logger.LogWarning("Uebersetzung der Kurs-Linie {Id} nach {Lang} verworfen: liest sich als {Erkannt}.",
                id, to, detected);
    }

    // ── Fuhren, Auftrag, Antwort ────────────────────────────────────────────────────────────────

    private static IEnumerable<List<(int Key, string Text)>> Chunks(IReadOnlyList<(int Key, string Text)> items)
    {
        var current = new List<(int Key, string Text)>();
        var size = 0;
        foreach (var t in items)
        {
            if (current.Count > 0 && size + t.Text.Length > ChunkChars)
            {
                yield return current;
                current = [];
                size = 0;
            }
            current.Add(t);
            size += t.Text.Length;
        }
        if (current.Count > 0) yield return current;
    }

    /// <summary>Der Auftrag. Fuer Partien WOERTLICH der bisherige (die Tests der Partie-Uebersetzung
    /// pruefen ihn mit); fuer Kurse kommen die Kurs-Regeln dazu.</summary>
    internal static string SystemPrompt(string? from, string to, TranslationSubject subject)
    {
        // Partien: woertlich wie vor dem Umbau (auch bei „und"). Kurse: eine unbekannte Quelle wird nicht
        // als Sprachkuerzel behauptet.
        var source = subject == TranslationSubject.Game || IsKnown(from)
            ? $"from {from} "
            : "from the language it is written in ";
        var head = $"""
            You translate chess annotations {source}to {to}.

            Rules:
            - Translate ONLY the prose. Leave every move, evaluation symbol and coordinate exactly as it
              is ({FigurineNote(to)}). Never add, remove or reorder moves.
            - Keep the author's voice: an annotation is a person explaining a game, not a report.
            - Do not explain, summarise or improve. If a sentence is wrong, it stays wrong.
            - Keep one entry per input entry, with the same ply number.
            """;
        return subject switch
        {
            TranslationSubject.Game => head,
            TranslationSubject.CourseLine => head + "\n" + CourseRules + $"""

                - The entries belong to ONE line of a chess course. Entry {CourseTextSlots.Title} is the line's title and
                  entry {CourseTextSlots.Chapter} its chapter name: these are headings — translate them as short headings.
                  Entries {CourseTextSlots.Comment} and {CourseTextSlots.Intro} introduce the line; the others comment on the move with that ply number.
                """,
            _ => head + "\n" + CourseRules + """

                - Every entry is the name of one chapter of the same chess course — a heading. Translate each as a
                  short heading and name the same thing the same way in every entry.
                """,
        };
    }

    private const string CourseRules = """
        - Game references ("Carlsen - Anand, Chennai 2013"), player names and opening names: use the form that
          is customary in the target language (keep a name as it is when there is no customary form).
        """;

    /// <summary>Eine Quellsprache, mit der sich etwas anfangen laesst — „und" heisst „nicht bestimmbar".</summary>
    private static bool IsKnown(string? lang) =>
        !string.IsNullOrWhiteSpace(lang) && !string.Equals(lang, "und", StringComparison.OrdinalIgnoreCase);

    /// <summary>Die Figurenbuchstaben sind sprachabhaengig — und das ist der Punkt, an dem eine
    /// woertliche Uebersetzung die Zuege unlesbar machen wuerde.</summary>
    private static string FigurineNote(string to) => to switch
    {
        "de" => "German piece letters: K D T L S",
        "fr" => "French piece letters: R D T F C",
        "es" => "Spanish piece letters: R D T A C",
        "it" => "Italian piece letters: R D T A C",
        "nl" => "Dutch piece letters: K D T L P",
        "hu" => "Hungarian piece letters: K V B F H",
        "hr" => "Croatian piece letters: K D T L S",
        _ => "English piece letters: K Q R B N",
    };

    private static string UserPrompt(List<(int Key, string Text)> chunk)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{\"items\":[");
        for (var i = 0; i < chunk.Count; i++)
        {
            sb.Append("  {\"ply\":").Append(chunk[i].Key).Append(",\"text\":")
              .Append(JsonSerializer.Serialize(chunk[i].Text)).Append('}');
            sb.AppendLine(i + 1 < chunk.Count ? "," : "");
        }
        sb.AppendLine("]}");
        return sb.ToString();
    }

    private static List<(int Ply, string Text)> Parse(string json)
    {
        List<(int, string)> result = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return result;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("ply", out var ply) || !item.TryGetProperty("text", out var text))
                    continue;
                var value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value)) result.Add((ply.GetInt32(), value));
            }
        }
        catch (JsonException)
        {
            // Ein unlesbares Ergebnis ist dasselbe wie keines — der Aufrufer bricht ab.
        }
        return result;
    }
}
