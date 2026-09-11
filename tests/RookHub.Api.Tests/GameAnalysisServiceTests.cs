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
            UserId = user.Id, EncryptedToken = "enc", BackgroundEngineIds = "eei_test",
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
        // Eine Stellung, an der jeder Anlauf scheitert, darf die Partie nicht aufhalten: nach
        // MaxPositionAttempts bekommt sie eine leere Liste und wird spaeter uebersprungen. Die
        // WIEDERHOLUNGEN davor sind der Punkt — ein einzelner Fehlschlag heisst nur, dass die
        // Engine gerade nicht zu gebrauchen war (siehe GescheiterterAuftrag_reihtDieStellungErneutEin).
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        var pos = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id && p.AnalysisJobId != null).OrderBy(p => p.Ply).FirstAsync();

        for (var i = 0; i < GameAnalysisDefaults.MaxPositionAttempts; i++)
        {
            await _db.Entry(pos).ReloadAsync();
            var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == pos.AnalysisJobId);
            job.Status = AnalysisJobStatus.Failed;
            await _db.SaveChangesAsync();
            await _svc.PumpOneAsync(dto.Id);
        }

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

    // ===== Kuratierter Bestand ==========================================================

    /// <summary>Nur der Kopf, ohne Engine — hier geht es um Sichtbarkeit, nicht um Aufträge.</summary>
    private async Task<GameAnalysis> AddAnalysisAsync(int userId, bool isPublic, bool analyzed,
        string title = "T", string pgn = "1. e4 e5 *")
    {
        var analysis = new GameAnalysis
        {
            UserId = userId, Title = title, Pgn = pgn, StartFen = GamePlies.StartFen(),
            PlyCount = 2, IsPublic = isPublic, Status = GameAnalysisStatus.Done,
        };
        analysis.Positions.Add(new GameAnalysisPosition
        {
            Ply = 0, Fen = GamePlies.StartFen(), GameMoveUci = "e2e4", GameMoveSan = "e4",
            CandidatesJson = analyzed ? "[{\"uci\":\"e2e4\",\"cp\":20}]" : null,
        });
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();
        return analysis;
    }

    /// <summary>
    /// Der Bestand zeigt nur, was freigegeben UND spielbar ist. Eine freigegebene Partie ohne eine
    /// einzige gerechnete Stellung wäre in der Auswahl ein Knopf, der sofort „wird noch gerechnet"
    /// sagt — die gehört noch nicht hinein.
    /// </summary>
    [Fact]
    public async Task ListPublic_nurFreigegebeneUndFertige()
    {
        var user = await CreateUserWithEngineAsync();
        await AddAnalysisAsync(user.Id, isPublic: true, analyzed: true, title: "sichtbar");
        await AddAnalysisAsync(user.Id, isPublic: true, analyzed: false, title: "noch nichts gerechnet");
        await AddAnalysisAsync(user.Id, isPublic: false, analyzed: true, title: "privat");

        var list = await _svc.ListPublicAsync();

        var row = Assert.Single(list);
        Assert.Equal("sichtbar", row.Title);
        Assert.True(row.IsPublic);
    }

    /// <summary>
    /// HALB gerechnet reicht nicht. Anspielen liesse sich so eine Partie zwar, sie ueberspraenge
    /// dann aber stillschweigend jede Stellung ohne Kandidatenliste — man raet eine Partie mit
    /// Loechern, ohne dass irgendwo steht, warum.
    /// </summary>
    [Fact]
    public async Task ListPublic_halbGerechneteBleibenDraussen()
    {
        var user = await CreateUserWithEngineAsync();
        var halb = await AddAnalysisAsync(user.Id, isPublic: true, analyzed: true, title: "halb fertig");
        halb.Positions.Add(new GameAnalysisPosition
        {
            Ply = 1, Fen = GamePlies.StartFen(), GameMoveUci = "e7e5", GameMoveSan = "e5",
            CandidatesJson = null,
        });
        await _db.SaveChangesAsync();

        Assert.Empty(await _svc.ListPublicAsync());
    }

    /// <summary>Grundlage des Filters „alle / nur kommentierte": eine geschweifte Klammer im PGN
    /// IST ein Kommentar.</summary>
    [Fact]
    public async Task ListPublic_meldetKommentierteAls_annotated()
    {
        var user = await CreateUserWithEngineAsync();
        await AddAnalysisAsync(user.Id, true, true, "mit", "1. e4 {ein guter Zug} e5 *");
        await AddAnalysisAsync(user.Id, true, true, "ohne", "1. e4 e5 *");

        var list = await _svc.ListPublicAsync();

        Assert.True(list.Single(a => a.Title == "mit").Annotated);
        Assert.False(list.Single(a => a.Title == "ohne").Annotated);
    }

    [Fact]
    public async Task SetPublic_derBesitzerDarf()
    {
        var user = await CreateUserWithEngineAsync();
        var analysis = await AddAnalysisAsync(user.Id, isPublic: false, analyzed: true);

        Assert.True(await _svc.SetPublicAsync(user.Id, isAdmin: false, analysis.Id, true));
        Assert.True((await _db.GameAnalyses.FindAsync(analysis.Id))!.IsPublic);

        Assert.False(await _svc.SetPublicAsync(user.Id, isAdmin: false, analysis.Id, false));
        Assert.False((await _db.GameAnalyses.FindAsync(analysis.Id))!.IsPublic);
    }

    [Fact]
    public async Task SetPublic_einFremderNicht_einAdminSchon()
    {
        var user = await CreateUserWithEngineAsync();
        var analysis = await AddAnalysisAsync(user.Id, isPublic: false, analyzed: true);
        var other = new AppUser { Username = "o", Email = "o@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();

        Assert.Null(await _svc.SetPublicAsync(other.Id, isAdmin: false, analysis.Id, true));
        Assert.False((await _db.GameAnalyses.FindAsync(analysis.Id))!.IsPublic);

        Assert.True(await _svc.SetPublicAsync(other.Id, isAdmin: true, analysis.Id, true));
        Assert.True((await _db.GameAnalyses.FindAsync(analysis.Id))!.IsPublic);
    }

    [Fact]
    public async Task SetPublic_unbekanteAnalyse_istNull()
    {
        var user = await CreateUserWithEngineAsync();
        Assert.Null(await _svc.SetPublicAsync(user.Id, isAdmin: true, 4711, true));
    }

    // ===== Gescheiterte Stellungen ======================================================

    /// <summary>
    /// Ein gescheiterter Auftrag heisst „die Engine war gerade nicht zu gebrauchen", nicht „diese
    /// Stellung geht nicht". Frueher wurde sie sofort mit leerer Kandidatenliste abgeschlossen —
    /// eine tote Engine loeschte damit Stellungen aus der Partie, die nie wieder gerechnet wurden.
    /// </summary>
    [Fact]
    public async Task GescheiterterAuftrag_reihtDieStellungErneutEin()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });
        var pos = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == dto.Id && p.Ply == 0);
        var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == pos.AnalysisJobId);

        job.Status = AnalysisJobStatus.Failed;
        job.LastError = "Engine lieferte nichts";
        await _db.SaveChangesAsync();
        await _svc.PumpOneAsync(dto.Id);

        await _db.Entry(pos).ReloadAsync();
        Assert.Equal(1, pos.FailedAttempts);
        Assert.Null(pos.CandidatesJson);                      // NICHT aufgegeben
        Assert.NotNull(pos.AnalysisJobId);                    // schon wieder eingereiht
    }

    /// <summary>Aber nicht ewig: nach MaxPositionAttempts ist Schluss, sonst liefe eine wirklich
    /// unloesbare Stellung im Kreis.</summary>
    [Fact]
    public async Task GescheiterterAuftrag_gibtNachDreiAnlaeufenAuf()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });
        var pos = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == dto.Id && p.Ply == 0);

        for (var i = 0; i < GameAnalysisDefaults.MaxPositionAttempts; i++)
        {
            await _db.Entry(pos).ReloadAsync();
            var job = await _db.AnalysisJobs.FirstAsync(j => j.Id == pos.AnalysisJobId);
            job.Status = AnalysisJobStatus.Failed;
            await _db.SaveChangesAsync();
            await _svc.PumpOneAsync(dto.Id);
        }

        await _db.Entry(pos).ReloadAsync();
        Assert.Equal(GameAnalysisDefaults.MaxPositionAttempts, pos.FailedAttempts);
        Assert.Equal("[]", pos.CandidatesJson);
        Assert.Null(pos.AnalysisJobId);
    }

    // ===== Mehrere Hintergrund-Engines ==================================================

    /// <summary>
    /// Der Worker rechnet je ENGINE einen Auftrag — die Verteilung entscheidet also, wie viele
    /// nebeneinander laufen. Neue Auftraege gehen deshalb auf die Engine mit der kuerzesten
    /// Schlange, nicht immer auf dieselbe.
    /// </summary>
    [Fact]
    public async Task NeueAuftraege_verteilenSichAufDieHinterlegtenEngines()
    {
        var user = new AppUser { Username = "m", Email = "m@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var cred = new LichessEngineCredential { UserId = user.Id, EncryptedToken = "enc" };
        cred.SetBackgroundEngines(["eei_a", "eei_b"]);
        _db.LichessEngineCredentials.Add(cred);
        await _db.SaveChangesAsync();

        var jobs = new AnalysisJobService(_db, new EncryptionService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" }).Build()), null);

        var a = await jobs.CreateAsync(user.Id, new CreateAnalysisJobRequest
        { Fen = GamePlies.StartFen(), TargetDepth = 20, MultiPv = 3 }, remember: false);
        var b = await jobs.CreateAsync(user.Id, new CreateAnalysisJobRequest
        { Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", TargetDepth = 20, MultiPv = 3 },
            remember: false);

        var used = await _db.AnalysisJobs.Where(j => j.Id == a.Id || j.Id == b.Id)
            .Select(j => j.EngineId).ToListAsync();
        Assert.Equal(2, used.Distinct().Count());
    }

    // ===== Neu anstossen ======================================================

    /// <summary>
    /// Der Knopf muss den ALTEN Auftrag loswerden. Ein Auftrag klebt an der Engine, die beim
    /// Anlegen gewaehlt wurde, und wechselt nie wieder — bleibt er stehen, aendert der Reset nichts.
    /// </summary>
    [Fact]
    public async Task Restart_verwirftDenSteckengebliebenenAuftrag_undReihtNeuEin()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        var before = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id && p.AnalysisJobId != null)
            .Select(p => p.AnalysisJobId!.Value).ToListAsync();
        Assert.NotEmpty(before);

        var after = await _svc.RestartAsync(user.Id, dto.Id);

        Assert.NotNull(after);
        var now = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id && p.AnalysisJobId != null)
            .Select(p => p.AnalysisJobId!.Value).ToListAsync();
        Assert.NotEmpty(now);                      // wieder eingereiht …
        Assert.Empty(now.Intersect(before));       // … aber mit NEUEN Auftraegen
        Assert.Empty(await _db.AnalysisJobs.Where(j => before.Contains(j.Id)).ToListAsync());
    }

    /// <summary>Aufgegebene Stellungen zaehlen als „gerechnet" und sind der Grund, warum eine Partie
    /// fertig aussehen kann und in der Punktepartie trotzdem Loecher hat. Genau die soll der Reset
    /// schliessen.</summary>
    [Fact]
    public async Task Restart_holtAufgegebeneStellungenZurueck()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });

        // Eine Stellung hat die Engine aufgegeben, eine andere ist echt gerechnet.
        var positions = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id).OrderBy(p => p.Ply).ToListAsync();
        positions[0].CandidatesJson = "[]";
        positions[0].AnalysisJobId = null;
        positions[0].FailedAttempts = GameAnalysisDefaults.MaxPositionAttempts;
        positions[0].AnalyzedAt = DateTime.UtcNow;
        positions[1].CandidatesJson = "[{\"uci\":\"e2e4\"}]";
        positions[1].AnalysisJobId = null;
        positions[1].AnalyzedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _svc.RestartAsync(user.Id, dto.Id);

        var reloaded = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == dto.Id).OrderBy(p => p.Ply).ToListAsync();
        Assert.Null(reloaded[0].CandidatesJson);            // aufgegeben → zurueck in die Schlange
        Assert.Equal(0, reloaded[0].FailedAttempts);        // mit frischem Zaehler
        Assert.NotNull(reloaded[1].CandidatesJson);         // echtes Ergebnis bleibt
    }

    /// <summary>Eine gescheiterte Partie darf der Knopf wieder in Gang setzen — sonst waere sie
    /// nur ueber Loeschen und neu Einwerfen zu retten.</summary>
    [Fact]
    public async Task Restart_setztDenFehlerZurueck()
    {
        var user = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });
        var analysis = await _db.GameAnalyses.FirstAsync(g => g.Id == dto.Id);
        analysis.Status = GameAnalysisStatus.Failed;
        analysis.LastError = "Keine Hintergrund-Engine konfiguriert";
        analysis.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var after = await _svc.RestartAsync(user.Id, dto.Id);

        Assert.NotNull(after);
        Assert.NotEqual("failed", after!.Status);
        Assert.Null(after.LastError);
        Assert.Null(after.FinishedAt);
    }

    /// <summary>Fremde Partien gehen niemanden etwas an — auch nicht ueber diesen Knopf.</summary>
    [Fact]
    public async Task Restart_fremdePartie_gibtNichts()
    {
        var owner = await CreateUserWithEngineAsync();
        var dto = await _svc.CreateAsync(owner.Id, new CreateGameAnalysisRequest { Pgn = Game });
        var other = await CreateUserAsync("fremd");

        Assert.Null(await _svc.RestartAsync(other.Id, dto.Id));
    }

    // ===== Einwurf auf der Punktepartie-Seite =================================

    private async Task<AppUser> CreateUserAsync(string name, bool admin = false)
    {
        var user = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h", IsAdmin = admin };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task GiveEngineAsync(AppUser user, bool house = false)
    {
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = user.Id, User = user, EncryptedToken = "enc",
            BackgroundEngineIds = $"eei_{user.Username}", ShareAsHouseEngine = house,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>Die Tiefe steht in keinem Formular — und sie ist auch nicht zu erraten: der Einwurf
    /// nimmt sie gar nicht erst entgegen.</summary>
    [Fact]
    public async Task CreateForGuess_rechnetMitFesterTiefe_aufDerEigenenEngine()
    {
        var user = await CreateUserAsync("u");
        await GiveEngineAsync(user);

        var result = await _svc.CreateForGuessAsync(user.Id, new CreateGuessGameRequest { Pgn = Game });

        Assert.Null(result.Reason);
        Assert.NotNull(result.Analysis);
        Assert.Equal(GameAnalysisDefaults.GuessTargetDepth, result.Analysis!.TargetDepth);
        Assert.NotEqual(GameAnalysisDefaults.TargetDepth, result.Analysis.TargetDepth);

        var analysis = await _db.GameAnalyses.FirstAsync(g => g.Id == result.Analysis.Id);
        Assert.Equal(GameAnalysisOrigin.Guess, analysis.Origin);
        // Eigene Maschine: kein fremder Engine-Besitzer am Auftrag.
        Assert.Null(analysis.EngineOwnerUserId);
        Assert.All(await _db.AnalysisJobs.ToListAsync(), j => Assert.Null(j.EngineOwnerUserId));
    }

    /// <summary>Ohne eigene Engine rechnet die Haus-Engine — der Auftrag bleibt aber beim Einwerfer,
    /// nur Token und Engine kommen vom Haus-Konto.</summary>
    [Fact]
    public async Task CreateForGuess_ohneEigeneEngine_nimmtDieHausEngine()
    {
        var admin = await CreateUserAsync("admin", admin: true);
        await GiveEngineAsync(admin, house: true);
        var user = await CreateUserAsync("u");

        var result = await _svc.CreateForGuessAsync(user.Id, new CreateGuessGameRequest { Pgn = Game });

        Assert.Null(result.Reason);
        var analysis = await _db.GameAnalyses.FirstAsync(g => g.Id == result.Analysis!.Id);
        Assert.Equal(admin.Id, analysis.EngineOwnerUserId);

        var jobs = await _db.AnalysisJobs.ToListAsync();
        Assert.NotEmpty(jobs);
        Assert.All(jobs, j =>
        {
            Assert.Equal(user.Id, j.UserId);              // Deckel und Liste bleiben beim Einwerfer
            Assert.Equal(admin.Id, j.EngineOwnerUserId);  // gerechnet wird auf der Haus-Engine
            Assert.Equal("eei_admin", j.EngineId);
        });
    }

    /// <summary>Eine Freigabe von jemandem OHNE Admin-Rechte zaehlt nicht — sonst liefe eine fremde
    /// Maschine weiter fuer alle, nachdem dem Konto die Rechte entzogen wurden.</summary>
    [Fact]
    public async Task CreateForGuess_ohneEngine_sagtWarum()
    {
        var notAdmin = await CreateUserAsync("ex-admin");
        await GiveEngineAsync(notAdmin, house: true);
        var user = await CreateUserAsync("u");

        var result = await _svc.CreateForGuessAsync(user.Id, new CreateGuessGameRequest { Pgn = Game });

        Assert.Equal(GuessUploadReason.NoEngine, result.Reason);
        Assert.Null(result.Analysis);
        Assert.Empty(await _db.GameAnalyses.ToListAsync());
    }

    /// <summary>Der Deckel gilt fuer eingeworfene Partien, nicht fuer von Hand eingereihte.</summary>
    [Fact]
    public async Task CreateForGuess_ueberDemDeckel_nimmtNichtsMehrAn()
    {
        var user = await CreateUserAsync("u");
        await GiveEngineAsync(user);

        for (var i = 0; i < GameAnalysisDefaults.MaxOpenGuessGamesPerUser; i++)
            Assert.Null((await _svc.CreateForGuessAsync(user.Id, new CreateGuessGameRequest { Pgn = Game })).Reason);

        var refused = await _svc.CreateForGuessAsync(user.Id, new CreateGuessGameRequest { Pgn = Game });
        Assert.Equal(GuessUploadReason.TooManyOpen, refused.Reason);

        var status = await _svc.GuessUploadStatusAsync(user.Id);
        Assert.True(status.EngineAvailable);
        Assert.True(status.OwnEngine);
        Assert.Equal(GameAnalysisDefaults.MaxOpenGuessGamesPerUser, status.OpenGames);

        // Von Hand geht weiter — dort rechnet die eigene Maschine, und da zaehlt niemand mit.
        var manual = await _svc.CreateAsync(user.Id, new CreateGameAnalysisRequest { Pgn = Game });
        Assert.Equal(GameAnalysisDefaults.TargetDepth, manual.TargetDepth);
    }

    /// <summary>Ein Text ohne spielbare Partie ist eine Absage mit Grund, keine Ausnahme.</summary>
    [Fact]
    public async Task CreateForGuess_ohneSpielbaresPgn_sagtWarum()
    {
        var user = await CreateUserAsync("u");
        await GiveEngineAsync(user);

        var result = await _svc.CreateForGuessAsync(user.Id, new CreateGuessGameRequest { Pgn = "kein PGN" });

        Assert.Equal(GuessUploadReason.InvalidPgn, result.Reason);
        Assert.Empty(await _db.GameAnalyses.ToListAsync());
    }

    /// <summary>Die CSV-Zerlegung liegt am Modell — Duplikate und Leerwerte fallen raus.</summary>
    [Fact]
    public void SetBackgroundEngines_raeumtAuf()
    {
        var cred = new LichessEngineCredential();
        cred.SetBackgroundEngines([" eei_a ", "eei_b", "eei_a", "", "  "]);
        Assert.Equal(["eei_a", "eei_b"], cred.BackgroundEngines);

        cred.SetBackgroundEngines([]);
        Assert.Null(cred.BackgroundEngineIds);
        Assert.Empty(cred.BackgroundEngines);
    }
}
