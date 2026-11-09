using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Tests.Games;

/// <summary>„Eine Partie, eine Analyse" (0.924.0): Einträge, die auf eine Vereinspartie zeigen.</summary>
public class ClubGameReferencesTests : IDisposable
{
    private const int House = 5, Member = 7;
    private const string Pgn = "[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 Nc6 1-0";
    private static readonly string[] Sans = ["e4", "e5", "Nf3", "Nc6"];
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public ClubGameReferencesTests()
    {
        _db.AppUsers.Add(new AppUser { Id = House, Username = "haus", PasswordHash = "x", IsAdmin = true });
        _db.AppUsers.Add(new AppUser { Id = Member, Username = "m", PasswordHash = "x" });
        var cred = new LichessEngineCredential { UserId = House, EncryptedToken = "enc", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(["rhe_a", "rhe_b"]);
        _db.LichessEngineCredentials.Add(cred);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private GameAnalysisService Analyses() => new(_db, new AnalysisJobService(_db),
        new CommentSetService(_db, NullLogger<CommentSetService>.Instance), NullLogger<GameAnalysisService>.Instance);

    private LeagueClubGame Club()
    {
        var c = new LeagueClubGame { ClubId = 1, Pgn = Pgn, White = "A", Black = "B", Result = "1-0", MovesHash = LeagueClubService.HashOf(Sans), Year = 2026 };
        _db.LeagueClubGames.Add(c);
        _db.SaveChanges();
        return c;
    }

    private GameAnalysis Analysis(int user, GameAnalysisOrigin origin, int? club = null, GameAnalysisStatus status = GameAnalysisStatus.Done)
    {
        var a = new GameAnalysis { UserId = user, Pgn = Pgn, StartFen = "x", Status = status, Origin = origin, LeagueClubGameId = club };
        _db.GameAnalyses.Add(a);
        _db.SaveChanges();
        return a;
    }

    private SavedGame Entry(int? club, int? analysis, string pgn = Pgn)
    {
        var g = new SavedGame { UserId = Member, Source = "pgn", Pgn = pgn, LeagueClubGameId = club, GameAnalysisId = analysis, ShareToken = "tok", OwnerSide = "black" };
        _db.SavedGames.Add(g);
        _db.SaveChanges();
        return g;
    }

    [Fact]
    public async Task Probelauf_aendertNichts_Umstellen_behaeltPersoenlichesUndLoeschtEigeneAnalyse()
    {
        var club = Club();
        var main = Analysis(House, GameAnalysisOrigin.Club, club.Id);
        var own = Analysis(Member, GameAnalysisOrigin.SavedGame);
        var entry = Entry(club.Id, own.Id);
        var svc = new ClubGameReferences(_db, Analyses());

        var dry = await svc.RunAsync(apply: false);
        Assert.Equal(1, dry.Convert);
        Assert.Equal(own.Id, (await _db.SavedGames.AsNoTracking().SingleAsync()).GameAnalysisId);
        Assert.NotNull(await _db.GameAnalyses.FindAsync(own.Id));

        var run = await svc.RunAsync(apply: true);
        Assert.Equal(1, run.AnalysesDeleted);
        var after = await _db.SavedGames.AsNoTracking().SingleAsync(g => g.Id == entry.Id);
        Assert.Equal(main.Id, after.GameAnalysisId);
        Assert.Equal("tok", after.ShareToken);
        Assert.Equal("black", after.OwnerSide);
        Assert.False(await _db.GameAnalyses.AnyAsync(a => a.Id == own.Id));

        Assert.Equal(1, (await svc.RunAsync(apply: false)).Done);
    }

    [Fact]
    public async Task AnalyseMitAnderenEintraegen_bleibt_undFremdeVereinspartieWirdNichtVerbunden()
    {
        var club = Club();
        Analysis(House, GameAnalysisOrigin.Club, club.Id);
        var own = Analysis(Member, GameAnalysisOrigin.SavedGame);
        Entry(club.Id, own.Id);
        Entry(null, own.Id, "[White \"X\"]\n\n1. d4 d5 *");   // zeigt dieselbe Analyse, andere Partie

        var run = await new ClubGameReferences(_db, Analyses()).RunAsync(apply: true);

        Assert.Equal(1, run.Convert);
        Assert.Equal(0, run.AnalysesDeleted);
        Assert.True(await _db.GameAnalyses.AnyAsync(a => a.Id == own.Id));
    }

    [Fact]
    public async Task OhneFertigeVereinsanalyse_wartet_andereZuege_bleiben()
    {
        var club = Club();
        Analysis(House, GameAnalysisOrigin.Club, club.Id, GameAnalysisStatus.Running);
        Entry(club.Id, null);
        Entry(club.Id, null, "[White \"A\"]\n\n1. d4 d5 *");

        var run = await new ClubGameReferences(_db, Analyses()).RunAsync(apply: true);

        Assert.Equal(1, run.Waiting);
        Assert.Equal(1, run.Diverged);
        Assert.Equal(0, run.Convert);
    }

    [Fact]
    public async Task Vereinsanalyse_entsteht_verknuepftEintraegeOhneAnalyse()
    {
        var club = Club();
        var entry = Entry(club.Id, null);

        var dto = await Analyses().CreateClubBatchAsync(House, club.Id, Pgn, default, expedited: true);

        Assert.Equal(dto.Id, (await _db.SavedGames.AsNoTracking().SingleAsync(g => g.Id == entry.Id)).GameAnalysisId);
    }

    [Fact]
    public async Task Analysieren_einesVerweises_rechnetKeineZweite_sondernNimmtDieDerVereinspartie()
    {
        var club = Club();
        var entry = Entry(club.Id, null);
        var games = TestServices.SavedGames(_db);

        var created = await games.AnalyzeAsync(Member, entry.Id);

        var a = await _db.GameAnalyses.SingleAsync();
        Assert.Equal(created!.Analysis!.Id, a.Id);
        Assert.Equal(GameAnalysisOrigin.Club, a.Origin);
        Assert.Equal(club.Id, a.LeagueClubGameId);
        Assert.True(a.Expedited);
        Assert.Equal(a.Id, (await _db.SavedGames.AsNoTracking().SingleAsync()).GameAnalysisId);

        var again = await games.AnalyzeAsync(Member, entry.Id);
        Assert.True(again!.Reused);
        Assert.Single(_db.GameAnalyses);
    }

    [Fact]
    public async Task VereinsKorrektur_laesstEintragAufDerVereinsanalyse()
    {
        var club = Club();
        var main = Analysis(House, GameAnalysisOrigin.Club, club.Id);
        var entry = Entry(club.Id, main.Id);

        await SavedGameService.ApplyClubMovesAsync(_db, club.Id, ["e4", "c5"], null);

        var after = await _db.SavedGames.AsNoTracking().SingleAsync(g => g.Id == entry.Id);
        Assert.Equal(main.Id, after.GameAnalysisId);
        Assert.Contains("c5", after.Pgn);
    }
}
