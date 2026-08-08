using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Ähnlichkeitssuche über echte Repertoire-PGNs: Durchlauf, Materialschranke, Farbtausch,
/// „je Linie nur der beste Treffer", Zugriffsregel und die Wirkung der Voreinstellungen.
/// Die Metrik selbst steckt in <see cref="PositionSimilarityTests"/>.
/// </summary>
public class RepertoireSimilarityServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly RepertoireLineSource _lines;
    private readonly RepertoireSimilarityService _svc;

    public RepertoireSimilarityServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _lines = new RepertoireLineSource(_db, _cache);
        _svc = new RepertoireSimilarityService(_lines);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    // ─── Stellungen (siehe PositionSimilarityTests für die Klartext-Herleitung) ───

    /// <summary>Damengambit-Abtauschvariante nach 9.Qc2 Te8 — Karlsbader Struktur.</summary>
    private const string Carlsbad_QGD = "r1bqr1k1/pp1nbppp/2p2n2/3p2B1/3P4/2NBPN2/PPQ2PPP/R3K2R w KQ - 7 10";
    /// <summary>Najdorf nach 8.0-0 0-0.</summary>
    private const string Najdorf = "rnbq1rk1/1p2bppp/p2p1n2/4p3/4P3/1NN5/PPP1BPPP/R1BQ1RK1 w - - 4 9";
    /// <summary>Dieselbe Najdorf-Stellung mit vertauschten Farben (Spiegelung an der Mittellinie).</summary>
    private const string NajdorfMirrored = "r1bq1rk1/ppp1bppp/1nn5/4p3/4P3/P2P1N2/1P2BPPP/RNBQ1RK1 b - - 4 9";

    // ─── PGN-Linien ───────────────────────────────────────────────────────

    /// <summary>Nimzoindisch mit Übergang in die Karlsbader Struktur (Endstellung = gleiches
    /// Bauerngerüst wie <see cref="Carlsbad_QGD"/>, andere Figuren).</summary>
    private const string NimzoPgn =
        "[Event \"Repertoire\"]\n[White \"Nimzoindisch: Rubinstein mit Karlsbader Struktur\"]\n[Black \"Nimzoindisch\"]\n\n" +
        "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. e3 O-O 5. Bd3 d5 6. cxd5 exd5 7. Nge2 Re8 8. O-O c6 9. Ng3 Nbd7 *\n";

    /// <summary>Königsindisch, Mar-del-Plata-Aufbau (nach 8.d5 Se7) als Kapitel-Linie mit
    /// [FEN]-Header — gleiches Material wie die Karlsbader Stellungen, aber ein völlig anderes
    /// Bauernbild (blockiertes Zentrum d5/e4 gegen d6/e5).
    /// Bewusst MIT [FEN] statt aus der Grundstellung: sonst liefe die Linie durch dieselben
    /// Eröffnungszüge wie die Nimzo-Linie und der Vergleich träfe zufällig auf gemeinsame
    /// Anfangsstellungen statt auf den Königsinder.</summary>
    private const string KingsIndianPgn =
        "[Event \"Repertoire\"]\n[White \"Königsindisch: Mar del Plata\"]\n[Black \"Königsindisch\"]\n" +
        "[FEN \"r1bq1rk1/ppp1npbp/3p1np1/3Pp3/2P1P3/2N2N2/PP2BPPP/R1BQ1RK1 w - - 1 9\"]\n\n" +
        "9. Ne1 Nd7 10. Nd3 f5 11. f3 f4 *\n";

    /// <summary>Sizilianisch Najdorf.</summary>
    private const string NajdorfPgn =
        "[Event \"Repertoire\"]\n[White \"Najdorf: 6.Le2 e5\"]\n[Black \"Sizilianisch\"]\n\n" +
        "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be2 e5 7. Nb3 Be7 8. O-O O-O *\n";

    /// <summary>Damengambit-Abtauschvariante bis genau zur Anfragestellung.</summary>
    private const string CarlsbadPgn =
        "[Event \"Repertoire\"]\n[White \"Damengambit: Abtauschvariante\"]\n[Black \"Damengambit\"]\n\n" +
        "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. cxd5 exd5 5. Bg5 c6 6. e3 Be7 7. Bd3 O-O 8. Nf3 Nbd7 9. Qc2 Re8 *\n";

    /// <summary>Chessable-Stil: Linie, die MITTEN in der Partie beginnt ([FEN]-Header, keine Züge).
    /// Stellung = dieselbe Abtauschvariante nach der Auflösung des Zentrums (…c5, dxc5, Sxc5):
    /// Figuren stehen fast wie in <see cref="Carlsbad_QGD"/>, das Bauerngerüst ist verändert.</summary>
    private const string LiquidationPgn =
        "[Event \"Repertoire\"]\n[White \"Abtauschvariante: Zentrum aufgelöst\"]\n[Black \"Damengambit\"]\n" +
        "[FEN \"r1bqr1k1/pp2bppp/5n2/2np4/7B/2NBPN2/PPQ2PPP/R3K2R w KQ - 0 12\"]\n\n*\n";

    /// <summary>Turmendspiel als [FEN]-Linie (dient dem königslosen Diagramm-Test).</summary>
    private const string RookEndgamePgn =
        "[Event \"Repertoire\"]\n[White \"Turmendspiel: 4 gegen 3 am Königsflügel\"]\n[Black \"Endspiel\"]\n" +
        "[FEN \"8/5pk1/6p1/7p/7P/6P1/5PK1/R7 w - - 0 1\"]\n\n*\n";

    /// <summary>Stellung mit Materialungleichgewicht (SCHWARZ hat die Dame, Weiß nicht) als
    /// [FEN]-Linie — das exakte Spiegelbild von <see cref="WhiteHasTheQueen"/>.</summary>
    private const string BlackQueenPgn =
        "[Event \"Repertoire\"]\n[White \"Damengewinn: Schwarz hat die Dame\"]\n[Black \"Endspiel\"]\n" +
        "[FEN \"r2qk2r/pp3ppp/2n1pn2/3p4/3P4/2N1PN2/PP3PPP/R3K2R w KQkq - 0 12\"]\n\n*\n";

    /// <summary>Anfrage dazu: dieselbe Stellung mit vertauschten Farben — WEISS hat die Dame.</summary>
    private const string WhiteHasTheQueen = "r3k2r/pp3ppp/2n1pn2/3p4/3P4/2N1PN2/PP3PPP/R2QK2R w KQkq - 0 12";

    // ─── Aufbau-Helfer ────────────────────────────────────────────────────

    private async Task<int> AddUserAsync(string name)
    {
        var u = new AppUser { Username = name, Email = name + "@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> AddRepertoireAsync(int ownerId, string name, params string[] pgns)
    {
        var rep = new Repertoire { UserId = ownerId, Name = name, Kind = RepertoireKind.Opening };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        foreach (var pgn in pgns)
            _db.RepertoireFiles.Add(new RepertoireFile
            {
                RepertoireId = rep.Id,
                FileName = "rep.pgn",
                PgnContent = pgn,
                FileSize = pgn.Length,
            });
        await _db.SaveChangesAsync();
        return rep.Id;
    }

    private Task<SimilarPositionsResultDto> FindAsync(int userId, string fen, Action<SimilarPositionsRequestDto>? tweak = null)
    {
        var dto = new SimilarPositionsRequestDto { Fen = fen };
        tweak?.Invoke(dto);
        return _svc.FindAsync(userId, dto, CancellationToken.None);
    }

    // ─── Tests ────────────────────────────────────────────────────────────

    /// <summary>Gleiches Bauerngerüst aus einer anderen Eröffnung (Karlsbader Struktur via
    /// Nimzoindisch) muss hoch punkten — samt Aufschlüsselung.</summary>
    [Fact]
    public async Task SameStructureFromAnotherOpening_IsFoundWithHighScore()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", NimzoPgn);

        var res = await FindAsync(user, Carlsbad_QGD);

        var hit = Assert.Single(res.Matches);
        Assert.Equal("Nimzoindisch: Rubinstein mit Karlsbader Struktur", hit.LineName);
        Assert.Equal("Nimzoindisch", hit.Chapter);
        Assert.Equal(0, hit.GameIndex);
        Assert.True(hit.Ply > 0, "Treffer liegt auf der Hauptlinie");
        Assert.False(hit.Mirrored);
        Assert.True(hit.Score >= 90, $"Score {hit.Score}");
        Assert.Equal(100, hit.Breakdown.Pawns);      // identisches Gerüst
        Assert.Equal(100, hit.Breakdown.Material);
        Assert.NotEmpty(hit.Fen);
        Assert.Equal("ausgewogen", res.Preset);
        Assert.Equal(RepertoireSimilarityService.DefaultMinScoreAusgewogen, res.MinScore);
        Assert.Equal(25, res.Limit);
        Assert.True(res.Compared > 0);
    }

    /// <summary>Gleiches Material, deutlich anderes Bauernbild → deutlich weiter hinten.</summary>
    [Fact]
    public async Task DifferentPawnPicture_RanksClearlyBelow()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", NimzoPgn, KingsIndianPgn);

        var res = await FindAsync(user, Carlsbad_QGD, d => d.MinScore = 0);

        Assert.Equal(2, res.Matches.Count);
        var carlsbad = res.Matches[0];
        var kid = res.Matches[1];
        Assert.StartsWith("Nimzoindisch", carlsbad.LineName);
        Assert.StartsWith("Königsindisch", kid.LineName);
        Assert.True(kid.Breakdown.Pawns < 60, $"Bauernwert {kid.Breakdown.Pawns}");
        Assert.True(kid.Score < carlsbad.Score - 20, $"{kid.Score} vs {carlsbad.Score}");
    }

    /// <summary>Farbtausch: die Anfrage ist die farbvertauschte Najdorf-Stellung („so sieht das von
    /// der Englisch-Seite aus") — sie muss die Najdorf-Linie mit <c>mirrored=true</c> finden.</summary>
    [Fact]
    public async Task ColourSwappedPosition_IsFoundAsMirroredHit()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Schwarz-Repertoire", NajdorfPgn);

        var res = await FindAsync(user, NajdorfMirrored);

        var hit = Assert.Single(res.Matches);
        Assert.True(hit.Mirrored);
        Assert.Equal(100, hit.Score);
        Assert.Equal(Najdorf.Split(' ')[0], hit.Fen.Split(' ')[0]);   // FEN ungespiegelt, wie im Repertoire
        Assert.Equal(16, hit.Ply);
    }

    [Fact]
    public async Task MirroringCanBeSwitchedOff()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Schwarz-Repertoire", NajdorfPgn);

        var res = await FindAsync(user, NajdorfMirrored, d => { d.IncludeMirrored = false; d.MinScore = 0; });

        Assert.All(res.Matches, m => Assert.False(m.Mirrored));
        Assert.DoesNotContain(res.Matches, m => m.Score == 100);
    }

    /// <summary>
    /// Die Materialschranke darf die SPIEGELSUCHE nicht kippen: die Anfrage hat die Dame, die
    /// Repertoire-Stellung ist ihr exaktes Spiegelbild (dort hat Schwarz sie). Farbweise geprüft ist
    /// der Unterschied 9 Bauerneinheiten — aber eben nur gegen die UNGESPIEGELTE Anfrage. Solange
    /// die Schranke dort geprüft wurde, fand keine Stellung mit Materialungleichgewicht mehr ihr
    /// eigenes Spiegelbild (Befund 1 der Gegenlesung).
    /// </summary>
    [Fact]
    public async Task MaterialGate_DoesNotBlockTheMirroredComparison()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Endspiel-Repertoire", BlackQueenPgn);

        var res = await FindAsync(user, WhiteHasTheQueen);

        var hit = Assert.Single(res.Matches);
        Assert.True(hit.Mirrored, "der Treffer stammt aus dem farbvertauschten Vergleich");
        Assert.Equal(100, hit.Score);
        Assert.Equal(1, res.Compared);
        Assert.Equal("Damengewinn: Schwarz hat die Dame", hit.LineName);
    }

    /// <summary>Gegenprobe: ohne Farbtausch bleibt die farbweise Schranke scharf — dann ist das
    /// Materialungleichgewicht ein Ausschlussgrund und es wird gar nicht erst verglichen.</summary>
    [Fact]
    public async Task MaterialGate_WithoutMirroring_StillRejectsTheOppositeQueen()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Endspiel-Repertoire", BlackQueenPgn);

        var res = await FindAsync(user, WhiteHasTheQueen, d => { d.IncludeMirrored = false; d.MinScore = 0; });

        Assert.Empty(res.Matches);
        Assert.Equal(0, res.Compared);
    }

    /// <summary>Materialschranke: die Anfrage ist die Najdorf-Stellung ohne die schwarze Dame.
    /// Alles andere ist identisch — trotzdem darf nichts gefunden werden (auch nicht bei minScore 0),
    /// und es darf gar nicht erst verglichen werden.</summary>
    [Fact]
    public async Task MaterialGate_QueenDown_YieldsNoMatchAtAll()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Schwarz-Repertoire", NajdorfPgn);
        const string najdorfWithoutBlackQueen = "rnb2rk1/1p2bppp/p2p1n2/4p3/4P3/1NN5/PPP1BPPP/R1BQ1RK1 w - - 4 9";

        var res = await FindAsync(user, najdorfWithoutBlackQueen, d => d.MinScore = 0);

        Assert.Empty(res.Matches);
        Assert.Equal(0, res.Compared);
    }

    /// <summary>Illegale Chessable-Diagramm-FEN OHNE König darf nicht werfen — und muss trotzdem
    /// suchen können: hier gegen dasselbe Turmendspiel mit Königen.</summary>
    [Fact]
    public async Task KinglessDiagramFen_DoesNotThrowAndStillMatches()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Endspiel-Repertoire", RookEndgamePgn);
        const string kinglessDiagram = "8/5p2/6p1/7p/7P/6P1/5P2/R7 w - - 0 1";

        var res = await FindAsync(user, kinglessDiagram);

        var hit = Assert.Single(res.Matches);
        Assert.Equal("Turmendspiel: 4 gegen 3 am Königsflügel", hit.LineName);
        Assert.Equal(0, hit.Ply);                       // [FEN]-Startstellung der Linie
        Assert.Equal(100, hit.Breakdown.Pawns);
        Assert.Null(hit.Breakdown.King);                // ohne Könige NICHT anwendbar (früher: 100 geschenkt)
    }

    /// <summary>Die identische Stellung ist kein „ähnlicher" Treffer — dafür gibt es die exakte
    /// Stellungssuche. Aus einer Linie kommt außerdem höchstens EIN Treffer.</summary>
    [Fact]
    public async Task IdenticalPositionIsExcluded_AndOnlyOneHitPerLine()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", CarlsbadPgn);

        var res = await FindAsync(user, Carlsbad_QGD, d => d.MinScore = 0);

        var hit = Assert.Single(res.Matches);           // eine Linie → ein Treffer
        Assert.NotEqual(Carlsbad_QGD.Split(' ')[0], hit.Fen.Split(' ')[0]);
        Assert.All(res.Matches, m => Assert.NotEqual(Carlsbad_QGD.Split(' ')[0], m.Fen.Split(' ')[0]));
    }

    /// <summary>Dieselbe Trefferliste, andere Reihenfolge: „struktur" stellt das gleiche Bauernbild
    /// nach vorn, „stellungsbild" die gleiche Figurenstellung.</summary>
    [Fact]
    public async Task Presets_OrderTheSameHitListDifferently()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", NimzoPgn, LiquidationPgn);

        var structur = await FindAsync(user, Carlsbad_QGD, d => { d.Preset = "struktur"; d.MinScore = 0; });
        var bild = await FindAsync(user, Carlsbad_QGD, d => { d.Preset = "stellungsbild"; d.MinScore = 0; });

        Assert.Equal(2, structur.Matches.Count);
        Assert.Equal(2, bild.Matches.Count);
        Assert.StartsWith("Nimzoindisch", structur.Matches[0].LineName);          // gleiches Gerüst
        Assert.StartsWith("Abtauschvariante", bild.Matches[0].LineName);          // gleiche Figuren
        Assert.Equal("struktur", structur.Preset);
        Assert.Equal("stellungsbild", bild.Preset);
    }

    /// <summary>Die Voreinstellung bringt IHRE Schwelle mit — eine globale Zahl kann es nicht geben,
    /// weil die drei Gewichtungen verschiedene Wertebereiche erzeugen (Befund 2 der Gegenlesung:
    /// die 70 lag in „stellungsbild" mitten im unverwandten Feld). Ein ausdrücklich gesetzter
    /// <c>minScore</c> schlägt den Default weiterhin.</summary>
    [Fact]
    public async Task DefaultMinScore_ComesFromThePreset_AndIsOverridable()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", NimzoPgn);

        Assert.Equal(67, (await FindAsync(user, Carlsbad_QGD, d => d.Preset = "struktur")).MinScore);
        Assert.Equal(75, (await FindAsync(user, Carlsbad_QGD, d => d.Preset = "ausgewogen")).MinScore);
        Assert.Equal(79, (await FindAsync(user, Carlsbad_QGD, d => d.Preset = "stellungsbild")).MinScore);
        Assert.Equal(75, (await FindAsync(user, Carlsbad_QGD)).MinScore);                       // ohne Angabe
        Assert.Equal(75, (await FindAsync(user, Carlsbad_QGD, d => d.Preset = "quatsch")).MinScore);

        var explicitly = await FindAsync(user, Carlsbad_QGD, d => { d.Preset = "stellungsbild"; d.MinScore = 40; });
        Assert.Equal(40, explicitly.MinScore);
    }

    [Fact]
    public async Task RepertoireIdsFilter_RestrictsTheSearch()
    {
        var user = await AddUserAsync("u1");
        var nimzo = await AddRepertoireAsync(user, "A Nimzo", NimzoPgn);
        await AddRepertoireAsync(user, "B Königsindisch", KingsIndianPgn);

        var all = await FindAsync(user, Carlsbad_QGD, d => d.MinScore = 0);
        var onlyNimzo = await FindAsync(user, Carlsbad_QGD, d => { d.MinScore = 0; d.RepertoireIds = new List<int> { nimzo }; });

        Assert.Equal(2, all.Matches.Count);
        var hit = Assert.Single(onlyNimzo.Matches);
        Assert.Equal(nimzo, hit.RepertoireId);
        Assert.Equal("A Nimzo", hit.RepertoireName);
    }

    [Fact]
    public async Task ForeignRepertoiresAreNotSearched_SharedOnesAre()
    {
        var me = await AddUserAsync("me");
        var other = await AddUserAsync("other");
        var foreign = await AddRepertoireAsync(other, "Fremd", NimzoPgn);

        var before = await FindAsync(me, Carlsbad_QGD, d => d.MinScore = 0);
        Assert.Empty(before.Matches);

        // Auch eine ausdrücklich angefragte, nicht lesbare Id bringt nichts.
        var forced = await FindAsync(me, Carlsbad_QGD, d => { d.MinScore = 0; d.RepertoireIds = new List<int> { foreign }; });
        Assert.Empty(forced.Matches);

        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = foreign, OwnerId = other, RecipientId = me });
        await _db.SaveChangesAsync();
        _lines.Invalidate(me);      // in Produktion macht das RepertoireService beim Teilen

        var after = await FindAsync(me, Carlsbad_QGD, d => d.MinScore = 0);
        Assert.Single(after.Matches);
    }

    [Fact]
    public async Task MinScore_FiltersWeakHits()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", NimzoPgn, KingsIndianPgn);

        var loose = await FindAsync(user, Carlsbad_QGD, d => d.MinScore = 0);
        var strict = await FindAsync(user, Carlsbad_QGD, d => d.MinScore = 90);

        Assert.Equal(2, loose.Matches.Count);
        var hit = Assert.Single(strict.Matches);
        Assert.StartsWith("Nimzoindisch", hit.LineName);
        Assert.True(hit.Score >= 90);
    }

    [Fact]
    public async Task LimitAndMinScore_AreClamped()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", NimzoPgn, KingsIndianPgn);

        var res = await FindAsync(user, Carlsbad_QGD, d => { d.Limit = 999; d.MinScore = 500; });
        Assert.Equal(100, res.Limit);
        Assert.Equal(100, res.MinScore);

        var tiny = await FindAsync(user, Carlsbad_QGD, d => { d.Limit = 1; d.MinScore = -5; });
        Assert.Equal(1, tiny.Limit);
        Assert.Equal(0, tiny.MinScore);
        Assert.Single(tiny.Matches);
    }

    /// <summary>Standardmäßig wird die Seite am Zug NICHT gefiltert (Strukturvergleich);
    /// mit <c>sameSideToMove</c> schon.</summary>
    [Fact]
    public async Task SameSideToMove_IsOptional()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Endspiel-Repertoire", RookEndgamePgn);
        // Dasselbe Turmendspiel als königsloses Diagramm, aber mit SCHWARZ am Zug — die
        // Repertoire-Stellung hat Weiß am Zug.
        const string blackToMove = "8/5p2/6p1/7p/7P/6P1/5P2/R7 b - - 0 1";

        // Farbtausch hier aus: beim gespiegelten Vergleich dreht sich auch die Seite am Zug mit,
        // ein Spiegeltreffer würde den Filter also zu Recht passieren und den Test verwischen.
        var loose = await FindAsync(user, blackToMove, d => d.IncludeMirrored = false);
        var strict = await FindAsync(user, blackToMove, d => { d.IncludeMirrored = false; d.SameSideToMove = true; });

        Assert.Single(loose.Matches);
        Assert.Empty(strict.Matches);
    }

    [Fact]
    public async Task EmptyBoard_ReturnsNothing()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", NimzoPgn);

        var res = await FindAsync(user, "8/8/8/8/8/8/8/8 w - - 0 1", d => d.MinScore = 0);

        Assert.Empty(res.Matches);
    }

    // ─── Zug-Treffer („ich erwäge hier 9.Sbd2 — wo geht der noch?") ───────
    //
    // Grundstellung aller drei Linien: Spanisch, Anti-Marshall nach 8.d3 0-0. Dort sind BEIDE
    // Springerzüge nach d2 legal (b1-d2 und f3-d2) — genau die Konstellation, in der die
    // SAN-Schreibweise auseinandergeht (`Nbd2` vs. `Nfd2` vs. `Nd2`) und ein Textvergleich
    // still danebengreifen würde.

    /// <summary>Anfrage: dieselbe Spanisch-Stellung, aber mit Kh1 und h6 — ähnlich, nicht identisch
    /// (die identische Stellung wäre kein „ähnlicher" Treffer). Weiß am Zug, Sb1-d2 ist möglich.</summary>
    private const string RuyQuery = "r1bq1rk1/2p1bpp1/p1np1n1p/1p2p3/4P3/1B1P1N2/PPP2PPP/RNBQR2K w - - 0 10";

    private const string RuyPrefix =
        "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6 8. d3 O-O ";

    /// <summary>Repertoire spielt dort 9.Sbd2 — das ist der EXAKTE Zug (b1→d2).</summary>
    private const string RuyKnightB1Pgn =
        "[Event \"Repertoire\"]\n[White \"Spanisch: Anti-Marshall mit 9.Sbd2\"]\n[Black \"Spanisch\"]\n\n" + RuyPrefix + "9. Nbd2 Na5 *\n";

    /// <summary>Repertoire spielt dort 9.Sfd2 — dasselbe Zielfeld, andere Herkunft (schwächere Stufe).</summary>
    private const string RuyKnightF3Pgn =
        "[Event \"Repertoire\"]\n[White \"Spanisch: Umgruppierung mit 9.Sfd2\"]\n[Black \"Spanisch\"]\n\n" + RuyPrefix + "9. Nfd2 Nb8 *\n";

    /// <summary>Repertoire spielt dort 9.a4 — Sbd2 wäre LEGAL, ist aber nicht der Repertoirezug.</summary>
    private const string RuyPawnPushPgn =
        "[Event \"Repertoire\"]\n[White \"Spanisch: Anti-Marshall mit 9.a4\"]\n[Black \"Spanisch\"]\n\n" + RuyPrefix + "9. a4 bxa4 *\n";

    /// <summary>Repertoire spielt dort 9.Sfd2, führt 9.Sbd2 aber als VARIANTE an dieser Stelle.</summary>
    private const string RuyVariationPgn =
        "[Event \"Repertoire\"]\n[White \"Spanisch: 9.Sfd2 nebst Variante\"]\n[Black \"Spanisch\"]\n\n" + RuyPrefix + "9. Nfd2 (9. Nbd2 Na5) Nb8 *\n";

    private static SimilarMoveInputDto FromTo(string from, string to, string? promotion = null)
        => new() { From = from, To = to, Promotion = promotion };

    /// <summary>Der exakte Repertoirezug schlägt „gleiche Figurenart aufs gleiche Zielfeld", und
    /// beides schlägt „dort wird etwas anderes gespielt" — obwohl alle drei Linien dieselbe
    /// Stellung erreichen und deshalb denselben Stellungswert haben.</summary>
    [Fact]
    public async Task MoveHit_ExactBeatsSameTarget_BeatsNoMove()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyKnightB1Pgn, RuyKnightF3Pgn, RuyPawnPushPgn);

        var res = await FindAsync(user, RuyQuery, d => d.Move = FromTo("b1", "d2"));

        Assert.Equal(3, res.Matches.Count);
        var exact = res.Matches[0];
        var sameTarget = res.Matches[1];
        var none = res.Matches[2];

        Assert.StartsWith("Spanisch: Anti-Marshall mit 9.Sbd2", exact.LineName);
        Assert.Equal("exact", exact.MoveMatch);
        Assert.Equal("Nbd2", exact.MoveSan);      // SAN aus dem PGN, verglichen wurde b1→d2
        Assert.Equal("b1", exact.MoveFrom);
        Assert.Equal("d2", exact.MoveTo);

        Assert.StartsWith("Spanisch: Umgruppierung mit 9.Sfd2", sameTarget.LineName);
        Assert.Equal("sameTarget", sameTarget.MoveMatch);
        Assert.Equal("f3", sameTarget.MoveFrom);
        Assert.Equal("d2", sameTarget.MoveTo);

        Assert.Null(none.MoveMatch);
        Assert.Equal(none.PositionScore, none.Score);     // ohne Treffer kein Bonus

        // Stellungswert identisch (dieselbe Stellung), nur der Zug-Bonus trennt sie.
        Assert.Equal(exact.PositionScore, sameTarget.PositionScore);
        Assert.True(exact.Score > sameTarget.Score && sameTarget.Score > none.Score,
            $"{exact.Score} > {sameTarget.Score} > {none.Score}");
        Assert.Equal("b1", res.Move!.From);
        Assert.Equal("d2", res.Move.To);
    }

    /// <summary>Verglichen wird from→to, NICHT die SAN-Zeichenkette: die Anfrage schreibt den Zug
    /// als „Nb1d2", das Repertoire als „Nbd2" — derselbe Zug. Ein Textvergleich hätte das still
    /// verfehlt (dieselbe Fehlerklasse wie beim Linien-Hash im August).</summary>
    [Fact]
    public async Task MoveHit_ComparesFromAndTo_NotTheSanSpelling()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyKnightB1Pgn);

        var res = await FindAsync(user, RuyQuery, d => d.Move = new SimilarMoveInputDto { San = "Nb1d2" });

        var hit = Assert.Single(res.Matches);
        Assert.Equal("exact", hit.MoveMatch);
        Assert.Equal("Nbd2", hit.MoveSan);
        Assert.Equal("b1", res.Move!.From);
        Assert.Equal("d2", res.Move.To);
        Assert.Equal("n", res.Move.Piece);
    }

    /// <summary>„Der Zug ist dort der Repertoirezug" heißt Hauptzug ODER Variante an dieser Stelle —
    /// hier steht 9.Sbd2 in Klammern hinter 9.Sfd2.</summary>
    [Fact]
    public async Task MoveHit_AlsoCountsAVariationAtThatPoint()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyVariationPgn);

        var res = await FindAsync(user, RuyQuery, d => d.Move = FromTo("b1", "d2"));

        var hit = Assert.Single(res.Matches);
        Assert.Equal("exact", hit.MoveMatch);
        Assert.Equal("Nbd2", hit.MoveSan);
    }

    /// <summary>Legalität genügt NICHT: in der a4-Linie wäre Sbd2 spielbar, das Repertoire spielt es
    /// dort aber nicht — kein Treffer, kein Bonus.</summary>
    [Fact]
    public async Task MoveHit_LegalButNotPlayed_IsNoHit()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyPawnPushPgn);

        var res = await FindAsync(user, RuyQuery, d => d.Move = FromTo("b1", "d2"));

        var hit = Assert.Single(res.Matches);
        Assert.Null(hit.MoveMatch);
        Assert.Equal(hit.PositionScore, hit.Score);
        Assert.NotNull(hit.MoveSan);              // gemeldet wird trotzdem, wie es dort weitergeht
    }

    /// <summary>Der Bonus ist ein Lücken-Schluss: er holt die Hälfte (bzw. ein Viertel) des
    /// FEHLENDEN Rests auf und kann deshalb nie über 100 laufen; beide Zahlen bleiben sichtbar.</summary>
    [Fact]
    public async Task MoveHit_BonusClosesHalfTheGap_AndBothNumbersStayVisible()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyKnightB1Pgn, RuyKnightF3Pgn);

        var res = await FindAsync(user, RuyQuery, d => d.Move = FromTo("b1", "d2"));

        var exact = res.Matches[0];
        var sameTarget = res.Matches[1];
        // ±1, weil in der Antwort beide Werte gerundet stehen (gerechnet wird ungerundet).
        Assert.InRange(exact.Score, exact.PositionScore + 0.50 * (100 - exact.PositionScore) - 1,
                                    exact.PositionScore + 0.50 * (100 - exact.PositionScore) + 1);
        Assert.InRange(sameTarget.Score, sameTarget.PositionScore + 0.25 * (100 - sameTarget.PositionScore) - 1,
                                         sameTarget.PositionScore + 0.25 * (100 - sameTarget.PositionScore) + 1);
        Assert.True(exact.Score <= 100);
        Assert.True(exact.PositionScore < exact.Score);
    }

    /// <summary>Ohne <c>onlyWithMove</c> ist der Zug ein Bonus, kein Filter; mit dem Schalter
    /// verschwinden die Treffer ohne passenden Zug.</summary>
    [Fact]
    public async Task OnlyWithMove_HidesHitsWithoutTheMove()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyKnightB1Pgn, RuyPawnPushPgn);

        var bonus = await FindAsync(user, RuyQuery, d => d.Move = FromTo("b1", "d2"));
        var filtered = await FindAsync(user, RuyQuery, d => { d.Move = FromTo("b1", "d2"); d.OnlyWithMove = true; });

        Assert.Equal(2, bonus.Matches.Count);
        Assert.False(bonus.OnlyWithMove);
        var hit = Assert.Single(filtered.Matches);
        Assert.Equal("exact", hit.MoveMatch);
        Assert.True(filtered.OnlyWithMove);
    }

    /// <summary>Ohne Zug in der Anfrage werden die Fortsetzungen gar nicht erst aufgelöst (das kostet
    /// je Stellung einen Zuggenerator-Lauf) — die Zug-Felder bleiben leer.</summary>
    [Fact]
    public async Task WithoutAMove_NoMoveFieldsAreReported()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyKnightB1Pgn);

        var res = await FindAsync(user, RuyQuery);

        var hit = Assert.Single(res.Matches);
        Assert.Null(res.Move);
        Assert.Null(hit.MoveMatch);
        Assert.Null(hit.MoveSan);
        Assert.Equal(hit.PositionScore, hit.Score);
    }

    /// <summary>Ein unauflösbarer Zug wird ignoriert (und macht <c>onlyWithMove</c> wirkungslos)
    /// statt still eine leere Liste zu liefern — <c>move: null</c> in der Antwort zeigt es an.</summary>
    [Fact]
    public async Task UnresolvableMove_IsIgnoredInsteadOfEmptyingTheList()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyKnightB1Pgn);

        var res = await FindAsync(user, RuyQuery, d =>
        {
            d.Move = new SimilarMoveInputDto { San = "?!?" };
            d.OnlyWithMove = true;
        });

        Assert.Null(res.Move);
        Assert.False(res.OnlyWithMove);
        Assert.Single(res.Matches);
    }

    /// <summary>
    /// Ein Zug NUR mit Zielfeld (kein Ausgangsfeld, kein SAN) ist nicht auflösbar: ohne Herkunft
    /// bleibt auch die Figurenart unbekannt, damit scheitert <c>exact</c> am fehlenden From und
    /// <c>sameTarget</c> an der fehlenden Figurenart — der Zug könnte NIE treffen. Früher galt er
    /// trotzdem als aufgelöst; mit <c>onlyWithMove</c> kam dann still eine leere Liste zurück, deren
    /// Ursache nirgends zu sehen war. Jetzt meldet die Antwort <c>move: null</c> und der Schalter
    /// bleibt wirkungslos (Befund 4 der Gegenlesung).
    /// </summary>
    [Fact]
    public async Task MoveWithTargetSquareOnly_IsNotResolvable()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "Weiß-Repertoire", RuyKnightB1Pgn);

        var res = await FindAsync(user, RuyQuery, d =>
        {
            d.Move = new SimilarMoveInputDto { To = "d2" };   // nur das Zielfeld
            d.OnlyWithMove = true;
        });

        Assert.Null(res.Move);                 // sichtbar: der Zug wurde nicht verwendet
        Assert.False(res.OnlyWithMove);        // … und der Filter greift deshalb nicht
        var hit = Assert.Single(res.Matches);  // statt einer stillen leeren Liste
        Assert.Null(hit.MoveMatch);

        // Gegenprobe: mit Ausgangsfeld ist derselbe Zug auflösbar und trifft.
        var withFrom = await FindAsync(user, RuyQuery, d => { d.Move = FromTo("b1", "d2"); d.OnlyWithMove = true; });
        Assert.Equal("b1", withFrom.Move!.From);
        Assert.True(withFrom.OnlyWithMove);
        Assert.Equal("exact", Assert.Single(withFrom.Matches).MoveMatch);

        // Und ein Zielfeld MIT SAN fällt auf die SAN-Auflösung zurück (die liefert die Herkunft).
        var withSan = await FindAsync(user, RuyQuery, d =>
        {
            d.Move = new SimilarMoveInputDto { To = "d2", San = "Nbd2" };
            d.OnlyWithMove = true;
        });
        Assert.Equal("b1", withSan.Move!.From);
        Assert.Equal("exact", Assert.Single(withSan.Matches).MoveMatch);
    }

    /// <summary>Umwandlungen: verglichen wird from→to PLUS Umwandlungsfigur. Dieselbe Stellung,
    /// zwei Linien — eine wandelt in die Dame um, die andere in den Springer.</summary>
    [Fact]
    public async Task MoveHit_PromotionPieceIsPartOfTheComparison()
    {
        var user = await AddUserAsync("u1");
        const string promoteQueen =
            "[Event \"Repertoire\"]\n[White \"Endspiel: Umwandlung in die Dame\"]\n[Black \"Endspiel\"]\n" +
            "[FEN \"8/P6k/8/8/8/8/7K/8 w - - 0 1\"]\n\n1. a8=Q *\n";
        const string promoteKnight =
            "[Event \"Repertoire\"]\n[White \"Endspiel: Umwandlung in den Springer\"]\n[Black \"Endspiel\"]\n" +
            "[FEN \"8/P6k/8/8/8/8/7K/8 w - - 0 1\"]\n\n1. a8=N *\n";
        await AddRepertoireAsync(user, "Endspiel-Repertoire", promoteQueen, promoteKnight);
        // Anfrage: dieselbe Stellung, schwarzer König am anderen Flügel (ähnlich, nicht identisch).
        const string query = "8/P7/8/8/8/8/1k5K/8 w - - 0 1";

        var res = await FindAsync(user, query, d => d.Move = FromTo("a7", "a8", "q"));

        Assert.Equal(2, res.Matches.Count);
        var queen = Assert.Single(res.Matches, m => m.LineName.Contains("Dame"));
        var knight = Assert.Single(res.Matches, m => m.LineName.Contains("Springer"));
        Assert.Equal("exact", queen.MoveMatch);
        Assert.Equal("q", queen.MovePromotion);
        Assert.Null(knight.MoveMatch);            // gleiches Feld, falsche Umwandlungsfigur
        Assert.True(queen.Score > knight.Score);
    }

    /// <summary>Beim farbvertauschten Treffer wird auch der Anfragezug gespiegelt — sonst zeigte die
    /// Spiegelsuche nie einen Zug-Treffer. Anfrage ist die gespiegelte Najdorf-Stellung.</summary>
    [Fact]
    public async Task MoveHit_MirroredHit_MirrorsTheQueriedMove()
    {
        var user = await AddUserAsync("u1");
        // Najdorf bis 8...0-0, danach 9.Le3 — der Repertoirezug ist c1→e3.
        const string najdorfBe3 =
            "[Event \"Repertoire\"]\n[White \"Najdorf: 9.Le3\"]\n[Black \"Sizilianisch\"]\n\n" +
            "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be2 e5 7. Nb3 Be7 8. O-O O-O 9. Be3 Nc6 *\n";
        await AddRepertoireAsync(user, "Schwarz-Repertoire", najdorfBe3);
        // Farbvertauschte Najdorf-Stellung mit einem zusätzlichen Zugpaar, damit sie nicht identisch
        // ist; der erwogene Zug ist dort c8→e6 = das Spiegelbild von Lc1-e3.
        const string mirroredQuery = "r1bq1rk1/ppp1bpp1/1nn4p/4p3/4P3/P2P1N2/1P2BPPP/RNBQ1RK1 b - - 0 10";

        var res = await FindAsync(user, mirroredQuery, d => d.Move = FromTo("c8", "e6"));

        var hit = res.Matches.First(m => m.Mirrored);
        Assert.Equal("exact", hit.MoveMatch);
        Assert.Equal("Be3", hit.MoveSan);         // im Repertoire ungespiegelt notiert
        Assert.Equal("c1", hit.MoveFrom);
        Assert.Equal("e3", hit.MoveTo);
    }
}
