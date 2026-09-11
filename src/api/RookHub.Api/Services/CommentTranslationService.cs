using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Uebersetzt die Anmerkungen einer Partie in eine weitere Sprache und legt sie als eigenen
/// <see cref="CommentSet"/> ab (<see cref="CommentOrigin.Machine"/>).
///
/// <para><b>Die Partie ist die Einheit, nicht der Kommentar.</b> Uebersetzt wird in moeglichst
/// wenigen Fuhren, weil Figurennamen, Eroeffnungsbegriffe und die Anrede sonst innerhalb DERSELBEN
/// Partie wechseln — „Springer" hier, „Pferd" zwei Zuege spaeter. Erst wenn eine Partie zu lang
/// wird, teilt <see cref="ChunkChars"/> sie auf.</para>
///
/// <para><b>Die Quelle bleibt unangetastet.</b> Geschrieben wird ein NEUER Satz; der gelesene
/// (<see cref="CommentOrigin.Source"/>) wird nie ueberschrieben. Und die Uebersetzung sagt, was sie
/// ist: <see cref="CommentSet.Origin"/>, <see cref="CommentSet.TranslatedFrom"/> und das Modell
/// stehen an der Zeile — eine maschinelle Uebersetzung, die sich als die Anmerkung des
/// Grossmeisters ausgibt, ist eine Falschaussage ueber die Quelle.</para>
/// </summary>
public class CommentTranslationService
{
    /// <summary>So viel von der Quelllaenge muss eine Uebersetzung mindestens haben. Deutsch ist
    /// eher laenger als Englisch — liegt das Ergebnis deutlich darunter, fehlt Text.</summary>
    public const double MinLengthShare = 0.7;

    /// <summary>So viele Zeichen gehen hoechstens in EINE Fuhre. Grosszuegig, weil die Einheitlichkeit
    /// der Begriffe an der gemeinsamen Fuhre haengt; die Antwort ist etwa so lang wie die Vorlage.</summary>
    public const int ChunkChars = 8000;

    private readonly AppDbContext _db;
    private readonly IClaudeJsonClient _claude;
    private readonly ILogger<CommentTranslationService> _logger;

    public CommentTranslationService(AppDbContext db, IClaudeJsonClient claude,
        ILogger<CommentTranslationService> logger)
    {
        _db = db;
        _claude = claude;
        _logger = logger;
    }

    /// <summary>True, wenn ueberhaupt uebersetzt werden kann (<c>Anthropic:ApiKey</c> gesetzt).</summary>
    public bool IsAvailable => _claude.IsConfigured;

