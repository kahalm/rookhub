using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Klammer um die Analyse-Aufträge: Partie zerlegen, in BLÖCKEN einreihen (offene Aufträge sind
/// je Nutzer gedeckelt), fertige Ergebnisse KOPIEREN (der Auftrags-Trimmer räumt sonst die Analyse
/// weg) und am Ende abschließen.
/// </summary>
public class GameAnalysisServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GameAnalysisService _svc;

    public GameAnalysisServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        var jobs = new AnalysisJobService(_db, new EncryptionService(config), null);
        _svc = new GameAnalysisService(_db, jobs, NullLogger<GameAnalysisService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private const string Game = """
[Event "Testpartie"]
[White "Anderssen"]
[Black "Kieseritzky"]
[Result "1-0"]

1. e4 e5 2. f4 exf4 3. Bc4 Qh4+ 4. Kf1 b5 5. Bxb5 Nf6 6. Nf3 Qh6 7. d3 Nh5 1-0
""";

    private async Task<AppUser> CreateUserWithEngineAsync()
    {
        var user = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        // Ohne hinterlegte Hintergrund-Engine kann nichts eingereiht werden.
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = user.Id, EncryptedToken = "enc", BackgroundEngineId = "eei_test",
        });
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Create_zerlegtDiePartie_undReihtNurEinenBlockEin()
    {
        var user = await CreateUserWithEngineAsync();

        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        Assert.Equal(14, dto.PlyCount);                       // 7 Züge × 2
        Assert.Equal("Anderssen – Kieseritzky", dto.Title);
        Assert.Equal(GameAnalysisDefaults.TargetDepth, dto.TargetDepth);
        Assert.Equal(GameAnalysisDefaults.MultiPv, dto.MultiPv);

        var positions = await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == dto.Id).ToListAsync();
        Assert.Equal(14, positions.Count);

        // NICHT alle 14 auf einmal: offene Aufträge sind je Nutzer gedeckelt, also blockweise.
        var enqueued = positions.Count(p => p.AnalysisJobId != null);
        Assert.Equal(GameAnalysisDefaults.MaxOpenJobsPerGame, enqueued);
        // Und zwar in Zugreihenfolge — man will die Partie von vorn ansehen können.
        Assert.All(positions.Where(p => p.AnalysisJobId != null),
            p => Assert.True(p.Ply < GameAnalysisDefaults.MaxOpenJobsPerGame));

        var jobs = await _db.AnalysisJobs.Where(j => j.UserId == user.Id).ToListAsync();
        Assert.Equal(GameAnalysisDefaults.MaxOpenJobsPerGame, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(GameAnalysisDefaults.TargetDepth, j.TargetDepth));
        Assert.All(jobs, j => Assert.Equal("eei_test", j.EngineId));
        Assert.Contains(jobs, j => j.Title!.Contains("1w e4"));   // sprechender Titel in der Auftragsliste
    }

    [Fact]
    public async Task Pump_uebernimmtFertigeErgebnisse_undFuettertNach()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        // Die ersten drei Aufträge „fertig rechnen" lassen.
        var positions = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id && p.AnalysisJobId != null)
            .OrderBy(p => p.Ply).Take(3).ToListAsync();
        foreach (var pos in positions)
        {
            var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == pos.AnalysisJobId);
            job.Status = AnalysisJobStatus.Done;
            job.ReachedDepth = 30;
            job.ResultJson = "{\"depth\":30,\"pvs\":[{\"depth\":30,\"cp\":35,\"moves\":[\"" + pos.GameMoveUci + "\"]}]}";
        }
        await _db.SaveChangesAsync();

        await _svc.PumpOneAsync(dto.Id);

        var after = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id).OrderBy(p => p.Ply).ToListAsync();

        // Ergebnisse sind KOPIERT (nicht nur verlinkt) — der Auftrags-Trimmer darf sie wegräumen.
        Assert.Equal(3, after.Count(p => p.CandidatesJson != null));
        Assert.All(after.Where(p => p.CandidatesJson != null), p =>
        {
            Assert.Equal(30, p.Depth);
            Assert.NotNull(p.EvalText);
            Assert.NotNull(p.AnalyzedAt);
        });

        // … und es wurde nachgefüttert: nie mehr als ein Block offen, aber so viel wie möglich.
        // (Diese Partie ist mit 14 Halbzügen kürzer als 3 + ein voller Block, also sind am Ende
        // ALLE Stellungen entweder fertig oder eingereiht.)
        var open = after.Count(p => p.CandidatesJson == null && p.AnalysisJobId != null);
        Assert.True(open <= GameAnalysisDefaults.MaxOpenJobsPerGame, $"{open} offene Aufträge");
        Assert.Equal(after.Count, after.Count(p => p.CandidatesJson != null || p.AnalysisJobId != null));

        var head = await _db.GameAnalyses.FirstAsync(g => g.Id == dto.Id);
        Assert.Equal(GameAnalysisStatus.Running, head.Status);
    }

    [Fact]
    public async Task Pump_reihtEineStellungNeuEin_wennIhrAuftragVerschwunden()
    {
        // Genau der Fall, gegen den kopiert wird: MaxJobsPerUser räumt fertige Aufträge weg.
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        var pos = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id && p.AnalysisJobId != null).OrderBy(p => p.Ply).FirstAsync();
        var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == pos.AnalysisJobId);
        _db.AnalysisJobs.Remove(job);
        await _db.SaveChangesAsync();

        await _svc.PumpOneAsync(dto.Id);

        var again = await _db.GameAnalysisPositions.FirstAsync(p => p.Id == pos.Id);
        Assert.Null(again.CandidatesJson);
        Assert.NotNull(again.AnalysisJobId);
        Assert.NotEqual(job.Id, again.AnalysisJobId);   // neuer Auftrag
    }

    [Fact]
    public async Task Pump_schliesstAb_wennJedeStellungIhreListeHat()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        // Alles auf einmal „analysiert" setzen und pumpen.
        var all = await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == dto.Id).ToListAsync();
        foreach (var p in all) p.CandidatesJson = "[{\"uci\":\"" + p.GameMoveUci + "\",\"cp\":10}]";
        await _db.SaveChangesAsync();

        await _svc.PumpOneAsync(dto.Id);

        var head = await _db.GameAnalyses.FirstAsync(g => g.Id == dto.Id);
        Assert.Equal(GameAnalysisStatus.Done, head.Status);
        Assert.NotNull(head.FinishedAt);
    }

    [Fact]
    public async Task Pump_haengtNichtAnEinemGescheitertenAuftrag()
    {
        // Matt-/Pattstellungen geben nach drei fruchtlosen Läufen auf. Die Partie muss trotzdem
        // fertig werden — die Stellung bekommt eine leere Liste und wird später übersprungen.
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        var pos = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id && p.AnalysisJobId != null).OrderBy(p => p.Ply).FirstAsync();
        var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == pos.AnalysisJobId);
        job.Status = AnalysisJobStatus.Failed;
        await _db.SaveChangesAsync();

        await _svc.PumpOneAsync(dto.Id);

        var again = await _db.GameAnalysisPositions.FirstAsync(p => p.Id == pos.Id);
        Assert.Equal("[]", again.CandidatesJson);
        Assert.Empty(BrokerCandidates.FromJson(again.CandidatesJson));
    }

    [Fact]
    public async Task OhneHintergrundEngine_scheitertDiePartieMitKlarerMeldung()
    {
        var user = new AppUser { Username = "u2", Email = "u2@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        var head = await _db.GameAnalyses.FirstAsync(g => g.Id == dto.Id);
        Assert.Equal(GameAnalysisStatus.Failed, head.Status);
        Assert.Contains("Engine", head.LastError);
    }

    [Fact]
    public async Task KaputtesPgn_wirdAbgelehnt_stattEineLeerePartieAnzulegen()
    {
        var user = await CreateUserWithEngineAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = "das ist kein PGN" }));
        Assert.Empty(_db.GameAnalyses);
    }

    [Fact]
    public async Task Delete_raeumtStellungenUndOffeneAuftraegeAb()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        Assert.True(await _svc.DeleteAsync(user.Id, dto.Id));

        Assert.Empty(_db.GameAnalyses);
        Assert.Empty(_db.GameAnalysisPositions);
        // Die eingereihten Aufträge sind mitgegangen — sonst rechnete die Engine für nichts weiter.
        Assert.Empty(_db.AnalysisJobs.Where(j => j.UserId == user.Id));
    }

    [Fact]
    public async Task Create_schwemmtDieMerklisteNicht_zu()
    {
        // Regression: `AnalysisJobService.CreateAsync` legt jede eingereihte Stellung zusaetzlich unter
        // „Gemerkte Stellungen" ab — sinnvoll fuer den von Hand eingereihten Einzelauftrag, aber eine
        // Partie erzeugt je Halbzug einen Auftrag und haette die Merkliste des Nutzers geflutet.
        var user = await CreateUserWithEngineAsync();

        await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        Assert.Empty(_db.RememberedPositions.Where(r => r.UserId == user.Id));
    }

    [Fact]
    public async Task Pump_raeumtUebernommeneAuftraegeAb()
    {
        // Der kopierte Auftrag hat seinen Zweck erfuellt. Bliebe er stehen, fuellte eine Partie 80 der
        // 200 Zeilen von MaxJobsPerUser — nach drei Partien haette der Trimmer die von Hand
        // eingereihten Auftraege des Nutzers verdraengt.
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        var pos = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id && p.AnalysisJobId != null)
            .OrderBy(p => p.Ply).FirstAsync();
        var jobId = pos.AnalysisJobId!.Value;
        var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == jobId);
        job.Status = AnalysisJobStatus.Done;
        job.ReachedDepth = 30;
        job.ResultJson = "{\"depth\":30,\"pvs\":[{\"depth\":30,\"cp\":35,\"moves\":[\"" + pos.GameMoveUci + "\"]}]}";
        await _db.SaveChangesAsync();

        await _svc.PumpOneAsync(dto.Id);

        Assert.False(await _db.AnalysisJobs.AnyAsync(j => j.Id == jobId));
        var again = await _db.GameAnalysisPositions.FirstAsync(p => p.Id == pos.Id);
        Assert.NotNull(again.CandidatesJson);
        Assert.Null(again.AnalysisJobId);   // kein Zeiger auf einen geloeschten Auftrag
    }

    [Fact]
    public async Task Delete_nimmtDiePunktepartienMit()
    {
        // GuessSessions zeigen per RESTRICT auf die Analyse: ohne dieses Aufraeumen scheiterte das
        // Loeschen in MySQL mit einem Fremdschluessel-Fehler (500 statt 204).
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });
        var pos = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == dto.Id && p.Ply == 0);
        pos.CandidatesJson = "[{\"uci\":\"" + pos.GameMoveUci + "\",\"cp\":10}]";
        await _db.SaveChangesAsync();

        var session = new GuessSession { UserId = user.Id, GameAnalysisId = dto.Id, StartPly = 0, CurrentPly = 0 };
        session.Moves.Add(new GuessMove { Ply = 0, PlayedUci = pos.GameMoveUci, Grade = GuessGrade.GameMove });
        _db.GuessSessions.Add(session);
        await _db.SaveChangesAsync();

        Assert.True(await _svc.DeleteAsync(user.Id, dto.Id));

        Assert.Empty(_db.GuessSessions);
        Assert.Empty(_db.GuessMoves);
        Assert.Empty(_db.GameAnalyses);
    }

    [Fact]
    public async Task FremdeAnalyseIstUnsichtbar()
    {
        var owner = await CreateUserWithEngineAsync();
        var other = new AppUser { Username = "other", Email = "o@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();

        var dto = await _svc.CreateAsync(owner.Id, new CreateGameAnalysisRequest { Pgn = Game });

        Assert.Null(await _svc.GetAsync(other.Id, dto.Id));
        Assert.False(await _svc.DeleteAsync(other.Id, dto.Id));
        Assert.Empty(await _svc.ListAsync(other.Id));
    }
}
