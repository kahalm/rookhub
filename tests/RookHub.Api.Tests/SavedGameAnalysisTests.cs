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
        // Zwei Durchgaenge (0.523.0): erst schnell (Tiefe 20, eine Linie — Kurve und Fehler stehen nach Minuten),
        // dann im Hintergrund die Vertiefung mit der Tiefe der gespeicherten Partien (25) und fuenf Linien.
        Assert.Equal(GameAnalysisDefaults.SavedGameFastDepth, analysis.TargetDepth);
        Assert.Equal(20, analysis.TargetDepth);
        Assert.Equal(1, analysis.MultiPv);
        Assert.Equal(GameAnalysisDefaults.SavedGameTargetDepth, analysis.RefineDepth);
        Assert.Equal(30, analysis.RefineDepth);
        Assert.Equal(GameAnalysisDefaults.MultiPv, analysis.RefineMultiPv);
        Assert.Equal("Anna – Bert", analysis.Title);
        Assert.Equal(owner.Id, analysis.UserId);
        Assert.Equal(analysis.Id, (await RowAsync(game.Id)).GameAnalysisId);
    }

    /// <summary>0.666.0: Liga + Jahrgang der Vereinspartie gehen in die Kopie — und ein später verbundenes Altstück bekommt sie
    /// nachgetragen, ohne etwas zu überschreiben, das der Nutzer selbst eingetragen hat.</summary>
    [Fact]
    public async Task Kopie_aus_der_Vereinsdb_uebernimmt_Liga_und_Jahrgang()
    {
        var club = new LeagueClubGame
        {
            ClubId = TestClubs.HomeId,
            White = "Schwaz", Black = "Gegner", Pgn = "1. e4 c5 2. Nf3 d6 *", Plies = 4, Year = 2025,
            Classifier1 = "Landesliga", Classifier2 = "2025/26",
            MovesHash = RookHub.Api.Services.League.LeagueClubService.HashOf(new[] { "e4", "c5", "Nf3", "d6" }),
        };
        _db.LeagueClubGames.Add(club);
        var owner = await UserAsync("owner");
        await _db.SaveChangesAsync();
        await MemberAsync(owner.Id);

        var res = await _svc.ImportPgnAsync(owner.Id, "[White \"Schwaz\"]\n[Black \"Gegner\"]\n\n1. e4 c5 2. Nf3 d6 *");

        var copy = await RowAsync(res.Ids[0]);
        Assert.Equal(club.Id, copy.LeagueClubGameId);
        Assert.Equal(("Landesliga", "2025/26"), (copy.Classifier1, copy.Classifier2));
        var listed = Assert.Single(await _svc.ListAsync(owner.Id));
        Assert.Equal(("Landesliga", "2025/26"), (listed.Classifier1, listed.Classifier2));

        // Altstück: unverbunden, Liga vom Nutzer gesetzt → Verbinden trägt nur den Jahrgang nach.
        var tracked = await _db.SavedGames.SingleAsync(g => g.Id == copy.Id);   // RowAsync liefert Abgetrenntes
        tracked.LeagueClubGameId = null; tracked.Classifier1 = "Eigene Liga"; tracked.Classifier2 = null;
        await _db.SaveChangesAsync();
        await _svc.LinkClubCopiesAsync();
        var healed = await RowAsync(res.Ids[0]);
        Assert.Equal(("Eigene Liga", "2025/26"), (healed.Classifier1, healed.Classifier2));
    }

    // ----- Aus der Vereins-Datenbank kopiert (0.653.0): deren Analyse statt einer zweiten Rechnung -----

    /// <summary>Das Konto gehört über eine Gruppe zum Testverein (Mandanten-Schritt 2026-10-07: Kopie und Analyse einer
    /// Vereinspartie nur für Mitglieder ihres Vereins).</summary>
    private async Task MemberAsync(int userId)
    {
        if (!await _db.LeagueClubs.AnyAsync()) await TestClubs.SeedAsync(_db);
        if (!await _db.Groups.AnyAsync(g => g.Id == 77))
        {
            _db.Groups.Add(new Group { Id = 77, Name = "Testdorf" });
            _db.LeagueClubMembers.Add(new LeagueClubMember { ClubId = TestClubs.HomeId, GroupId = 77 });
        }
        _db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = 77 });
        await _db.SaveChangesAsync();
    }

    /// <summary>Wer NICHT zum Verein der Partie gehört, bekommt weder deren Analyse noch die Verbindung zur Vereinspartie —
    /// auch nicht, wenn er dieselben Züge hochlädt (Mandanten-Schritt 2026-10-07).</summary>
    [Fact]
    public async Task Import_kopierteVereinspartie_einesFremdenVereins_bleibtUnverbunden()
    {
        await ClubAnalysisAsync();
        await TestClubs.SeedAsync(_db);
        var stranger = await UserAsync("stranger");

        var res = await _svc.ImportPgnAsync(stranger.Id, "[White \"X\"]\n[Black \"Y\"]\n\n1. e4 c5 2. Nf3 d6 *");

        var copy = await RowAsync(res.Ids[0]);
        Assert.Null(copy.GameAnalysisId);
        Assert.Null(copy.LeagueClubGameId);
        await _svc.LinkClubCopiesAsync();
        Assert.Null((await RowAsync(res.Ids[0])).LeagueClubGameId);
    }

    /// <summary>Eine Vereinspartie mit den Zügen 1.e4 c5 2.Nf3 d6 samt ihrer Hintergrund-Analyse.</summary>
    private async Task<GameAnalysis> ClubAnalysisAsync(GameAnalysisStatus status = GameAnalysisStatus.Done)
    {
        var club = new LeagueClubGame
        {
            ClubId = TestClubs.HomeId,
            White = "Schwaz", Black = "Gegner", Pgn = "1. e4 c5 2. Nf3 d6 *", Plies = 4, Year = 2026,
            MovesHash = RookHub.Api.Services.League.LeagueClubService.HashOf(new[] { "e4", "c5", "Nf3", "d6" }),
        };
        _db.LeagueClubGames.Add(club);
        var house = await UserAsync("house");
        await _db.SaveChangesAsync();
        var analysis = new GameAnalysis
        {
            UserId = house.Id, Title = "Schwaz – Gegner", Pgn = club.Pgn, Origin = GameAnalysisOrigin.Club,
            LeagueClubGameId = club.Id, Status = status, PlyCount = 4,
        };
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();
        return analysis;
    }

    [Fact]
    public async Task Analyze_kopierteVereinspartie_nimmtDieVereinsanalyse_stattNeuZuRechnen()
    {
        var club = await ClubAnalysisAsync();
        var owner = await UserAsync("owner");
        await MemberAsync(owner.Id);
        var game = await SaveAsync(owner.Id);   // dieselben Züge, andere Kopfdaten

        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.True(result!.Reused);
        Assert.Equal(club.Id, result.Analysis!.Id);
        Assert.Equal(club.Id, (await RowAsync(game.Id)).GameAnalysisId);
        Assert.Equal(1, await _db.GameAnalyses.CountAsync());
    }

    [Fact]
    public async Task Analyze_gescheiterteVereinsanalyse_zaehltNicht()
    {
        await ClubAnalysisAsync(GameAnalysisStatus.Failed);
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);

        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.False(result!.Reused);
        Assert.Equal(GameAnalysisOrigin.SavedGame, (await _db.GameAnalyses.AsNoTracking().SingleAsync(a => a.Id == result.Analysis!.Id)).Origin);
    }

    [Fact]
    public async Task Import_kopierteVereinspartie_hatSofortDieVereinsanalyse_andereZuegeNicht()
    {
        var club = await ClubAnalysisAsync();
        var owner = await UserAsync("owner");
        await MemberAsync(owner.Id);

        var res = await _svc.ImportPgnAsync(owner.Id,
            "[White \"Schwaz\"]\n[Black \"Gegner\"]\n\n1. e4 c5 2. Nf3 d6 *\n\n[White \"A\"]\n[Black \"B\"]\n\n1. e4 c5 2. Nf3 Nc6 *");

        Assert.Equal(2, res.Imported);
        Assert.Equal(club.Id, (await RowAsync(res.Ids[0])).GameAnalysisId);
        Assert.Null((await RowAsync(res.Ids[1])).GameAnalysisId);
    }

    /// <summary>0.665.0: eine Liga-Partie aus dem Hintergrund-Stapel (über ihren Zug-Schlüssel) zählt genauso.</summary>
    [Fact]
    public async Task Analyze_LigaPartieDesStapels_nimmtDeren_Analyse()
    {
        var house = await UserAsync("house");
        var league = new GameAnalysis
        {
            UserId = house.Id, Title = "Liga", Pgn = "1. e4 c5 2. Nf3 d6 *", Origin = GameAnalysisOrigin.League,
            MovesHash = RookHub.Api.Services.League.LeagueClubService.HashOf(new[] { "e4", "c5", "Nf3", "d6" }),
            Status = GameAnalysisStatus.Done, PlyCount = 4,
        };
        _db.GameAnalyses.Add(league);
        await _db.SaveChangesAsync();
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);

        var result = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.True(result!.Reused);
        Assert.Equal(league.Id, result.Analysis!.Id);
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

    /// <summary>Die Sprache der Seite (0.540.0): darin entstehen nach der Analyse Erklärungen und Roasts — gemerkt
    /// nur beim Besitzer, auch wenn die Analyse wiederverwendet wird; ein Gast bestimmt nichts an fremden Partien.</summary>
    [Fact]
    public async Task Analyze_merktDieSpracheDesBesitzers_nichtDieDesGastes()
    {
        var owner = await UserAsync("owner");
        var guest = await UserAsync("guest");
        var game = await SaveAsync(owner.Id);

        await _svc.AnalyzeAsync(owner.Id, game.Id, lang: "de-AT");
        Assert.Equal("de", (await RowAsync(game.Id)).ReviewLanguage);

        await _svc.AnalyzeSharedAsync(guest.Id, game.ShareToken, lang: "hr");
        Assert.Equal("de", (await RowAsync(game.Id)).ReviewLanguage);

        var again = await _svc.AnalyzeSharedAsync(owner.Id, game.ShareToken, lang: "en");   // wiederverwendet
        Assert.True(again!.Reused);
        Assert.Equal("en", (await RowAsync(game.Id)).ReviewLanguage);

        await _svc.AnalyzeAsync(owner.Id, game.Id);                                         // Erweiterung: keine Sprache
        Assert.Equal("en", (await RowAsync(game.Id)).ReviewLanguage);
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

    // ===== Partie aus einer eigenen Stellung (Sparring gegen Maia) ==========

    /// <summary>Eine Partie, die aus einer Stellung mit Schwarz am Zug beginnt (Sparring mitten in einer Partie,
    /// hochgeladen über <c>POST /api/games/import</c>): die Analyse beginnt GENAU bei der FEN aus dem Kopf, rechnet je
    /// Halbzug eine Stellung, und der erste Halbzug gehört Schwarz. Ein Versatz hier verschöbe Kurve, Zug-Klassen und
    /// Fehler-Training um einen Halbzug.</summary>
    [Fact]
    public async Task Analyze_PartieAusEinerStellung_SchwarzAmZug_beginntBeiDerFenAusDemKopf()
    {
        const string fen = "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 4 4";
        var owner = await UserAsync("owner");
        var import = await _svc.ImportPgnAsync(owner.Id,
            "[Event \"Sparring vs Maia\"]\n[Site \"RookHub\"]\n[Date \"2026.10.02\"]\n[Round \"-\"]\n"
            + "[White \"Maia 1600\"]\n[Black \"owner\"]\n[Result \"*\"]\n[SetUp \"1\"]\n"
            + $"[FEN \"{fen}\"]\n\n4... Bc5 5. c3 d6 *\n", "black");
        var gameId = Assert.Single(import.Ids);

        var result = await _svc.AnalyzeAsync(owner.Id, gameId);

        Assert.Null(result!.Reason);
        var analysis = await _db.GameAnalyses.AsNoTracking().SingleAsync();
        Assert.Equal(fen, analysis.StartFen);
        Assert.Equal(3, analysis.PlyCount);
        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == analysis.Id).OrderBy(p => p.Ply).ToListAsync();
        Assert.Equal(new[] { 0, 1, 2, 3 }, positions.Select(p => p.Ply));
        Assert.Equal(fen, positions[0].Fen);
        Assert.Equal(("f8c5", "Bc5"), (positions[0].GameMoveUci, positions[0].GameMoveSan));
        Assert.Equal("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 5 5", positions[1].Fen);
        Assert.Equal(("c2c3", "c3"), (positions[1].GameMoveUci, positions[1].GameMoveSan));
        Assert.Equal("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/2P2N2/PP1P1PPP/RNBQK2R b KQkq - 0 5", positions[2].Fen);
        Assert.Equal(("d7d6", "d6"), (positions[2].GameMoveUci, positions[2].GameMoveSan));
        Assert.Equal("black", (await RowAsync(gameId)).OwnerSide);
    }

    // ===== Längerer Re-Save aus der Erweiterung (N8-002) =====================

    private Task<SavedGameDetailDto> ResaveAsync(int userId, List<string> moves, int? whiteElo = null)
        => _svc.SaveAsync(userId, new SaveGameInputDto
        {
            Source = "lichess", Moves = moves, White = "Anna", Black = "Bert", Result = "1-0", ExternalId = "g-1",
            WhiteElo = whiteElo,
        });

    private async Task AddRecapAndTrainingAsync(int userId, int gameId)
    {
        _db.GameRecaps.Add(new GameRecap { SavedGameId = gameId, Language = "en", Text = "Anna won in four moves." });
        _db.GameMistakeProgresses.Add(new GameMistakeProgress { UserId = userId, SavedGameId = gameId, Total = 2, SolvedPlies = "3", SolvedCount = 1 });
        await _db.SaveChangesAsync();
    }

    /// <summary>Partie mitten im Spiel gespeichert und analysiert, später vollständig neu gespeichert (gleiche ExternalId,
    /// heilt in place): vorher gab „Analysieren" die alte Analyse als „Reused" zurück — die Kurve endete bei der alten
    /// Fassung, Fehler-Training und Nacherzählung (auch in der Vorschau von /g/{token}) galten für die alten Züge.</summary>
    [Fact]
    public async Task Resave_mitMehrZuegen_loestAnalyse_Training_undNacherzaehlung_undAnalysierenRechnetNeu()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var old = await _svc.AnalyzeAsync(owner.Id, game.Id);
        await AddRecapAndTrainingAsync(owner.Id, game.Id);

        var healed = await ResaveAsync(owner.Id, new() { "e4", "c5", "Nf3", "d6", "d4", "cxd4" });

        Assert.Equal(game.Id, healed.Id);
        Assert.Equal(game.ShareToken, healed.ShareToken);
        Assert.Null((await RowAsync(game.Id)).GameAnalysisId);
        Assert.Empty(_db.GameRecaps.Where(r => r.SavedGameId == game.Id));
        Assert.Empty(_db.GameMistakeProgresses.Where(p => p.SavedGameId == game.Id));
        Assert.Null((await _svc.GetSharedAsync(game.ShareToken))!.Recap);

        var again = await _svc.AnalyzeAsync(owner.Id, game.Id);

        Assert.False(again!.Reused);
        Assert.NotEqual(old!.Analysis!.Id, again.Analysis!.Id);
        Assert.Equal(again.Analysis.Id, (await RowAsync(game.Id)).GameAnalysisId);
    }

    /// <summary>Ein Re-Save, der nur die Wertung nachträgt (gleiche Züge), ändert an Analyse, Training und
    /// Nacherzählung nichts.</summary>
    [Fact]
    public async Task Resave_nurMitElo_behaeltAnalyse_Training_undNacherzaehlung()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        var old = await _svc.AnalyzeAsync(owner.Id, game.Id);
        await AddRecapAndTrainingAsync(owner.Id, game.Id);

        var healed = await ResaveAsync(owner.Id, new() { "e4", "c5", "Nf3", "d6" }, whiteElo: 1832);

        Assert.Equal(1832, healed.WhiteElo);
        Assert.Equal(old!.Analysis!.Id, (await RowAsync(game.Id)).GameAnalysisId);
        Assert.Single(_db.GameRecaps.Where(r => r.SavedGameId == game.Id));
        Assert.Single(_db.GameMistakeProgresses.Where(p => p.SavedGameId == game.Id));
    }

    /// <summary>Korrigieren (UpdateAsync) nimmt denselben Weg — und löscht jetzt auch die Nacherzählung.</summary>
    [Fact]
    public async Task Korrigieren_mitAnderenZuegen_loeschtAuchDieNacherzaehlung()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);
        await _svc.AnalyzeAsync(owner.Id, game.Id);
        await AddRecapAndTrainingAsync(owner.Id, game.Id);

        await _svc.UpdateAsync(owner.Id, game.Id, new GameUpdateDto
        {
            Moves = new() { new() { San = "e4" }, new() { San = "e5" } }, White = "Anna", Black = "Bert", Result = "1-0",
        });

        Assert.Null((await RowAsync(game.Id)).GameAnalysisId);
        Assert.Empty(_db.GameRecaps.Where(r => r.SavedGameId == game.Id));
        Assert.Empty(_db.GameMistakeProgresses.Where(p => p.SavedGameId == game.Id));
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

    /// <summary>Buchzuege (0.522.0): aus den fuer die Erweiterung markierten Repertoires des AUFRUFERS — anonym keine,
    /// sonst verriete ein Teilen-Link, was der Teilende vorbereitet hat.</summary>
    [Fact]
    public async Task Evals_Buchzuege_ausDenErweiterungsRepertoiresDesAufrufers_anonymKeine()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id);                       // 1.e4 c5 2.Nf3 d6
        await _svc.AnalyzeAsync(owner.Id, game.Id);                 // legt die Stellungen an
        _db.Repertoires.Add(new Repertoire
        {
            UserId = owner.Id, Name = "Sizilianisch", Kind = RepertoireKind.Opening, UseForExtension = true,
            Files = { new RepertoireFile { FileName = "sic.pgn", PgnContent = "[Event \"x\"]\n\n1. e4 c5 2. Nf3 Nc6 *", FileSize = 40 } },
        });
        await _db.SaveChangesAsync();

        var mine = await _svc.GetEvalsAsync(owner.Id, game.Id);
        var anonymous = await _svc.GetSharedEvalsAsync(game.ShareToken, callerUserId: null);

        Assert.Equal(new[] { 0, 1, 2 }, mine!.BookPlies);            // 2…d6 steht nicht im Repertoire
        Assert.Empty(anonymous!.BookPlies);

        // 0.664.0: ohne Buchzüge (der schnelle Weg der Seite) bleibt alles andere gleich, nur die Liste ist leer.
        var fast = await _svc.GetEvalsAsync(owner.Id, game.Id, withBook: false);
        Assert.Empty(fast!.BookPlies);
        Assert.Equal(mine.Plies.Count, fast.Plies.Count);
        Assert.Equal(mine.Status, fast.Status);
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
        foreach (var p in analysis.Positions) p.CandidatesJson = $"[{{\"uci\":\"{TestMoves.MoveOf(p)}\",\"cp\":0}}]";
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

    // ----- Wertung + Bedenkzeit als SPALTEN (0.526.0, Partienliste im chess.com-Schnitt) -----

    [Fact]
    public async Task Save_legtWertungUndBedenkzeitInDieSpalten_undMarkiertSieAlsGelesen()
    {
        var user = await UserAsync("anna");

        await _svc.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "chess.com", Moves = new() { "e4", "c5" }, ExternalId = "184299739920",
            White = "Anna", Black = "Bert", Result = "1-0",
            WhiteElo = 1632, BlackElo = 40 /* unplausibel */, TimeControl = "180+2",
        });

        var row = await _db.SavedGames.AsNoTracking().SingleAsync();
        Assert.Equal(1632, row.WhiteElo);
        Assert.Null(row.BlackElo);
        Assert.Equal("180+2", row.TimeControl);
        Assert.True(row.HeadersScanned);

        var listed = Assert.Single(await _svc.ListAsync(user.Id));
        Assert.Equal(1632, listed.WhiteElo);
        Assert.Equal("180+2", listed.TimeControl);
    }

    /// <summary>Ein Re-Save ohne Wertung darf die gespeicherte nicht loeschen (er heilt nur).</summary>
    [Fact]
    public async Task Save_erneut_ohneWertung_laesstDieGespeicherteStehen()
    {
        var user = await UserAsync("anna");
        var dto = new SaveGameInputDto
        {
            Source = "chess.com", Moves = new() { "e4", "c5" }, ExternalId = "g1",
            White = "Anna", Black = "Bert", Result = "1-0", WhiteElo = 1632, TimeControl = "180+2",
        };
        await _svc.SaveAsync(user.Id, dto);

        dto.Moves = new() { "e4", "c5", "Nf3" };   // mehr Zuege → der Datensatz wird geheilt
        dto.WhiteElo = null; dto.TimeControl = null;
        await _svc.SaveAsync(user.Id, dto);

        var row = await _db.SavedGames.AsNoTracking().SingleAsync();
        Assert.Equal(3, row.MoveCount);
        Assert.Equal(1632, row.WhiteElo);
        Assert.Equal("180+2", row.TimeControl);
    }

    [Fact]
    public async Task List_traegtDieWertungDesAltbestandsAusDemPgnNach_undNurEinmal()
    {
        var user = await UserAsync("anna");
        var saved = await SaveAsync(user.Id);
        // Wie eine Zeile von vor 0.526.0: Wertung nur im PGN, Spalten leer, nie nachgesehen.
        var row = await _db.SavedGames.SingleAsync(g => g.Id == saved.Id);
        row.Pgn = row.Pgn.Replace("[Result", "[WhiteElo \"1832\"]\n[Result");
        row.WhiteElo = null; row.BlackElo = null; row.HeadersScanned = false;
        await _db.SaveChangesAsync();

        var listed = Assert.Single(await _svc.ListAsync(user.Id));
        Assert.Equal(1832, listed.WhiteElo);
        Assert.Null(listed.BlackElo);

        var nachher = await _db.SavedGames.AsNoTracking().SingleAsync(g => g.Id == saved.Id);
        Assert.Equal(1832, nachher.WhiteElo);
        // Auch OHNE gefundenes Schwarz-Elo gilt die Zeile als nachgesehen — sonst holte jeder
        // Listenaufruf ihr PGN wieder.
        Assert.True(nachher.HeadersScanned);
    }

    // ----- „Welche Partien der Uebersicht kennt RookHub schon?" (0.524.0, Haekchen in der Erweiterung) -----

    [Fact]
    public async Task Known_nurEigenePartienDerQuelle_mitRookHubId()
    {
        var owner = await UserAsync("owner");
        var fremd = await UserAsync("fremd", engine: false);
        var meine = await SaveAsync(owner.Id, "184299739920");
        await SaveAsync(fremd.Id, "184296489960", "Clara");

        var known = await _svc.KnownAsync(owner.Id, "lichess",
            new[] { "184299739920", "184296489960", "999" });

        var hit = Assert.Single(known);
        Assert.Equal("184299739920", hit.ExternalId);
        Assert.Equal(meine.Id, hit.Id);
        Assert.Null(hit.Analysis);
        // Andere Quelle = andere Partie, auch bei gleicher Nummer.
        Assert.Empty(await _svc.KnownAsync(owner.Id, "chess.com", new[] { "184299739920" }));
    }

    /// <summary>
    /// Der Analyse-Stand haengt an der GameAnalysis-Id, nicht an der Partie-Id — die Abfrage muss die
    /// verknuepfte Analyse nachschlagen. Mit der Partie-Id gefragt stand hier der Stand einer FREMDEN
    /// Analyse (oder gar keiner), sobald die beiden Zaehler auseinanderlaufen.
    /// </summary>
    [Fact]
    public async Task Known_traegtDenStandDerVERKNUEPFTENAnalyse()
    {
        var owner = await UserAsync("owner");
        // Den Partie-Zaehler vorschieben, damit Partie-Id und Analyse-Id nicht zufaellig gleich sind.
        await SaveAsync(owner.Id, "vorlauf-1", "Dora");
        await SaveAsync(owner.Id, "vorlauf-2", "Emil");
        var game = await SaveAsync(owner.Id, "184299739920");
        var created = await _svc.AnalyzeAsync(owner.Id, game.Id);
        Assert.NotEqual(game.Id, created!.Analysis!.Id);

        var hit = Assert.Single(await _svc.KnownAsync(owner.Id, "lichess", new[] { "184299739920" }));

        // Derselbe Stand, den die Partienliste zeigt — die liest ihn ueber die Analyse-Id.
        var ausDerListe = (await _svc.ListAsync(owner.Id)).Single(g => g.Id == game.Id).Analysis;
        Assert.Equal(ausDerListe!.Status, hit.Analysis!.Status);
        Assert.Equal(4, hit.Analysis.Total);
        Assert.Equal(ausDerListe.Analyzed, hit.Analysis.Analyzed);
    }

    [Fact]
    public async Task Known_verweisInsLeere_keinStand()
    {
        var owner = await UserAsync("owner");
        var game = await SaveAsync(owner.Id, "184299739920");
        var row = await RowAsync(game.Id);
        row.GameAnalysisId = 987654;   // die Analyse gibt es nicht (mehr)
        await _db.SaveChangesAsync();

        Assert.Null(Assert.Single(await _svc.KnownAsync(owner.Id, "lichess", new[] { "184299739920" })).Analysis);
    }

    [Fact]
    public async Task Known_ohneIdsOderMitUnbekannterQuelle_leer()
    {
        var owner = await UserAsync("owner");
        await SaveAsync(owner.Id, "184299739920");

        Assert.Empty(await _svc.KnownAsync(owner.Id, "lichess", Array.Empty<string>()));
        Assert.Empty(await _svc.KnownAsync(owner.Id, "lichess", new[] { "  " }));
        Assert.Empty(await _svc.KnownAsync(owner.Id, "irgendwas", new[] { "184299739920" }));
        Assert.Empty(await _svc.KnownAsync(owner.Id, null, new[] { "184299739920" }));
        Assert.Empty(await _svc.KnownAsync(owner.Id, "lichess", null));
    }
}