    /// <summary>
    /// Uebersetzt die Anmerkungen EINER Partie in <paramref name="target"/>.
    /// </summary>
    /// <param name="analysisId">Die Partie (ueber sie wird die Bibliothekszeile gefunden).</param>
    /// <param name="target">ISO-Kuerzel der Zielsprache.</param>
    /// <param name="force">Eine vorhandene MASCHINELLE Uebersetzung ersetzen. Eine Quelle und eine
    /// von Hand gepflegte Fassung werden NIE ersetzt — die waeren nicht wiederherstellbar.</param>
    /// <returns>Wie viele Zeilen geschrieben wurden; 0 = nichts zu tun oder nicht moeglich.</returns>
    public async Task<int> TranslateAsync(int analysisId, string target, bool force = false,
        CancellationToken ct = default)
    {
        var libraryGameId = await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.Id == analysisId).Select(g => g.LibraryGameId).FirstOrDefaultAsync(ct);
        return await RunAsync(libraryGameId, libraryGameId is null ? analysisId : null, target, force, ct);
    }

    /// <summary>Dasselbe fuer eine Partie des Rohbestands, die noch keine Analyse hat — der Text
    /// laesst sich lange vor der Engine aufbereiten.</summary>
    public Task<int> TranslateLibraryGameAsync(int libraryGameId, string target, bool force = false,
        CancellationToken ct = default)
        => RunAsync(libraryGameId, null, target, force, ct);

    private async Task<int> RunAsync(int? libraryGameId, int? analysisId, string target, bool force,
        CancellationToken ct)
    {
        if (!_claude.IsConfigured) return 0;
        target = target.Trim().ToLowerInvariant();
        if (target.Length is 0 or > 8) return 0;

        var query = _db.CommentSets.Include(s => s.Texts);
        var sets = libraryGameId is int lib
            ? await query.Where(s => s.LibraryGameId == lib).ToListAsync(ct)
            : await query.Where(s => s.GameAnalysisId == analysisId).ToListAsync(ct);
        if (sets.Count == 0) return 0;

        var existing = sets.FirstOrDefault(s => s.Language == target);
        if (existing is not null)
        {
            // Nur die eigene Maschinenfassung wird ersetzt, und auch die nur auf Zuruf.
            if (!force || existing.Origin != CommentOrigin.Machine) return 0;
            _db.CommentTexts.RemoveRange(existing.Texts);
            _db.CommentSets.Remove(existing);
            await _db.SaveChangesAsync(ct);
            sets.Remove(existing);
        }

        // Uebersetzt wird aus der QUELLE, nie aus einer Uebersetzung: zweimal uebersetzt wird aus
        // „Springer" ein „Pferd" und aus einer Einschaetzung eine Behauptung.
        var source = sets.Where(s => s.Origin == CommentOrigin.Source)
                         .OrderByDescending(s => s.Texts.Count)
                         .FirstOrDefault() ?? sets[0];
        if (source.Texts.Count == 0) return 0;

        var translated = new Dictionary<int, string>();
        foreach (var chunk in Chunks(source.Texts.OrderBy(t => t.Ply).ToList()))
        {
            var json = await _claude.TranslateCommentsJsonAsync(
                SystemPrompt(source.Language, target), UserPrompt(chunk), ct);
            if (json is null)
            {
                _logger.LogWarning("Uebersetzung der Partie {Id} nach {Lang} abgebrochen.",
                    libraryGameId ?? analysisId, target);
                return 0;   // lieber gar kein Satz als ein halber
            }
            foreach (var (ply, text) in Parse(json))
                translated[ply] = text;
        }
        if (translated.Count == 0) return 0;

        // DIE LAENGE PRUEFEN, bevor irgendetwas gespeichert wird. Am 2026-09-11 an echten Partien
        // erlebt: ein sparsameres Modell lieferte 18 bis 53 Prozent der Quelllaenge — Saetze mitten
        // im Absatz abgeschnitten, und zwar lautlos. Struktur und Zuege stimmten dabei, es fehlte
        // nur Prosa, und genau die ist die Lehre der Partie.
        //
        // Die Grenze liegt bei 70 Prozent und nicht hoeher, weil manche Quell-Saetze selbst noch
        // zweisprachig sind: dort wirft die Uebersetzung die doppelte Haelfte zu Recht weg
        // (gemessen 53 bis 55 Prozent). Lieber ein paar Faelle von Hand nachsehen als stumme
        // Luecken im Bestand.
        var quellLaenge = source.Texts.Sum(t => t.Text.Length);
        var zielLaenge = translated.Values.Sum(t => t.Length);
        if (quellLaenge > 0 && zielLaenge < quellLaenge * MinLengthShare)
        {
            _logger.LogWarning(
                "Uebersetzung der Partie {Id} nach {Lang} verworfen: {Anteil} % der Quelllaenge — es fehlt Text.",
                libraryGameId ?? analysisId, target, zielLaenge * 100 / quellLaenge);
            return 0;
        }

        var set = new CommentSet
        {
            LibraryGameId = source.LibraryGameId,
            GameAnalysisId = source.GameAnalysisId,
            Language = target,
            Origin = CommentOrigin.Machine,
            TranslatedFrom = source.Language,
            Model = ModelName,
            Status = CommentSetStatus.Ready,
        };
        foreach (var (ply, text) in translated.OrderBy(t => t.Key))
            set.Texts.Add(new CommentText { Ply = ply, Text = text });
        _db.CommentSets.Add(set);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Partie {Id}: {Count} Anmerkungen nach {Lang} uebersetzt.",
            libraryGameId ?? analysisId, set.Texts.Count, target);
        return set.Texts.Count;
    }

    /// <summary>Womit uebersetzt wurde — steht an jedem Satz, damit ein spaeteres Modell gezielt
    /// nachbessern kann. Kommt vom Client, weil nur der weiss, was tatsaechlich gelaufen ist.</summary>
    public string ModelName => _claude.TranslationModel;

    private static IEnumerable<List<CommentText>> Chunks(List<CommentText> texts)
    {
        var current = new List<CommentText>();
        var size = 0;
        foreach (var t in texts)
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

    private static string SystemPrompt(string from, string to) =>
        $"""
        You translate chess annotations from {from} to {to}.

        Rules:
        - Translate ONLY the prose. Leave every move, evaluation symbol and coordinate exactly as it
          is ({FigurineNote(to)}). Never add, remove or reorder moves.
        - Keep the author's voice: an annotation is a person explaining a game, not a report.
        - Do not explain, summarise or improve. If a sentence is wrong, it stays wrong.
        - Keep one entry per input entry, with the same ply number.
        """;

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

    private static string UserPrompt(List<CommentText> chunk)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{\"items\":[");
        for (var i = 0; i < chunk.Count; i++)
        {
            sb.Append("  {\"ply\":").Append(chunk[i].Ply).Append(",\"text\":")
              .Append(JsonSerializer.Serialize(chunk[i].Text)).Append('}');
            sb.AppendLine(i + 1 < chunk.Count ? "," : "");
        }
        sb.AppendLine("]}");
        return sb.ToString();
    }

    private static IEnumerable<(int Ply, string Text)> Parse(string json)
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
