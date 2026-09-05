using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Punktepartie: eine analysierte Partie Zug für Zug erraten. Geprüft wird vor allem, dass die
/// FORTSETZUNG den Server nicht verlässt und dass nur wertbare Stellungen gefragt werden.
/// </summary>
public class GuessSessionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GuessSessionService _svc;

    public GuessSessionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new GuessSessionService(_db);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 — jede Stellung mit Kandidatenliste.</summary>
    private async Task<(AppUser User, GameAnalysis Analysis)> SeedAsync(bool analyzeAll = true)
    {
        var user = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        var pgn = "[Event \"T\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *";
        var (header, plies) = GamePlies.Parse(pgn)!.Value;
        var analysis = new GameAnalysis
        {
            UserId = user.Id, Title = "Test", Pgn = pgn, StartFen = header.StartFen,
            PlyCount = plies.Count, Status = GameAnalysisStatus.Done,
        };
        foreach (var p in plies)
        {
            // Kandidatenliste: der Partiezug (+30) plus eine deutlich schwächere Alternative (-80).
            // Abstand 1.10 > OnlyMoveGapPawns/MuchWorsePawns (1.0) → „einziger Zug" bzw. „deutlich schlechter".
            var alt = AlternativeUci(p.Fen, p.Uci);
            var json = "[{\"uci\":\"" + p.Uci + "\",\"cp\":30}"
                + (alt is null ? "" : ",{\"uci\":\"" + alt + "\",\"cp\":-80}") + "]";
            analysis.Positions.Add(new GameAnalysisPosition
            {
                Ply = p.Index, Fen = p.Fen, GameMoveUci = p.Uci, GameMoveSan = p.San,
                CandidatesJson = analyzeAll ? json : null, Depth = 30, EvalText = "+0.30",
            });
        }
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();
        return (user, analysis);
    }

    /// <summary>Irgendein anderer legaler Zug der Stellung — als schwächere Alternative.</summary>
    private static string? AlternativeUci(string fen, string exclude)
    {
        var board = Chess.ChessBoard.LoadFromFen(fen);
        foreach (var m in board.Moves(generateSan: true))
        {
            var uci = GamePlies.ToUci(m);
            if (uci != exclude) return uci;
        }
        return null;
    }

    [Fact]
    public async Task Start_liefertNurDieStellung_niemalsDieFortsetzung()
    {
        var (user, analysis) = await SeedAsync();

        var dto = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        Assert.NotNull(dto.Position);
        Assert.Equal(0, dto.Position!.Ply);
        Assert.True(dto.Position.WhiteToMove);
        Assert.NotEmpty(dto.Position.Fen);

        // Das DTO trägt KEIN Feld mit dem Partiezug oder den Kandidaten — sonst wäre das Feature
        // im Netzwerk-Tab gelöst. (Sicherung gegen ein versehentlich ergänztes Property.)
        var json = System.Text.Json.JsonSerializer.Serialize(dto).ToLowerInvariant();
        Assert.DoesNotContain("e2e4", json);
        Assert.DoesNotContain("candidates", json);
        // `gameMoveHits` (ein Zähler) ist erlaubt — der ZUG darf nicht drin stehen.
        Assert.DoesNotContain("gamemovesan", json);
        Assert.DoesNotContain("gamemoveuci", json);
    }

    [Fact]
    public async Task Guess_exakterPartiezug_gibtPunkteUndSpieltDenGegenzugNach()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        var res = await _svc.GuessAsync(user.Id, session.Id, new GuessMoveRequest { Uci = "e2e4", AddSeconds = 12 });

        Assert.Equal("onlyMove", res.Grade);      // Alternative ist 0.9 schlechter → „einziger Zug"
        Assert.Equal(8, res.Points);
        Assert.Equal("e4", res.GameMoveSan);
        Assert.Equal("e5", res.ReplySan);          // Gegenzug automatisch nachgespielt
        Assert.Equal(8, res.Session.Points);
        Assert.Equal(10, res.Session.MaxPoints);
        Assert.Equal(1, res.Session.GameMoveHits);
        Assert.Equal(12, res.Session.SecondsSpent);
        Assert.Equal(2, res.Session.Position!.Ply);   // weiter beim nächsten eigenen Halbzug
    }

    [Fact]
    public async Task Guess_schwaechererZug_gibtAbzug()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });
        var pos = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 0);
        var alt = BrokerCandidates.FromJson(pos.CandidatesJson).Last().Uci;

        var res = await _svc.GuessAsync(user.Id, session.Id, new GuessMoveRequest { Uci = alt });

        Assert.Equal("muchWorse", res.Grade);
        Assert.Equal(-2, res.Points);
        Assert.Equal(-110, res.DiffCp);           // -80 gegen +30
    }

    [Fact]
    public async Task Passen_gibtNullPunkteAberKeineStrafe_undZeigtDenPartiezug()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        var res = await _svc.GuessAsync(user.Id, session.Id, new GuessMoveRequest { Uci = null });

        Assert.Null(res.Grade);
        Assert.Equal(0, res.Points);
        Assert.Equal("e4", res.GameMoveSan);      // jetzt darf er ihn sehen
        Assert.Equal(0, res.Session.MaxPoints);   // eine Passe zählt nicht ins Maximum
    }

    [Fact]
    public async Task UnmoeglicherZug_wirdAbgelehnt_stattGewertet()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.GuessAsync(user.Id, session.Id, new GuessMoveRequest { Uci = "e2e5" }));
        // Kein Zug protokolliert, die Sitzung steht unverändert.
        Assert.Empty(_db.GuessMoves);
    }

    [Fact]
    public async Task SchwarzRaten_beginntBeimErstenSchwarzenHalbzug()
    {
        var (user, analysis) = await SeedAsync();

        var dto = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = false, StartPly = 0,
        });

        Assert.Equal(1, dto.Position!.Ply);       // Ply 0 gehört Weiß
        Assert.False(dto.Position.WhiteToMove);
    }

    [Fact]
    public async Task NichtWertbareStellungenWerdenUebersprungen()
    {
        // Ply 2 ohne Kandidatenliste (Engine hat aufgegeben) → die Sitzung fragt Ply 4.
        var (user, analysis) = await SeedAsync();
        var ply2 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 2);
        ply2.CandidatesJson = "[]";
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });
        await _svc.GuessAsync(user.Id, session.Id, new GuessMoveRequest { Uci = "e2e4" });

        var state = await _svc.GetAsync(user.Id, session.Id);
        Assert.Equal(4, state!.Position!.Ply);
    }

    [Fact]
    public async Task AmEndeDerPartie_istDieSitzungFertig()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        foreach (var uci in new[] { "e2e4", "g1f3", "f1b5" })
            await _svc.GuessAsync(user.Id, session.Id, new GuessMoveRequest { Uci = uci });

        var state = await _svc.GetAsync(user.Id, session.Id);
        Assert.Equal("done", state!.Status);
        Assert.Null(state.Position);
        Assert.Equal(3, state.MovesPlayed);

        var review = await _svc.ReviewAsync(user.Id, session.Id);
        Assert.Equal(3, review!.Count);
        Assert.All(review, r => Assert.True(r.White));
        Assert.Equal("e4", review[0].GameSan);
        Assert.Equal("e4", review[0].PlayedSan);
    }

    [Fact]
    public async Task NochNichtAnalysiertePartie_laesstSichNichtStarten()
    {
        var (user, analysis) = await SeedAsync(analyzeAll: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.StartAsync(user.Id, new CreateGuessSessionRequest { GameAnalysisId = analysis.Id }));
    }

    [Fact]
    public async Task FremdeSitzungIstUnsichtbar()
    {
        var (owner, analysis) = await SeedAsync();
        var other = new AppUser { Username = "o", Email = "o@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(owner.Id, new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, StartPly = 0,
        });

        Assert.Null(await _svc.GetAsync(other.Id, session.Id));
        Assert.Null(await _svc.ReviewAsync(other.Id, session.Id));
        Assert.False(await _svc.DeleteAsync(other.Id, session.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.GuessAsync(other.Id, session.Id, new GuessMoveRequest { Uci = "e2e4" }));
        // Auch eine fremde ANALYSE lässt sich nicht bespielen.
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.StartAsync(other.Id, new CreateGuessSessionRequest { GameAnalysisId = analysis.Id }));
    }

    [Fact]
    public async Task Vorgabe_ueberspringtDieEroeffnung()
    {
        var (user, analysis) = await SeedAsync();
        var dto = await _svc.StartAsync(user.Id, new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });
        // Ohne StartPly wird die Eröffnung gezeigt statt abgefragt — Raten ab Zug 1 prüft Buchwissen.
        Assert.Equal(GuessSessionService.DefaultSkipPlies, dto.StartPly);
    }
}
