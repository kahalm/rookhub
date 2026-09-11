using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>Ein Kommentar, wie er ausgeliefert wird: der Text und die Sprache, in der er
/// TATSAECHLICH vorliegt — die kann von der gewuenschten abweichen, wenn ein Halbzug in der
/// gewaehlten Sprache keinen Text hat.</summary>
public readonly record struct GameComment(string Text, string Language);

/// <summary>Die Kommentare einer Partie in EINER Sprache, samt der Liste dessen, was es sonst gibt.</summary>
public sealed record GameComments(
    IReadOnlyDictionary<int, GameComment> ByPly,
    IReadOnlyList<string> Languages,
    string? Language)
{
    public static readonly GameComments Empty =
        new(new Dictionary<int, GameComment>(), [], null);
}

/// <summary>
/// Die Zug-Kommentare einer Partie — mehrsprachig, getrennt vom PGN abgelegt
/// (<see cref="CommentSet"/>).
///
/// <para><b>Angelegt wird beim ersten Bedarf, nicht auf Vorrat.</b> Der Rohbestand hat 94 898
/// Partien mit Kommentaren; sie alle zu zerlegen waeren Millionen Zeilen fuer Partien, die nie
/// jemand spielt. Gebaut wird deshalb genau dann, wenn eine Partie zum Rechnen eingereiht wird —
/// ab da kann sie gespielt werden.</para>
///
/// <para><b>Der Rueckfall bleibt das PGN.</b> Fuer alles, was vor diesem Umbau angelegt wurde (und
/// fuer eine Partie, deren Zerlegung nichts hergab), liest <see cref="ForAnalysisAsync"/> die
/// Kommentare weiterhin direkt aus dem Partietext. Ein Umbau, der den Altbestand stumm macht,
/// waere keiner.</para>
/// </summary>
public class CommentSetService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CommentSetService> _logger;

    public CommentSetService(AppDbContext db, ILogger<CommentSetService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Die Kommentare einer Analyse in der gewuenschten Sprache.
    ///
    /// <para>Fehlt ein Halbzug in der gewaehlten Sprache, tritt der QUELL-Satz an seine Stelle —
    /// mit seiner eigenen Sprachangabe, damit die Anzeige es sagen kann. Eine Luecke waere die
    /// schlechtere Antwort: der Kommentar ist die Lehre der Partie, und „auf Deutsch gibt es hier
    /// nichts" hilft niemandem weiter.</para>
    /// </summary>
    public async Task<GameComments> ForAnalysisAsync(int analysisId, string? wanted,
        CancellationToken ct = default)
    {
        var sets = await LoadSetsAsync(analysisId, ct);
        if (sets.Count == 0) return await FromPgnAsync(analysisId, ct);

        // Die Quelle ist der Rueckhalt: sie hat die meisten Zeilen und ist keine Uebersetzung.
        var source = sets.Where(s => s.Origin == CommentOrigin.Source)
                         .OrderByDescending(s => s.Texts.Count)
                         .FirstOrDefault() ?? sets[0];
        var chosen = sets.FirstOrDefault(s => string.Equals(s.Language, wanted, StringComparison.OrdinalIgnoreCase))
                     ?? source;

        var byPly = new Dictionary<int, GameComment>();
        foreach (var t in chosen.Texts)
            byPly[t.Ply] = new GameComment(t.Text, chosen.Language);
        if (!ReferenceEquals(chosen, source))
            foreach (var t in source.Texts)
                if (!byPly.ContainsKey(t.Ply))
                    byPly[t.Ply] = new GameComment(t.Text, source.Language);

        var languages = sets.Select(s => s.Language).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.Ordinal).ToList();
        return new GameComments(byPly, languages, chosen.Language);
    }

    /// <summary>
    /// Legt die Quell-Saetze einer Analyse an, falls es noch keine gibt. Idempotent — und still,
    /// wenn die Partie gar keine Kommentare hat.
    /// </summary>
    /// <returns>Wie viele Saetze entstanden sind (0 = es gab schon welche oder es gibt nichts).</returns>
    public async Task<int> EnsureSourceAsync(int analysisId, CancellationToken ct = default)
    {
        var head = await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.Id == analysisId)
            .Select(g => new { g.Id, g.Pgn, g.LibraryGameId })
            .FirstOrDefaultAsync(ct);
        if (head is null) return 0;
        return await BuildAsync(head.LibraryGameId, head.LibraryGameId is null ? analysisId : null,
            head.Pgn, ct);
    }

    /// <summary>
    /// Dasselbe fuer eine Partie, die NOCH KEINE Analyse hat: der Text laesst sich lange vor der
    /// Engine aufbereiten, und eine angeforderte Partie ist damit sofort in beiden Sprachen da,
    /// statt erst nach einer halben Stunde Rechnen.
    /// </summary>
    public async Task<int> EnsureSourceForLibraryAsync(int libraryGameId, CancellationToken ct = default)
    {
        var pgn = await _db.LibraryGames.AsNoTracking()
            .Where(g => g.Id == libraryGameId).Select(g => g.Pgn).FirstOrDefaultAsync(ct);
        return pgn is null ? 0 : await BuildAsync(libraryGameId, null, pgn, ct);
    }

    private async Task<int> BuildAsync(int? libraryGameId, int? analysisId, string? pgn,
        CancellationToken ct)
    {
        var exists = libraryGameId is int lib
            ? await _db.CommentSets.AnyAsync(s => s.LibraryGameId == lib, ct)
            : await _db.CommentSets.AnyAsync(s => s.GameAnalysisId == analysisId, ct);
        if (exists) return 0;

        var comments = ExtractComments(pgn);
        if (comments.Count == 0) return 0;

        var languages = await LanguagesOfAsync(libraryGameId, pgn, ct);
        var byLanguage = SplitAll(comments, languages);
        if (byLanguage.Count == 0) return 0;

        foreach (var (lang, texts) in byLanguage)
        {
            var set = new CommentSet
            {
                LibraryGameId = libraryGameId,
                GameAnalysisId = analysisId,
                Language = lang,
                Origin = CommentOrigin.Source,
                Status = CommentSetStatus.Ready,
            };
            // Jetzt steht die Sprache fest — also werden hier die Figurenzeichen aufgeloest:
            // ein Springer heisst englisch N und deutsch S, und ohne die ChessBase-Schrift ist das
            // Zeichen fuer jeden Leser ein Loch mitten im Satz.
            foreach (var (ply, text) in texts.OrderBy(t => t.Key))
                set.Texts.Add(new CommentText { Ply = ply, Text = Figurines.Apply(text, lang) });
            _db.CommentSets.Add(set);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Zwei Anforderungen derselben Bibliothekspartie koennen sich ueberholen; der
            // eindeutige Index faengt das ab, und der Verlierer braucht nichts zu tun.
            _logger.LogDebug(ex, "Kommentar-Saetze fuer {Owner} lagen schon vor.",
                libraryGameId is int l ? $"Bibliothekspartie {l}" : $"Analyse {analysisId}");
            return 0;
        }
        return byLanguage.Count;
    }

    /// <summary>Die Saetze zu dieser Analyse — ueber die Bibliothekszeile, wo es eine gibt.</summary>
    private async Task<List<CommentSet>> LoadSetsAsync(int analysisId, CancellationToken ct)
    {
        var libraryGameId = await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.Id == analysisId)
            .Select(g => g.LibraryGameId)
            .FirstOrDefaultAsync(ct);

        // Die Fallunterscheidung steht in C# und nicht als `?:` in der Abfrage: so wird daraus ein
        // einfaches Gleich auf einer indizierten Spalte statt eines CASE ueber die ganze Tabelle.
        var query = _db.CommentSets.AsNoTracking().Include(s => s.Texts);
        return libraryGameId is int lib
            ? await query.Where(s => s.LibraryGameId == lib).ToListAsync(ct)
            : await query.Where(s => s.GameAnalysisId == analysisId).ToListAsync(ct);
    }

    /// <summary>Der Rueckfall auf das PGN — das Verhalten vor den Kommentar-Saetzen.</summary>
    private async Task<GameComments> FromPgnAsync(int analysisId, CancellationToken ct)
    {
        var pgn = await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.Id == analysisId)
            .Select(g => g.Pgn)
            .FirstOrDefaultAsync(ct);
        var comments = ExtractComments(pgn);
        if (comments.Count == 0) return GameComments.Empty;

        var byPly = comments.ToDictionary(c => c.Key, c => new GameComment(c.Value, string.Empty));
        return new GameComments(byPly, [], null);
    }

    /// <summary>Die Sprachen der Partie: was der Rohbestand vermerkt hat, sonst frisch gemessen.</summary>
    private async Task<List<string>> LanguagesOfAsync(int? libraryGameId, string? pgn, CancellationToken ct)
    {
        string? csv = null;
        if (libraryGameId is int id)
            csv = await _db.LibraryGames.AsNoTracking()
                .Where(g => g.Id == id).Select(g => g.Languages).FirstOrDefaultAsync(ct);
        csv ??= CommentLanguage.Detect(CommentLanguage.CommentText(pgn));

        var list = (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length is > 0 and <= 8)
            .ToList();
        // „und" heisst „nicht bestimmbar" — als SPRACHE taugt das nicht, aber der Text ist da und
        // soll ausgeliefert werden. Er bekommt diesen Namen und keine erfundene Sprache.
        return list.Count > 0 ? list : ["und"];
    }

    /// <summary>Jeden Block zerlegen und nach Sprache einsortieren.</summary>
    private static Dictionary<string, Dictionary<int, string>> SplitAll(
        Dictionary<int, string> comments, IReadOnlyList<string> languages)
    {
        var byLanguage = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
        foreach (var (ply, text) in comments)
        {
            foreach (var (lang, part) in CommentSplit.Split(text, languages))
            {
                if (!byLanguage.TryGetValue(lang, out var map))
                    byLanguage[lang] = map = new Dictionary<int, string>();
                map[ply] = part;
            }
        }
        return byLanguage;
    }

    private static Dictionary<int, string> ExtractComments(string? pgn)
    {
        if (string.IsNullOrEmpty(pgn) || !pgn.Contains('{')) return new Dictionary<int, string>();
        var game = PgnParser.SplitGames(pgn).FirstOrDefault();
        if (game.MoveText is null) return new Dictionary<int, string>();
        return PgnParser.ExtractMoveComments(game.MoveText) ?? new Dictionary<int, string>();
    }
}
