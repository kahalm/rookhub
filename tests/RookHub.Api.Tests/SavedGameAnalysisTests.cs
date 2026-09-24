using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Partie analysieren" an der gespeicherten Partie: die Verknuepfung Partie ↔ Analyse, der eigene
/// Ursprung (die Analyse gehoert NICHT in die Liste der Punktepartie-Seite), das Wiederverwenden statt
/// doppelt Rechnens und die Bewertungen fuer die Kurve.
///
/// <para>Gebaut mit der ECHTEN <see cref="GameAnalysisService"/> (Deckel, Engine-Wahl, Einreihen) —
/// eine Attrappe haette genau die Stellen verborgen, an denen die beiden Wege sich beruehren.</para>
/// </summary>
public class SavedGameAnalysisTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GameAnalysisService _analyses;
    private readonly SavedGameService _svc;

    public SavedGameAnalysisTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _analyses = TestServices.GameAnalyses(_db);
        _svc = TestServices.SavedGames(_db, _analyses);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> UserAsync(string name, bool engine = true)
    {
        var user = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        if (engine)
        {
            _db.LichessEngineCredentials.Add(new LichessEngineCredential
            {
                UserId = user.Id, EncryptedToken = "enc", BackgroundEngineIds = $"eei_{name}",
            });
            await _db.SaveChangesAsync();
        }
        return user;
    }

    /// <summary>Verschiedene Partien brauchen verschiedene Namen: das PGN wird aus Zuegen und
    /// Kopfdaten gebaut, und gleicher Text hiesse „dieselbe Partie" (sie wuerde wiederverwendet).</summary>
    private Task<SavedGameDetailDto> SaveAsync(int userId, string externalId = "g-1", string white = "Anna")
        => _svc.SaveAsync(userId, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5", "Nf3", "d6" }, White = white, Black = "Bert",
            Result = "1-0", ExternalId = externalId,
        });

    // ----- Eigene Partie als Seite (/games/{id}, 0.513.0): das Brett startet aus Sicht des Besitzers -----

    [Fact]
    public async Task Get_setztOwnerSide_ausDemPlattformNamenDesProfils_wieBeimTeilenLink()
    {
        var user = await UserAsync("bert");
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, LichessUsername = "Bert" });
        await _db.SaveChangesAsync();
        var saved = await SaveAsync(user.Id);   // Weiß Anna, Schwarz Bert, Quelle lichess

        var detail = await _svc.GetAsync(user.Id, saved.Id);

        Assert.Equal("black", detail!.OwnerSide);
    }

    [Fact]
    public async Task Get_ohneProfilName_keineOwnerSide()
    {
        var user = await UserAsync("anna");
        var saved = await SaveAsync(user.Id);

        var detail = await _svc.GetAsync(user.Id, saved.Id);

        Assert.Null(detail!.OwnerSide);
    }

    private async Task<SavedGame> RowAsync(int id)
    {
        _db.ChangeTracker.Clear();
        return await _db.SavedGames.AsNoTracking().SingleAsync(g => g.Id == id);
    }

    // ===== Anlegen + Verknuepfen ============================================

    /// <summary>Derselbe Weg wie der Einwurf auf der Punktepartie-Seite (feste Tiefe, fuenf Linien),
    /// nur anders etikettiert — und die Partie weiss danach, welche Analyse zu ihr gehoert.</summary>
    [Fact]
    public async Task Analyze_eigenePartie_legtAnalyseMitEigenemUrsprungAn_undVerknuepft()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);

        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.NotNull(result);
        Assert.Null(result!.Reason);
        Assert.False(result.Reused);
        var analysis = await _db.GameAnalyses.AsNoTracking().SingleAsync();
        Assert.Equal(result.Analysis!.Id, analysis.Id);
        Assert.Equal(GameAnalysisOrigin.SavedGame, analysis.Origin);
        // Tiefe 30, nicht die 20 der Punktepartie: hier ist die Bewertung das Ergebnis (seit 0.514.2).
        Assert.Equal(GameAnalysisDefaults.SavedGameTargetDepth, analysis.TargetDepth);
        Assert.Equal(30, analysis.TargetDepth);
        Assert.Equal(GameAnalysisDefaults.MultiPv, analysis.MultiPv);
        Assert.Equal("Anna – Bert", analysis.Title);
        Assert.Equal(owner.Id, analysis.UserId);
        Assert.Equal(analysis.Id, (await RowAsync(game.Id)).GameAnalysisId);
    }

    /// <summary>Zweimal klicken = einmal rechnen. Der zweite Aufruf bekommt dieselbe Analyse zurück.</summary>
    [Fact]
    public async Task Analyze_zweiAufrufeNacheinander_ergebenEINEAnalyse()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);

        var first = await _svc.AnalyzeAsync(owner.Id, game.Id);
        var second = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.False(first!.Reused);
        Assert.True(second!.Reused);
        Assert.Equal(first.Analysis!.Id, second.Analysis!.Id);
        Assert.Equal(1, await _db.GameAnalyses.CountAsync());
    }

    /// <summary>Eine gescheiterte Analyse ist kein Ergebnis — wer erneut klickt, will neu rechnen, und
    /// die Partie zeigt danach auf die NEUE.</summary>
    [Fact]
    public async Task Analyze_gescheiterteVerknuepfte_wirdNichtWiederverwendet_sondernErsetzt()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var first = await _svc.AnalyzeAsync(owner.Id, game.Id);
        var failed = await _db.GameAnalyses.SingleAsync(a => a.Id == first!.Analysis!.Id);
        failed.Status = GameAnalysisStatus.Failed;
        await _db.SaveChangesAsync();

        var second = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.False(second!.Reused);
        Assert.NotEqual(first!.Analysis!.Id, second.Analysis!.Id);
        Assert.Equal(2, await _db.GameAnalyses.CountAsync());
        Assert.Equal(second.Analysis.Id, (await RowAsync(game.Id)).GameAnalysisId);
    }

    /// <summary>Hat der Nutzer dieselbe Partie (gleiches PGN) schon anders rechnen lassen — etwa ueber
    /// die Punktepartie-Seite —, wird DIE genommen und verknuepft, statt die Engine ein zweites Mal
    /// durch dieselben Stellungen zu schicken.</summary>
    [Fact]
    public async Task Analyze_vorhandeneEigeneAnalyseGleichesPgn_wirdWiederverwendetUndVerknuepft()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var earlier = await _analyses.CreateForGuessAsync(owner.Id, new CreateGuessGameRequest { Pgn = game.Pgn });

        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.True(result!.Reused);
        Assert.Equal(earlier.Analysis!.Id, result.Analysis!.Id);
        Assert.Equal(1, await _db.GameAnalyses.CountAsync());
        Assert.Equal(earlier.Analysis.Id, (await RowAsync(game.Id)).GameAnalysisId);
    }

    [Fact]
    public async Task Analyze_eigeneGescheiterteMitGleichemPgn_zaehltNicht()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var earlier = await _analyses.CreateForGuessAsync(owner.Id, new CreateGuessGameRequest { Pgn = game.Pgn });
        (await _db.GameAnalyses.SingleAsync(a => a.Id == earlier.Analysis!.Id)).Status = GameAnalysisStatus.Failed;
        await _db.SaveChangesAsync();

        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.False(result!.Reused);
        Assert.NotEqual(earlier.Analysis!.Id, result.Analysis!.Id);
    }

    /// <summary>Die geteilte Partie darf JEDER Angemeldete rechnen lassen — er bekommt seine eigene
    /// Analyse, die Partie bleibt aber unverknuepft: die oeffentliche Kurve ist die des Besitzers.</summary>
    [Fact]
    public async Task AnalyzeShared_Gast_bekommtEigeneAnalyse_diePartieBleibtUnverknuepft()
    {
        var owner = await UserAsync("owner", engine: false);
        var guest = await UserAsync("guest");
        var game = await SaveAsync(owner.Id);

        var result = await _svc.AnalyzeSharedAsync(guest.Id, game.ShareToken);

        Assert.Null(result!.Reason);
        var analysis = await _db.GameAnalyses.AsNoTracking().SingleAsync();
        Assert.Equal(guest.Id, analysis.UserId);
        Assert.Equal(GameAnalysisOrigin.SavedGame, analysis.Origin);
        Assert.Null((await RowAsync(game.Id)).GameAnalysisId);
    }

    /// <summary>Hat der Besitzer schon rechnen lassen, bekommt auch der Gast DIESE zurueck — die Kurve
    /// steht ja schon auf der Seite.</summary>
    [Fact]
    public async Task AnalyzeShared_verknuepfteDesBesitzers_wirdAuchDemGastZurueckgegeben()
    {
        var owner = await UserAsync("owner");
        var guest = await UserAsync("guest");
        var game = await SaveAsync(owner.Id);
        var byOwner = await _svc.AnalyzeAsync(owner.Id, game.Id);

        var byGuest = await _svc.AnalyzeSharedAsync(guest.Id, game.ShareToken);

        Assert.True(byGuest!.Reused);
        Assert.Equal(byOwner!.Analysis!.Id, byGuest.Analysis!.Id);
        Assert.Equal(1, await _db.GameAnalyses.CountAsync());
    }

    /// <summary>Der Besitzer ueber seinen eigenen Teilen-Link: das ist SEINE Partie — sie wird
    /// verknuepft wie aus der Liste.</summary>
    [Fact]
    public async Task AnalyzeShared_Besitzer_verknuepft()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);

        var result = await _svc.AnalyzeSharedAsync(owner.Id, game.ShareToken);

        Assert.Equal(result!.Analysis!.Id, (await RowAsync(game.Id)).GameAnalysisId);
    }

    [Fact]
    public async Task Analyze_fremdeOderUnbekanntePartie_null()
    {
        var owner = await UserAsync("owner");
        var other = await UserAsync("other");
        var game = await SaveAsync(owner.Id);

        Assert.Null(await _svc.AnalyzeAsync(other.Id, game.Id));
        Assert.Null(await _svc.AnalyzeAsync(owner.Id, game.Id + 999));
        Assert.Null(await _svc.AnalyzeSharedAsync(owner.Id, "gibt-es-nicht"));
        Assert.Empty(await _db.GameAnalyses.ToListAsync());
    }

    /// <summary>Ohne Engine: dieselbe Absage mit Grund wie auf der Punktepartie-Seite — und die Partie
    /// zeigt auf nichts.</summary>
    [Fact]
    public async Task Analyze_ohneEngine_sagtWarum_undVerknuepftNichts()
    {
        var owner = await UserAsync("owner", engine: false);
        var game = await SaveAsync(owner.Id);

        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.Equal(GuessUploadReason.NoEngine, result!.Reason);
        Assert.Null(result.Analysis);
        Assert.Null((await RowAsync(game.Id)).GameAnalysisId);
    }

    /// <summary>Der Deckel der eingeworfenen Partien gilt auch hier — gerechnet wird auf derselben
    /// fremden Rechenzeit.</summary>
    [Fact]
    public async Task Analyze_ueberDemDeckel_sagtWarum()
    {
        var owner = await UserAsync("owner");
        for (var i = 0; i < GameAnalysisDefaults.MaxOpenGuessGamesPerUser; i++)
            Assert.Null((await _svc.AnalyzeAsync(owner.Id, (await SaveAsync(owner.Id, $"g-{i}", $"Spieler {i}")).Id))!.Reason);

        var refused = await _svc.AnalyzeAsync(owner.Id, (await SaveAsync(owner.Id, "g-x", "Spieler x")).Id);

        Assert.Equal(GuessUploadReason.TooManyOpen, refused!.Reason);
    }

    // ===== Nicht bei den Punktepartien ======================================

    /// <summary>Die Liste „Eigene Analysen" (Punktepartie-Seite und Partie-Analysen) zeigt die ueber
    /// die gespeicherte Partie angestossene NICHT — sie ist nur ueber die Partie sichtbar, und die
    /// findet sie ueber die Verknuepfung.</summary>
    [Fact]
    public async Task Analyse_ausDerPartie_stehtNichtInDerListe_aberAnDerPartie()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.Empty(await _analyses.ListAsync(owner.Id));

        var evals = await _svc.GetEvalsAsync(owner.Id, game.Id);
        Assert.Equal(result!.Analysis!.Id, evals!.AnalysisId);
        Assert.NotEqual("none", evals.Status);
    }

    // ===== Bewertungen fuer die Kurve =======================================

    /// <summary>Legt eine Analyse mit gerechneten Zeilen an (1.e4 c5 2.Nf3 d6): Zeile 0 und 1
    /// gerechnet, Zeile 2 aufgegeben, Zeile 3 noch offen.</summary>
    private async Task<GameAnalysis> SeedAnalysisAsync(int userId, string pgn, GameAnalysisStatus status = GameAnalysisStatus.Running)
    {
        var analysis = new GameAnalysis
        {
            UserId = userId, Pgn = pgn, StartFen = "startpos", Status = status, PlyCount = 4,
            TargetDepth = 20, Origin = GameAnalysisOrigin.SavedGame,
        };
        analysis.Positions.Add(new GameAnalysisPosition
        {
            Ply = 0, Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            GameMoveUci = "e2e4", GameMoveSan = "e4", Depth = 20,
            CandidatesJson = """[{"uci":"e2e4","cp":30},{"uci":"d2d4","cp":28}]""",
        });
        analysis.Positions.Add(new GameAnalysisPosition
        {
            Ply = 1, Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
            GameMoveUci = "c7c5", GameMoveSan = "c5", Depth = 19,
            CandidatesJson = """[{"uci":"e7e5","cp":-25},{"uci":"c7c5","cp":-32}]""",
        });
        analysis.Positions.Add(new GameAnalysisPosition
        {
            Ply = 2, Fen = "rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2",
            GameMoveUci = "g1f3", GameMoveSan = "Nf3", CandidatesJson = "[]",
        });
        analysis.Positions.Add(new GameAnalysisPosition
        {
            Ply = 3, Fen = "rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2",
            GameMoveUci = "d7d6", GameMoveSan = "d6",
        });
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();
        return analysis;
    }

    private async Task LinkAsync(int savedGameId, int analysisId)
    {
        var row = await _db.SavedGames.SingleAsync(g => g.Id == savedGameId);
        row.GameAnalysisId = analysisId;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Evals_liefernWeissSicht_Fortschritt_undLassenLueckenWeg()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var analysis = await SeedAnalysisAsync(owner.Id, game.Pgn);
        await LinkAsync(game.Id, analysis.Id);

        var evals = await _svc.GetSharedEvalsAsync(game.ShareToken, callerUserId: null);

        Assert.NotNull(evals);
        Assert.Equal("running", evals!.Status);
        Assert.Equal(analysis.Id, evals.AnalysisId);
        Assert.Equal(4, evals.Total);
        Assert.Equal(3, evals.Analyzed);          // aufgegeben zaehlt als gerechnet, offen nicht
        Assert.Equal(20, evals.TargetDepth);
        Assert.Equal(new[] { 0, 1 }, evals.Plies.Select(p => p.Ply));   // aufgegeben + offen fehlen
        Assert.Equal(30, evals.Plies[0].Cp);
        Assert.Equal(25, evals.Plies[1].Cp);      // Schwarz am Zug → gedreht
        Assert.Equal(32, evals.Plies[1].PlayedCp);
        Assert.Null(evals.Final);                 // letzte Zeile nicht gerechnet
    }

    [Fact]
    public async Task Evals_Final_ausDemGespieltenKandidatenDerLetztenZeile()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var analysis = await SeedAnalysisAsync(owner.Id, game.Pgn, GameAnalysisStatus.Done);
        var last = await _db.GameAnalysisPositions.SingleAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 3);
        last.CandidatesJson = """[{"uci":"d7d6","cp":-40},{"uci":"e7e6","cp":-45}]""";
        await _db.SaveChangesAsync();
        await LinkAsync(game.Id, analysis.Id);

        var evals = await _svc.GetEvalsAsync(owner.Id, game.Id);

        Assert.Equal("done", evals!.Status);
        Assert.Equal(40, evals.Final!.Cp);        // Schwarz am Zug, gespielt −40 → Weiß +40
        Assert.Null(evals.Final.Mate);
    }

    /// <summary>„8 von 47 Stellungen" sagt nicht, wie lange noch — die Restdauer kommt aus den
    /// Zeitstempeln der gerechneten Stellungen DIESER Partie, und nur solange sie laeuft.</summary>
    [Theory]
    [InlineData(GameAnalysisStatus.Running, 1)]
    [InlineData(GameAnalysisStatus.Done, null)]
    public async Task Evals_Restdauer_nurSolangeDieAnalyseLaeuft(GameAnalysisStatus status, int? expected)
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var analysis = await SeedAnalysisAsync(owner.Id, game.Pgn, status);
        var now = DateTime.UtcNow;
        foreach (var p in await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == analysis.Id && p.Ply < 3).ToListAsync())
            p.AnalyzedAt = now.AddMinutes(p.Ply - 3);   // vor 3, 2 und 1 Minute(n)
        await _db.SaveChangesAsync();
        await LinkAsync(game.Id, analysis.Id);

        var evals = await _svc.GetSharedEvalsAsync(game.ShareToken, callerUserId: null);

        // Drei Stellungen in drei Minuten, eine offen → eine Minute.
        Assert.Equal(expected, evals!.EtaMinutes);
    }

    /// <summary>Anonym gibt es NUR die verknuepfte Analyse — ohne Verknuepfung nichts, auch wenn
    /// irgendwer dieselbe Partie gerechnet hat.</summary>
    [Fact]
    public async Task Evals_anonymOhneVerknuepfung_none()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        await SeedAnalysisAsync(owner.Id, game.Pgn);   // gleiches PGN, aber nicht verknuepft

        var evals = await _svc.GetSharedEvalsAsync(game.ShareToken, callerUserId: null);

        Assert.Equal("none", evals!.Status);
        Assert.Null(evals.AnalysisId);
        Assert.Empty(evals.Plies);
    }

    /// <summary>Ein angemeldeter Gast, der die geteilte Partie selbst hat rechnen lassen, sieht SEINE
    /// Kurve — anonym bleibt die Seite leer, weil der Besitzer nicht gerechnet hat.</summary>
    [Fact]
    public async Task Evals_angemeldeterGastMitEigenerAnalyse_bekommtSeine()
    {
        var owner = await UserAsync("owner", engine: false);
        var guest = await UserAsync("guest");
        var game = await SaveAsync(owner.Id);
        var mine = await _svc.AnalyzeSharedAsync(guest.Id, game.ShareToken);

        var asGuest = await _svc.GetSharedEvalsAsync(game.ShareToken, guest.Id);
        var anonymous = await _svc.GetSharedEvalsAsync(game.ShareToken, callerUserId: null);

        Assert.Equal(mine!.Analysis!.Id, asGuest!.AnalysisId);
        Assert.Equal("none", anonymous!.Status);
    }

    /// <summary>Ein gescheiterter eigener Versuch wird dem Gast nicht als Kurve gezeigt.</summary>
    [Fact]
    public async Task Evals_eigeneGescheiterteMitGleichemPgn_none()
    {
        var owner = await UserAsync("owner", engine: false);
        var guest = await UserAsync("guest");
        var game = await SaveAsync(owner.Id);
        await SeedAnalysisAsync(guest.Id, game.Pgn, GameAnalysisStatus.Failed);

        var evals = await _svc.GetSharedEvalsAsync(game.ShareToken, guest.Id);

        Assert.Equal("none", evals!.Status);
    }

    /// <summary>Die Analyse kann geloescht werden, die Partie behaelt ihren Verweis (kein Fremd-
    /// schluessel) — der Leser muss das aushalten: „nichts da" statt eines Wurfs.</summary>
    [Fact]
    public async Task Evals_geloeschteVerknuepfteAnalyse_none_ohneWurf()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);
        Assert.True(await _analyses.DeleteAsync(owner.Id, result!.Analysis!.Id));
        _db.ChangeTracker.Clear();

        var anonymous = await _svc.GetSharedEvalsAsync(game.ShareToken, callerUserId: null);

        Assert.Equal("none", anonymous!.Status);
        Assert.Equal(result.Analysis.Id, (await RowAsync(game.Id)).GameAnalysisId);   // Verweis bleibt stehen
    }

    [Fact]
    public async Task Evals_fremdeOderUnbekanntePartie_null()
    {
        var owner = await UserAsync("owner");
        var other = await UserAsync("other");
        var game = await SaveAsync(owner.Id);

        Assert.Null(await _svc.GetEvalsAsync(other.Id, game.Id));
        Assert.Null(await _svc.GetSharedEvalsAsync("gibt-es-nicht", callerUserId: null));
    }

    // ----- Partienliste: Stand der verknuepften Analyse (0.515.0) -----------------------------------

    [Fact]
    public async Task List_traegtDenStandDerVerknuepftenAnalyse_laufend_ohneGenauigkeit()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        Assert.Null((await _svc.ListAsync(owner.Id)).Single().Analysis);   // noch nichts verknuepft

        await _svc.AnalyzeAsync(owner.Id, game.Id);

        var row = (await _svc.ListAsync(owner.Id)).Single();
        Assert.NotNull(row.Analysis);
        // Der Einwurf reiht sofort den ersten Block ein — die Analyse steht damit schon auf „running".
        Assert.Contains(row.Analysis!.Status, new[] { "pending", "running" });
        Assert.Equal(0, row.Analysis.Analyzed);
        Assert.Equal(4, row.Analysis.Total);
        Assert.Null(row.Analysis.AccuracyWhite);
        Assert.Null(row.Analysis.AccuracyBlack);
    }

    [Fact]
    public async Task List_fertigeAnalyseOhneGenauigkeit_wirdEinmalNachgerechnetUndAbgelegt()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var created = await _svc.AnalyzeAsync(owner.Id, game.Id);
        var analysis = await _db.GameAnalyses.Include(a => a.Positions).SingleAsync(a => a.Id == created!.Analysis!.Id);
        // Wie eine Analyse von vor 0.515.0: fertig, alle Stellungen gerechnet, aber keine Genauigkeit abgelegt.
        foreach (var p in analysis.Positions) p.CandidatesJson = $"[{{\"uci\":\"{p.GameMoveUci}\",\"cp\":0}}]";
        analysis.Status = GameAnalysisStatus.Done;
        analysis.AccuracyWhite = null;
        analysis.AccuracyBlack = null;
        await _db.SaveChangesAsync();

        var row = (await _svc.ListAsync(owner.Id)).Single();
        Assert.Equal("done", row.Analysis!.Status);
        Assert.Equal(4, row.Analysis.Analyzed);
        Assert.Equal(100, row.Analysis.AccuracyWhite!.Value, 3);   // jeder Zug war der Bestzug
        Assert.Equal(100, row.Analysis.AccuracyBlack!.Value, 3);

        var stored = await _db.GameAnalyses.AsNoTracking().SingleAsync(a => a.Id == analysis.Id);
        Assert.Equal(100, stored.AccuracyWhite!.Value, 3);
        Assert.Equal(100, stored.AccuracyBlack!.Value, 3);
    }

    [Fact]
    public async Task List_verweisInsLeere_keinStand_derKnopfKommtZurueck()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var row = await RowAsync(game.Id);
        row.GameAnalysisId = 987654;   // die Analyse gibt es nicht (mehr)
        await _db.SaveChangesAsync();

        Assert.Null((await _svc.ListAsync(owner.Id)).Single().Analysis);
    }
}
