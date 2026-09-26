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
    /// <summary>So viel von der Quelllaenge muss eine Uebersetzung mindestens haben (siehe
    /// <see cref="CommentTranslator.MinLengthShare"/> — der Kern, den Partien und Kurse teilen).</summary>
    public const double MinLengthShare = CommentTranslator.MinLengthShare;

    /// <summary>So viele Zeichen gehen hoechstens in EINE Fuhre (<see cref="CommentTranslator.ChunkChars"/>).</summary>
    public const int ChunkChars = CommentTranslator.ChunkChars;

    private readonly AppDbContext _db;
    private readonly IClaudeJsonClient _claude;
    private readonly ILogger<CommentTranslationService> _logger;
    /// <summary>Portionen, Auftrag, Figurenbuchstaben, Laengen- und Sprachpruefung — seit 0.547.0 in
    /// <see cref="CommentTranslator"/>, mit dem Logger DIESES Dienstes (die Kategorie bleibt).</summary>
    private readonly CommentTranslator _translator;

    public CommentTranslationService(AppDbContext db, IClaudeJsonClient claude,
        ILogger<CommentTranslationService> logger)
    {
        _db = db;
        _claude = claude;
        _logger = logger;
        _translator = new CommentTranslator(claude, logger);
    }

    /// <summary>True, wenn ueberhaupt uebersetzt werden kann (<c>Anthropic:TextApiKey</c> gesetzt — nicht der Konto-Schluessel).</summary>
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

    /// <summary>
    /// Welche Partien des Rohbestands als naechste uebersetzt werden (<c>tools/LibraryImport translate --library</c>):
    /// kommentiert, weder aussortiert noch Dublette, ohne Satz in der Zielsprache — die BESTEN zuerst (Note,
    /// kommentierte Halbzuege, Textmenge; dieselbe Reihenfolge wie <c>comments --library</c>). Ob die Partie
    /// schon Quell-Saetze hat, spielt keine Rolle: der Lauf legt sie selbst an. Auf dem ganzen Bestand dauert
    /// die Uebersetzung Tage — was die Punktepartie zuerst zeigt, soll zuerst fertig sein.
    ///
    /// <para>Partien, deren Quellsprache beim Einlesen nicht zu bestimmen war (<c>Languages = "und"</c>),
    /// bleiben aussen vor: das sind im Bestand 40 627 Stueck mit im Schnitt 183 Zeichen — Zeitangaben,
    /// Auswertungen, Markup, kaum je ein Satz Prosa. Ein Modellaufruf je Stueck waere der halbe Bestand
    /// fuer ein Ergebnis, das niemand liest. <paramref name="includeUndetermined"/> nimmt sie mit.</para>
    /// </summary>
    public static Task<List<int>> LibraryCandidatesAsync(AppDbContext db, string target, int take,
        bool includeUndetermined = false, CancellationToken ct = default)
    {
        target = target.Trim().ToLowerInvariant();
        return db.LibraryGames.AsNoTracking()
            .Where(g => g.CommentedPlies > 0
                        && g.Status != LibraryGameStatus.Rejected && g.Status != LibraryGameStatus.Duplicate)
            // `Languages == null` gehoert ausdruecklich dazu: in SQL ist `NULL <> 'und'` nicht wahr,
            // sondern NULL — ohne die erste Haelfte fielen Partien ohne Sprachangabe lautlos heraus.
            .Where(g => includeUndetermined || g.Languages == null || g.Languages != "und")
            .Where(g => !db.CommentSets.Any(s => s.LibraryGameId == g.Id && s.Language == target))
            .OrderByDescending(g => g.Score)
            .ThenByDescending(g => g.CommentedPlies)
            .ThenByDescending(g => g.CommentChars)
            .ThenBy(g => g.Id)
            .Select(g => g.Id)
            .Take(take)
            .ToListAsync(ct);
    }

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

        var translated = await _translator.TranslateAsync(
            source.Texts.OrderBy(t => t.Ply).Select(t => (t.Ply, t.Text)).ToList(),
            source.Language, target, TranslationSubject.Game, libraryGameId ?? analysisId, ct);
        // null = abgebrochen/verworfen (der Kern hat es geloggt), leer = nichts Brauchbares — beides schreibt nichts.
        if (translated is null || translated.Count == 0) return 0;

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
}
