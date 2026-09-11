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
}
