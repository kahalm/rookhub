using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using Microsoft.Extensions.Logging.Abstractions;
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
        _svc = new GuessSessionService(_db, new GuessStartPly(_db),
            new CommentSetService(_db, NullLogger<CommentSetService>.Instance));
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

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
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
        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        var res = await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = "e2e4", AddSeconds = 12 });

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
        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });
        var pos = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 0);
        var alt = BrokerCandidates.FromJson(pos.CandidatesJson).Last().Uci;

        var res = await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = alt });

        Assert.Equal("muchWorse", res.Grade);
        Assert.Equal(-2, res.Points);
        Assert.Equal(-110, res.DiffCp);           // -80 gegen +30
    }

    [Fact]
    public async Task Passen_gibtNullPunkteAberKeineStrafe_undZeigtDenPartiezug()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        var res = await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = null });

        Assert.Null(res.Grade);
        Assert.Equal(0, res.Points);
        Assert.Equal("e4", res.GameMoveSan);      // jetzt darf er ihn sehen
        Assert.Equal(0, res.Session.MaxPoints);   // eine Passe zählt nicht ins Maximum
    }

    [Fact]
    public async Task UnmoeglicherZug_wirdAbgelehnt_stattGewertet()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = "e2e5" }));
        // Kein Zug protokolliert, die Sitzung steht unverändert.
        Assert.Empty(_db.GuessMoves);
    }

    [Fact]
    public async Task SchwarzRaten_beginntBeimErstenSchwarzenHalbzug()
    {
        var (user, analysis) = await SeedAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
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

        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });
        await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = "e2e4" });

        var state = await _svc.GetAsync(GuessOwner.ForUser(user.Id), session.Id);
        Assert.Equal(4, state!.Position!.Ply);
    }

    [Fact]
    public async Task AmEndeDerPartie_istDieSitzungFertig()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        foreach (var uci in new[] { "e2e4", "g1f3", "f1b5" })
            await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = uci });

        var state = await _svc.GetAsync(GuessOwner.ForUser(user.Id), session.Id);
        Assert.Equal("done", state!.Status);
        Assert.Null(state.Position);
        Assert.Equal(3, state.MovesPlayed);

        var review = await _svc.ReviewAsync(GuessOwner.ForUser(user.Id), session.Id);
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
            _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest { GameAnalysisId = analysis.Id }));
    }

    [Fact]
    public async Task NochNichtGerechneteStellung_wirdNichtVerbrannt()
    {
        // Spielbar ist eine Partie schon ab der ERSTEN fertigen Stellung — die Sitzung kann der
        // Engine also davonlaufen. Frueher wurde so eine Stellung stumm mit 0 gewertet und nie
        // wieder gezeigt; der Nutzer arbeitete sich punktlos durch die halbe Partie.
        var (user, analysis) = await SeedAsync();
        var ply2 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 2);
        ply2.CandidatesJson = null;   // rechnet noch
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });
        await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = "e2e4" });

        var state = await _svc.GetAsync(GuessOwner.ForUser(user.Id), session.Id);
        Assert.Equal(2, state!.Position!.Ply);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = "g1f3" }));
        Assert.Single(_db.GuessMoves);   // der Halbzug ist NICHT verbraucht

        // Sobald die Engine nachgezogen hat, geht es normal weiter.
        ply2.CandidatesJson = "[{\"uci\":\"" + ply2.GameMoveUci + "\",\"cp\":30}]";
        await _db.SaveChangesAsync();
        var res = await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = ply2.GameMoveUci });
        Assert.Equal("gameMove", res.Grade);
    }

    [Fact]
    public async Task BewertungNachDemZug_zeigtMattAlsMatt()
    {
        // Regression: die Anzeige rechnete mit der VERGLEICHSZAHL (Matt = 1000 Bauern) und schrieb
        // „+997.00" statt „#-3" unter das Brett.
        var (user, analysis) = await SeedAsync();
        var ply1 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 1);
        ply1.CandidatesJson = "[{\"uci\":\"" + ply1.GameMoveUci + "\",\"mate\":3}]";
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });
        var res = await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = "e2e4" });

        Assert.Equal("#-3", res.EvalText);
    }

    [Fact]
    public async Task StartPly_wirdAufDiePartieBegrenzt()
    {
        // Ohne Deckel lief `start++` bei int.MaxValue in den negativen Bereich.
        var (user, analysis) = await SeedAsync();
        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = int.MaxValue,
        });
        Assert.True(dto.StartPly >= 0);
        Assert.True(dto.StartPly <= GameAnalysisDefaults.MaxPlies + 1);
    }

    [Fact]
    public async Task FremdeSitzungIstUnsichtbar()
    {
        var (owner, analysis) = await SeedAsync();
        var other = new AppUser { Username = "o", Email = "o@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(GuessOwner.ForUser(owner.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, StartPly = 0,
        });

        Assert.Null(await _svc.GetAsync(GuessOwner.ForUser(other.Id), session.Id));
        Assert.Null(await _svc.ReviewAsync(GuessOwner.ForUser(other.Id), session.Id));
        Assert.False(await _svc.DeleteAsync(GuessOwner.ForUser(other.Id), session.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.GuessAsync(GuessOwner.ForUser(other.Id), session.Id, new GuessMoveRequest { Uci = "e2e4" }));
        // Auch eine fremde ANALYSE lässt sich nicht bespielen.
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.StartAsync(GuessOwner.ForUser(other.Id), new CreateGuessSessionRequest { GameAnalysisId = analysis.Id }));
    }

    [Fact]
    public async Task Vorgabe_ueberspringtDieEroeffnung()
    {
        var (user, analysis) = await SeedAsync();
        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });
        // Ohne StartPly wird die Eröffnung gezeigt statt abgefragt — Raten ab Zug 1 prüft Buchwissen.
        Assert.Equal(GuessSessionService.DefaultSkipPlies, dto.StartPly);
    }
    // ---- Eroeffnung vor dem Einstieg (durchklickbar) ----

    [Fact]
    public async Task Start_WithSkippedOpening_DeliversTheGameSoFar()
    {
        var (user, analysis) = await SeedAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 4,   // ab 3.Bb5
        });

        Assert.Equal(4, dto.StartPly);
        Assert.Equal(4, dto.History.Count);                       // e4 e5 Nf3 Nc6
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6" }, dto.History.Select(m => m.San));
        Assert.Equal(new[] { 1, 1, 2, 2 }, dto.History.Select(m => m.MoveNumber));
        Assert.Equal(new[] { true, false, true, false }, dto.History.Select(m => m.White));

        // Jeder Eintrag traegt die Stellung NACH seinem Zug — die des letzten ist die Einstiegsstellung.
        Assert.Equal(dto.Position!.Fen, dto.History[^1].Fen);
        Assert.NotNull(dto.StartFen);
        Assert.StartsWith("rnbqkbnr/pppppppp", dto.StartFen);   // Grundstellung vor 1.e4
        Assert.All(dto.History, m => Assert.NotEqual(dto.StartFen, m.Fen));
    }

    [Fact]
    public async Task Start_FromTheFirstMove_HasNothingToBrowse()
    {
        var (user, analysis) = await SeedAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        Assert.Empty(dto.History);
        Assert.Null(dto.StartFen);
    }

    [Fact]
    public async Task List_LeavesTheHistoryOut()
    {
        var (user, analysis) = await SeedAsync();
        await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 4,
        });

        // Die Uebersicht braucht die Eroeffnung nicht — sie waere dort je Sitzung ein zweiter Satz Zeilen.
        var list = await _svc.ListAsync(GuessOwner.ForUser(user.Id));
        Assert.All(list, s => Assert.Empty(s.History));
    }

    // ---- Rueckblick: bester Zug + Bewertungen ----

    [Fact]
    public async Task Review_NamesTheBestMoveAndBothEvaluations()
    {
        var (user, analysis) = await SeedAsync();
        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, GuessWhite = true, StartPly = 0,
        });

        // Die Seed-Kandidatenliste fuehrt den Partiezug (+30) vor einer schwaecheren Alternative (-80),
        // der Partiezug IST hier also der beste Zug.
        var first = await _svc.GetAsync(GuessOwner.ForUser(user.Id), session.Id);
        var pos = await _db.GameAnalysisPositions.FirstAsync(x => x.GameAnalysisId == analysis.Id && x.Ply == 0);
        await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = pos.GameMoveUci });

        var review = await _svc.ReviewAsync(GuessOwner.ForUser(user.Id), session.Id);
        var row = Assert.Single(review!);
        Assert.Equal(pos.GameMoveSan, row.BestSan);          // hier deckungsgleich
        Assert.Equal("+0.30", row.BestEval);
        Assert.Equal("+0.30", row.GameEval);
        Assert.NotNull(first);
    }

    [Fact]
    public async Task Review_WithoutCandidates_LeavesTheExtrasEmpty()
    {
        var (user, analysis) = await SeedAsync(analyzeAll: false);
        // Ohne Kandidatenliste laesst sich die Sitzung nicht starten — also eine Stellung nachtragen.
        var p0 = await _db.GameAnalysisPositions.FirstAsync(x => x.GameAnalysisId == analysis.Id && x.Ply == 0);
        p0.CandidatesJson = "[]";                            // vorhanden, aber leer
        await _db.SaveChangesAsync();

        var session = new GuessSession { UserId = user.Id, GameAnalysisId = analysis.Id, GuessWhite = true, CurrentPly = 0 };
        session.Moves.Add(new GuessMove { Ply = 0, PlayedUci = null, SecondsSpent = 3 });
        _db.GuessSessions.Add(session);
        await _db.SaveChangesAsync();

        var review = await _svc.ReviewAsync(GuessOwner.ForUser(user.Id), session.Id);
        var row = Assert.Single(review!);
        Assert.Null(row.BestSan);
        Assert.Null(row.BestEval);
        Assert.Null(row.GameEval);
    }


    // ===== Ohne Anmeldung + kuratierter Bestand =========================================

    private const string AnonA = "11111111-2222-3333-4444-555555555555";
    private const string AnonB = "66666666-7777-8888-9999-aaaaaaaaaaaa";

    /// <summary>Eine fremde, NICHT freigegebene Analyse ist ohne Anmeldung unerreichbar — sonst
    /// wäre der kuratierte Bestand nur eine Empfehlung und jede private Partie mitspielbar.</summary>
    [Fact]
    public async Task StartAsync_Anonymous_PrivateAnalysis_Throws()
    {
        var (_, analysis) = await SeedAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.StartAsync(GuessOwner.ForAnonymous(AnonA),
                new CreateGuessSessionRequest { GameAnalysisId = analysis.Id }));
    }

    [Fact]
    public async Task StartAsync_Anonymous_PublicAnalysis_Works()
    {
        var (_, analysis) = await SeedAsync();
        analysis.IsPublic = true;
        await _db.SaveChangesAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForAnonymous(AnonA),
            new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });

        var row = await _db.GuessSessions.FirstAsync(s => s.Id == dto.Id);
        Assert.Null(row.UserId);
        Assert.Equal(AnonA, row.AnonymousSessionId);
    }

    /// <summary>Zwei Browser sind zwei Besitzer. Ohne diese Trennung liefe die Fortsetzung EINER
    /// Sitzung an jeden, der die Id errät — die Kennung ist hier die einzige Schranke.</summary>
    [Fact]
    public async Task AnonymousSessions_AreIsolated()
    {
        var (_, analysis) = await SeedAsync();
        analysis.IsPublic = true;
        await _db.SaveChangesAsync();

        var mine = await _svc.StartAsync(GuessOwner.ForAnonymous(AnonA),
            new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });

        Assert.Null(await _svc.GetAsync(GuessOwner.ForAnonymous(AnonB), mine.Id));
        Assert.Empty(await _svc.ListAsync(GuessOwner.ForAnonymous(AnonB)));
        Assert.Single(await _svc.ListAsync(GuessOwner.ForAnonymous(AnonA)));
        Assert.False(await _svc.DeleteAsync(GuessOwner.ForAnonymous(AnonB), mine.Id));
    }

    /// <summary>Und ein Konto sieht die anonymen Durchläufe nicht (und umgekehrt).</summary>
    [Fact]
    public async Task AnonymousSession_IsInvisibleToAccounts()
    {
        var (user, analysis) = await SeedAsync();
        analysis.IsPublic = true;
        await _db.SaveChangesAsync();

        var anon = await _svc.StartAsync(GuessOwner.ForAnonymous(AnonA),
            new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });

        Assert.Null(await _svc.GetAsync(GuessOwner.ForUser(user.Id), anon.Id));
        Assert.Empty(await _svc.ListAsync(GuessOwner.ForUser(user.Id)));
    }

    // ===== Seitenwahl: der Gewinner =====================================================

    /// <summary>Ohne Angabe übernimmt der Nutzer die Seite des GEWINNERS — darum geht es im
    /// kuratierten Bestand, und deshalb fragt die Auswahl dort nicht mehr nach der Seite.</summary>
    [Theory]
    [InlineData("1-0", true)]
    [InlineData("0-1", false)]
    public async Task StartAsync_WithoutSide_TakesWinnerFromResult(string result, bool expectWhite)
    {
        var (user, analysis) = await SeedAsync();
        analysis.Result = result;
        await _db.SaveChangesAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id),
            new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });

        Assert.Equal(expectWhite, dto.GuessWhite);
    }

    /// <summary>
    /// Kein Ergebnis in der Kopfzeile — der Normalfall bei den Meisterpartien aus Capablancas
    /// <i>Chess Fundamentals</i>, die dort nur <c>*</c> stehen haben. Dann entscheidet die
    /// BEWERTUNG der letzten gerechneten Stellung: Schwarz am Zug (ungerader Halbzug) mit +5 aus
    /// SEINER Sicht heißt, Schwarz hat gewonnen.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithoutSide_NoResult_TakesWinnerFromLastEval()
    {
        var (user, analysis) = await SeedAsync();
        analysis.Result = "*";
        var last = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == analysis.Id)
            .OrderByDescending(p => p.Ply).FirstAsync();
        Assert.True(last.Ply % 2 == 1);                       // Schwarz am Zug
        last.CandidatesJson = "[{\"uci\":\"" + last.GameMoveUci + "\",\"cp\":500}]";
        await _db.SaveChangesAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id),
            new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });

        Assert.False(dto.GuessWhite);
    }

    /// <summary>Ausgeglichene Schlussstellung: raten hilft niemandem, es bleibt bei Weiß.</summary>
    [Fact]
    public async Task StartAsync_WithoutSide_UndecidedEval_FallsBackToWhite()
    {
        var (user, analysis) = await SeedAsync();
        analysis.Result = null;
        await _db.SaveChangesAsync();   // Kandidaten stehen alle auf +0.30

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id),
            new CreateGuessSessionRequest { GameAnalysisId = analysis.Id });

        Assert.True(dto.GuessWhite);
    }

    /// <summary>Eine ausdrücklich gewählte Seite schlägt die Ableitung.</summary>
    [Fact]
    public async Task StartAsync_ExplicitSide_Wins()
    {
        var (user, analysis) = await SeedAsync();
        analysis.Result = "1-0";
        await _db.SaveChangesAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id),
            new CreateGuessSessionRequest { GameAnalysisId = analysis.Id, GuessWhite = false });

        Assert.False(dto.GuessWhite);
    }

    // ===== Kommentare der Partie ========================================================

    /// <summary>
    /// Bei einer Meisterpartie sind die Kommentare die eigentliche Lehre — sie muessen an den
    /// GESPIELTEN Zuegen haengen. Und nur dort: ein Kommentar am noch zu ratenden Zug waere die
    /// Loesung in Prosa.
    /// </summary>
    [Fact]
    public async Task History_traegtDieKommentareDerGespieltenZuege()
    {
        var user = new AppUser { Username = "k", Email = "k@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        var pgn = "[Event \"T\"]\n\n1. e4 {ein guter Anfang} e5 2. Nf3 {entwickelt und greift an} Nc6 "
                + "3. Bb5 {die spanische Partie} a6 *";
        var (header, plies) = GamePlies.Parse(pgn)!.Value;
        var analysis = new GameAnalysis
        {
            UserId = user.Id, Title = "Mit Kommentaren", Pgn = pgn, StartFen = header.StartFen,
            PlyCount = plies.Count, Status = GameAnalysisStatus.Done,
        };
        foreach (var p in plies)
            analysis.Positions.Add(new GameAnalysisPosition
            {
                Ply = p.Index, Fen = p.Fen, GameMoveUci = p.Uci, GameMoveSan = p.San,
                CandidatesJson = "[{\"uci\":\"" + p.Uci + "\",\"cp\":20}]", Depth = 20,
            });
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();

        // Ab Halbzug 4 raten: 0..3 sind gespielt und stehen im Verlauf.
        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, StartPly = 4, GuessWhite = true,
        });

        var byPly = dto.History.ToDictionary(h => h.Ply, h => h.Comment);
        Assert.Equal("ein guter Anfang", byPly[0]);          // 1. e4
        Assert.Null(byPly[1]);                                // 1... e5 ohne Kommentar
        Assert.Equal("entwickelt und greift an", byPly[2]);   // 2. Nf3
        Assert.Null(byPly[3]);                                // 2... Nc6

        // Der Kommentar zu 3. Bb5 gehoert zum noch zu ratenden Zug und darf NICHT dabei sein.
        Assert.DoesNotContain(dto.History, h => h.Comment == "die spanische Partie");
        Assert.DoesNotContain("spanische", System.Text.Json.JsonSerializer.Serialize(dto));
    }

    /// <summary>Eine Partie ohne Kommentare kostet nichts und liefert keine.</summary>
    [Fact]
    public async Task History_ohneKommentare_bleibtLeer()
    {
        var (user, analysis) = await SeedAsync();

        var dto = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, StartPly = 4, GuessWhite = true,
        });

        Assert.NotEmpty(dto.History);
        Assert.All(dto.History, h => Assert.Null(h.Comment));
    }

    /// <summary>
    /// Eine Stellung, in der die Engine den Partiezug NICHT unter ihren Kandidaten fuehrt, wird
    /// uebersprungen (ohne Bezugspunkt waere eine Wertung geraten) — aber das muss man SEHEN. Sonst
    /// spielt das Brett wortlos ueber Zuege hinweg; auf Dev gemeldet als „nach Bxe7 spielt er
    /// sofort 3 Zuege, warum wird Bd3 nicht abgefragt?".
    /// </summary>
    [Fact]
    public async Task History_nenntDenGrundFuerUebersprungeneZuege()
    {
        var (user, analysis) = await SeedAsync();
        // Halbzug 2 (Weiss, Nf3) unwertbar machen: Kandidatenliste ohne den Partiezug — UND die
        // Folgestellung aufgegeben, sonst liesse sich die Bewertung des Partiezuges von dort
        // ableiten und die Stellung waere spielbar (siehe der Test darunter).
        var p2 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 2);
        p2.CandidatesJson = "[{\"uci\":\"b1c3\",\"cp\":10}]";
        var p3 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 3);
        p3.CandidatesJson = "[]";
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, StartPly = 0, GuessWhite = true,
        });
        // Halbzug 0 raten; danach steht die Sitzung auf 4, weil 2 uebersprungen wurde.
        await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id, new GuessMoveRequest { Uci = "e2e4" });
        var state = await _svc.GetAsync(GuessOwner.ForUser(user.Id), session.Id);

        Assert.Equal(4, state!.Position!.Ply);
        var skipped = state.History.Single(h => h.Ply == 2);
        Assert.Equal("notScorable", skipped.Skipped);
        // Der geratene Zug und die Gegenseite tragen KEINEN Grund.
        Assert.Null(state.History.Single(h => h.Ply == 0).Skipped);
        Assert.Null(state.History.Single(h => h.Ply == 1).Skipped);
    }

    /// <summary>
    /// Steht der Partiezug nicht unter den besten fuenf, wird die Stellung TROTZDEM gespielt: seine
    /// Bewertung steht in der Folgestellung (beste Bewertung des Gegners, Vorzeichen gedreht).
    ///
    /// <para>Frueher wurde sie kommentarlos uebersprungen — und das traf ausgerechnet die
    /// interessanten Zuege: ein Opfer, das die Engine nicht unter ihre besten nimmt, ist genau der
    /// Zug, den man raten moechte.</para>
    /// </summary>
    [Fact]
    public async Task Guess_partiezugNichtUnterDenBesten_wirdTrotzdemGewertet()
    {
        var (user, analysis) = await SeedAsync();
        var p2 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 2);
        p2.CandidatesJson = "[{\"uci\":\"b1c3\",\"cp\":10}]";        // ohne den Partiezug (g1f3)
        var p3 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 3);
        p3.CandidatesJson = "[{\"uci\":\"b8c6\",\"cp\":-30}]";       // Gegner steht 0,3 schlechter
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(GuessOwner.ForUser(user.Id), new CreateGuessSessionRequest
        {
            GameAnalysisId = analysis.Id, StartPly = 2, GuessWhite = true,
        });

        Assert.Equal(2, session.Position!.Ply);   // NICHT uebersprungen

        // Der Partiezug selbst: er ist der gesuchte, also volle Wertung statt gar keiner.
        var result = await _svc.GuessAsync(GuessOwner.ForUser(user.Id), session.Id,
            new GuessMoveRequest { Uci = "g1f3" });

        Assert.NotNull(result.Grade);
        Assert.Contains(result.Grade, new[] { "gameMove", "onlyMove" });
    }

    /// <summary>Was noch nicht gerechnet ist, wird anders benannt als was nicht wertbar ist —
    /// das eine wird noch, das andere nie.</summary>
    [Fact]
    public async Task History_unterscheidetNochNichtGerechnetVonNichtWertbar()
    {
        var (user, analysis) = await SeedAsync();
        var p2 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 2);
        p2.CandidatesJson = null;                      // noch nicht gerechnet
        await _db.SaveChangesAsync();

        var session = new GuessSession
        {
            UserId = user.Id, GameAnalysisId = analysis.Id, GuessWhite = true,
            StartPly = 0, CurrentPly = 4,
        };
        _db.GuessSessions.Add(session);
        await _db.SaveChangesAsync();

        var state = await _svc.GetAsync(GuessOwner.ForUser(user.Id), session.Id);
        Assert.Equal("pending", state!.History.Single(h => h.Ply == 2).Skipped);
    }

    // ===== „Ich will den Partiezug finden" ==============================================

    /// <summary>
    /// Wer einen besseren Zug NICHT als erledigt gelten laesst, bekommt die Auskunft und die
    /// Aufgabe zurueck — gespeichert wird nichts, und der Partiezug bleibt geheim. Ihn hier
    /// mitzuschicken hiesse, die Aufgabe zu verraten, die man sich selbst gestellt hat.
    /// </summary>
    [Fact]
    public async Task Guess_abgelehnterZug_bleibtOhneWirkungUndOhneVerrat()
    {
        var (user, analysis) = await SeedAsync();
        var owner = GuessOwner.ForUser(user.Id);
        // Kandidatenliste so setzen, dass ein anderer Zug DEUTLICH besser ist als der Partiezug.
        var p0 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 0);
        p0.CandidatesJson = "[{\"uci\":\"d2d4\",\"cp\":120},{\"uci\":\"e2e4\",\"cp\":10}]";
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(owner, new CreateGuessSessionRequest
        { GameAnalysisId = analysis.Id, StartPly = 0, GuessWhite = true });

        var res = await _svc.GuessAsync(owner, session.Id,
            new GuessMoveRequest { Uci = "d2d4", AcceptBetter = false });

        Assert.False(res.Accepted);
        Assert.Equal("d4", res.PlayedSan);
        Assert.Empty(res.GameMoveSan);                       // kein Verrat
        Assert.Empty(res.GameMoveUci);
        Assert.Null(res.ReplySan);
        Assert.Equal(0, res.Points);
        Assert.Equal(0, res.Session.MovesPlayed);            // nichts protokolliert
        Assert.Equal(0, res.Session.Position!.Ply);          // dieselbe Aufgabe

        Assert.Empty(await _db.GuessMoves.Where(m => m.GuessSessionId == session.Id).ToListAsync());
    }

    /// <summary>Mit der Vorgabe (annehmen) zaehlt derselbe Zug ganz normal.</summary>
    [Fact]
    public async Task Guess_bessererZug_zaehltWennErlaubt()
    {
        var (user, analysis) = await SeedAsync();
        var owner = GuessOwner.ForUser(user.Id);
        var p0 = await _db.GameAnalysisPositions.FirstAsync(p => p.GameAnalysisId == analysis.Id && p.Ply == 0);
        p0.CandidatesJson = "[{\"uci\":\"d2d4\",\"cp\":120},{\"uci\":\"e2e4\",\"cp\":10}]";
        await _db.SaveChangesAsync();

        var session = await _svc.StartAsync(owner, new CreateGuessSessionRequest
        { GameAnalysisId = analysis.Id, StartPly = 0, GuessWhite = true });

        var res = await _svc.GuessAsync(owner, session.Id, new GuessMoveRequest { Uci = "d2d4" });

        Assert.True(res.Accepted);
        Assert.Equal("e4", res.GameMoveSan);
        Assert.Equal(1, res.Session.MovesPlayed);
    }

    /// <summary>Der PARTIEZUG selbst laesst sich nicht ablehnen — er ist ja das Ziel.</summary>
    [Fact]
    public async Task Guess_derPartiezug_wirdNieAbgelehnt()
    {
        var (user, analysis) = await SeedAsync();
        var owner = GuessOwner.ForUser(user.Id);
        var session = await _svc.StartAsync(owner, new CreateGuessSessionRequest
        { GameAnalysisId = analysis.Id, StartPly = 0, GuessWhite = true });

        var res = await _svc.GuessAsync(owner, session.Id,
            new GuessMoveRequest { Uci = "e2e4", AcceptBetter = false, AcceptSimilar = false });

        Assert.True(res.Accepted);
        Assert.Equal("e4", res.GameMoveSan);
    }
}
