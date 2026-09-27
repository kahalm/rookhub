using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Kommentare einer Partie, getrennt vom PGN und nach Sprachen sortiert. Geprueft wird vor
/// allem, was die Umschaltung braucht: dass beide Sprachen ankommen, dass eine Luecke in der
/// gewaehlten Sprache aus der QUELLE gefuellt wird (statt leer zu bleiben), und dass der Altbestand
/// ohne Saetze weiterhin seine Kommentare aus dem Partietext bekommt.
/// </summary>
public class CommentSetServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CommentSetService _svc;

    public CommentSetServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CommentSetService(_db, NullLogger<CommentSetService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Ein Zweizeiler, wie ihn der Export liefert: je Zug ein Block mit beiden Sprachen
    /// hintereinander.</summary>
    private const string BilingualPgn = """
        [White "A"]
        [Black "B"]

        1. e4 {This is the main move and the position is good for White. Dies ist der Hauptzug und
        die Stellung ist gut fuer Weiss.} e5 {Black answers in the same way, and that is the most
        popular move here. Schwarz antwortet genauso, und das ist hier der beliebteste Zug.} 2. Nf3 *
        """;

    private async Task<int> GameAsync(string pgn, string? languages = "en,de", bool library = true)
    {
        int? libraryId = null;
        if (library)
        {
            var row = new LibraryGame { Pgn = pgn, Languages = languages };
            _db.LibraryGames.Add(row);
            await _db.SaveChangesAsync();
            libraryId = row.Id;
        }
        var analysis = new GameAnalysis
        {
            UserId = 1, Pgn = pgn, StartFen = "startpos", TargetDepth = 20, MultiPv = 5,
            PlyCount = 3, LibraryGameId = libraryId,
        };
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();
        return analysis.Id;
    }

    [Fact]
    public async Task EnsureSource_zerlegtDenZweisprachigenBlock()
    {
        var id = await GameAsync(BilingualPgn);

        var created = await _svc.EnsureSourceAsync(id);

        Assert.Equal(2, created);
        var sets = await _db.CommentSets.Include(s => s.Texts).ToListAsync();
        Assert.Equal(2, sets.Count);
        Assert.All(sets, s => Assert.Equal(CommentOrigin.Source, s.Origin));
        // Beide haengen an der BIBLIOTHEKSZEILE, nicht an der Analyse — sonst uebersetzte man
        // dieselbe Partie fuer jeden Anforderer neu.
        Assert.All(sets, s => Assert.NotNull(s.LibraryGameId));
        Assert.All(sets, s => Assert.Null(s.GameAnalysisId));

        var de = sets.Single(s => s.Language == "de");
        Assert.Contains(de.Texts, t => t.Text.Contains("Hauptzug"));
        Assert.DoesNotContain(de.Texts, t => t.Text.Contains("main move"));
    }

    /// <summary>Zweimal aufgerufen entstehen nicht zwei Saetze je Sprache — die Anforderung
    /// derselben Partie durch zwei Leute ist der Normalfall.</summary>
    [Fact]
    public async Task EnsureSource_istWiederholbar()
    {
        var id = await GameAsync(BilingualPgn);

        Assert.Equal(2, await _svc.EnsureSourceAsync(id));
        Assert.Equal(0, await _svc.EnsureSourceAsync(id));
        Assert.Equal(2, await _db.CommentSets.CountAsync());
    }

    [Fact]
    public async Task ForAnalysis_liefertDieGewaehlteSprache()
    {
        var id = await GameAsync(BilingualPgn);
        await _svc.EnsureSourceAsync(id);

        var comments = await _svc.ForAnalysisAsync(id, "de");

        Assert.Equal("de", comments.Language);
        Assert.Equal(["de", "en"], comments.Languages);
        Assert.Contains("Hauptzug", comments.ByPly[0].Text);
    }

    /// <summary>Fehlt ein Halbzug in der gewaehlten Sprache, tritt die Quelle ein — und sagt es.
    /// Eine Luecke waere die schlechtere Antwort: der Kommentar ist die Lehre der Partie.</summary>
    [Fact]
    public async Task ForAnalysis_fuelltLueckenAusDerQuelle()
    {
        var id = await GameAsync(BilingualPgn);
        await _svc.EnsureSourceAsync(id);
        var de = await _db.CommentSets.Include(s => s.Texts).SingleAsync(s => s.Language == "de");
        _db.CommentTexts.Remove(de.Texts.Single(t => t.Ply == 1));
        await _db.SaveChangesAsync();

        var comments = await _svc.ForAnalysisAsync(id, "de");

        Assert.Equal("de", comments.ByPly[0].Language);
        Assert.Equal("en", comments.ByPly[1].Language);   // eingesprungen
        Assert.Contains("popular move", comments.ByPly[1].Text);
    }

    /// <summary>Eine unbekannte Sprache ist kein Fehler: es kommt die Quelle.</summary>
    [Fact]
    public async Task ForAnalysis_unbekannteSprache_faelltAufDieQuelleZurueck()
    {
        var id = await GameAsync(BilingualPgn);
        await _svc.EnsureSourceAsync(id);

        var comments = await _svc.ForAnalysisAsync(id, "hr");

        Assert.NotNull(comments.Language);
        Assert.NotEmpty(comments.ByPly);
    }

    /// <summary>Der Altbestand hat keine Saetze — und bekommt seine Kommentare weiterhin aus dem
    /// Partietext. Ein Umbau, der die vorhandenen Partien stumm macht, waere keiner.</summary>
    [Fact]
    public async Task ForAnalysis_ohneSaetze_liestDasPgn()
    {
        var id = await GameAsync(BilingualPgn);

        var comments = await _svc.ForAnalysisAsync(id, "de");

        Assert.Empty(comments.Languages);
        Assert.Null(comments.Language);
        Assert.Contains("main move", comments.ByPly[0].Text);
    }

    /// <summary>Eine selbst eingeworfene Partie hat keine Bibliothekszeile — dann haengen die
    /// Saetze an der Analyse.</summary>
    [Fact]
    public async Task EnsureSource_ohneBibliothekszeile_haengtAnDerAnalyse()
    {
        var id = await GameAsync(BilingualPgn, library: false);

        await _svc.EnsureSourceAsync(id);

        var sets = await _db.CommentSets.ToListAsync();
        Assert.NotEmpty(sets);
        Assert.All(sets, s => Assert.Equal(id, s.GameAnalysisId));
    }

    [Fact]
    public async Task EnsureSource_ohneKommentare_legtNichtsAn()
    {
        var id = await GameAsync("[White \"A\"]\n\n1. e4 e5 2. Nf3 *", languages: null);

        Assert.Equal(0, await _svc.EnsureSourceAsync(id));
        Assert.Empty(await _db.CommentSets.ToListAsync());
    }

    // ── Nachzug: zweisprachig, aber als einsprachig vermerkt (0.560.1) ──────────────────────────

    private async Task<int> LibraryRowAsync(string pgn, string languages)
    {
        var row = new LibraryGame { Pgn = pgn, Languages = languages };
        _db.LibraryGames.Add(row);
        await _db.SaveChangesAsync();
        return row.Id;
    }

    private async Task AddSetAsync(int libraryGameId, string language, CommentOrigin origin, string text)
    {
        var set = new CommentSet { LibraryGameId = libraryGameId, Language = language, Origin = origin };
        set.Texts.Add(new CommentText { Ply = 0, Text = text });
        _db.CommentSets.Add(set);
        await _db.SaveChangesAsync();
    }

    /// <summary>Der Fall vom 2026-09-27 (Partie 108403): als „en" vermerkt, die Quelle traegt beide Sprachen, und die
    /// maschinelle deutsche Uebersetzung entstand aus dem gemischten Text. Danach: zwei Quell-Saetze, keine Maschine.</summary>
    [Fact]
    public async Task Resplit_zerlegtZweisprachigeAlsEnVermerktePartie()
    {
        var id = await LibraryRowAsync(BilingualPgn, "en");
        Assert.Equal(1, await _svc.EnsureSourceForLibraryAsync(id));   // heute: EIN gemischter en-Satz
        await AddSetAsync(id, "de", CommentOrigin.Machine, "Maschinell aus dem Gemisch");

        var r = await _svc.ResplitLibraryAsync(id);

        Assert.Equal(ResplitStatus.Applied, r.Status);
        Assert.Equal("en,de", r.Languages);
        Assert.Equal(1, r.RemovedMachineSets);
        Assert.Equal("en,de", (await _db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == id)).Languages);
        var sets = await _db.CommentSets.AsNoTracking().Include(s => s.Texts).Where(s => s.LibraryGameId == id).ToListAsync();
        Assert.Equal(2, sets.Count);
        Assert.All(sets, s => Assert.Equal(CommentOrigin.Source, s.Origin));
        var en = sets.Single(s => s.Language == "en");
        var de = sets.Single(s => s.Language == "de");
        Assert.Contains(en.Texts, t => t.Text.Contains("main move"));
        Assert.DoesNotContain(en.Texts, t => t.Text.Contains("Hauptzug"));
        Assert.Contains(de.Texts, t => t.Text.Contains("Hauptzug"));
    }

    /// <summary>Ein englischer Kommentar mit EINEM deutschen Zitat ist keine zweisprachige Partie — ein winziger deutscher
    /// Quell-Satz sperrte sie sonst fuer die deutsche Uebersetzung.</summary>
    [Fact]
    public async Task Resplit_englischMitEinemDeutschenSatz_bleibtEinsprachig()
    {
        const string pgn = """
            [White "A"]
            [Black "B"]

            1. e4 {This is the main move and the position is good for White, with a better game now.} e5 {Black
            answers in the same way and that is the most popular move after this.} 2. Nf3 {The knight move is
            very natural and it has been played in this position a lot.} Nc6 {Here the black knight defends the
            pawn and the game is now balanced.} 3. Bb5 {The famous Spanish move, and a quote: Das ist der beste Zug
            und die Stellung ist sehr gut.} *
            """;
        var id = await LibraryRowAsync(pgn, "en");
        await AddSetAsync(id, "de", CommentOrigin.Machine, "Bleibt stehen");

        var r = await _svc.ResplitLibraryAsync(id);

        Assert.Equal(ResplitStatus.NotBilingual, r.Status);
        Assert.Equal("en", (await _db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == id)).Languages);
        Assert.Single(await _db.CommentSets.Where(s => s.LibraryGameId == id).ToListAsync());
    }

    [Fact]
    public async Task Resplit_Probelauf_schreibtNichts()
    {
        var id = await LibraryRowAsync(BilingualPgn, "en");
        await _svc.EnsureSourceForLibraryAsync(id);
        await AddSetAsync(id, "de", CommentOrigin.Machine, "Maschinell");

        var r = await _svc.ResplitLibraryAsync(id, dryRun: true);

        Assert.Equal(ResplitStatus.WouldApply, r.Status);
        Assert.Equal(1, r.RemovedMachineSets);
        Assert.Equal("en", (await _db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == id)).Languages);
        Assert.Equal(2, await _db.CommentSets.CountAsync(s => s.LibraryGameId == id));
    }

    /// <summary>Eine von Hand gepflegte Fassung ist nicht wiederherstellbar — sie verhindert den Umbau.</summary>
    [Fact]
    public async Task Resplit_vonHandGepflegteFassung_verhindertUmbau()
    {
        var id = await LibraryRowAsync(BilingualPgn, "en");
        await _svc.EnsureSourceForLibraryAsync(id);
        await AddSetAsync(id, "de", CommentOrigin.Human, "Von Hand");

        var r = await _svc.ResplitLibraryAsync(id);

        Assert.Equal(ResplitStatus.Conflict, r.Status);
        Assert.Equal("en", (await _db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == id)).Languages);
        Assert.Equal(2, await _db.CommentSets.CountAsync(s => s.LibraryGameId == id));
    }

    /// <summary>Ohne Saetze wird nur die Sprache korrigiert — die Saetze entstehen beim ersten Bedarf, dann zerlegt.</summary>
    [Fact]
    public async Task Resplit_ohneSaetze_setztNurDieSprachen()
    {
        var id = await LibraryRowAsync(BilingualPgn, "en");

        var r = await _svc.ResplitLibraryAsync(id);

        Assert.Equal(ResplitStatus.Applied, r.Status);
        Assert.Equal(0, await _db.CommentSets.CountAsync());
        Assert.Equal(2, await _svc.EnsureSourceForLibraryAsync(id));
    }

    [Fact]
    public async Task Resplit_schonZweisprachig_bleibtUnangetastet()
    {
        var id = await LibraryRowAsync(BilingualPgn, "en,de");

        Assert.Equal(ResplitStatus.AlreadyBilingual, (await _svc.ResplitLibraryAsync(id)).Status);
    }
}
