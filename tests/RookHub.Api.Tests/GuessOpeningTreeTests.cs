using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Eroeffnungsbaum. Die Liste daneben laesst sich nach Namen durchsuchen, aber nicht nach
/// STELLUNG — und danach sucht, wer seine Eroeffnung ueben will.
/// </summary>
public class GuessOpeningTreeTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GuessOpeningTree _tree;

    public GuessOpeningTreeTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _tree = new GuessOpeningTree(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task AnalysisAsync(string line, bool oeffentlich = true, bool gerechnet = true)
    {
        var g = new GameAnalysis
        {
            UserId = 1, Pgn = "x", StartFen = "s", TargetDepth = 20, MultiPv = 5,
            IsPublic = oeffentlich, OpeningLine = line, PlyCount = 2,
        };
        g.Positions.Add(new GameAnalysisPosition
        {
            Ply = 0, Fen = "f", GameMoveUci = "e2e4", GameMoveSan = "e4",
            CandidatesJson = gerechnet ? "[]" : null,
        });
        _db.GameAnalyses.Add(g);
        await _db.SaveChangesAsync();
    }

    private async Task LibraryAsync(string line)
    {
        _db.LibraryGames.Add(new LibraryGame { Pgn = "x", OpeningLine = line, Status = LibraryGameStatus.New });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Branch_grundstellung_zaehltDieErstenZuege()
    {
        await AnalysisAsync("e4 e5 Nf3");
        await AnalysisAsync("e4 c5 Nf3");
        await AnalysisAsync("d4 Nf6");

        var ast = await _tree.BranchAsync(null, onlyPlayable: true);

        Assert.Equal(3, ast.Total);
        Assert.Equal("e4", ast.Moves[0].San);
        Assert.Equal(2, ast.Moves[0].Games);
        Assert.Equal("d4", ast.Moves[1].San);
    }

    [Fact]
    public async Task Branch_nachZwei_zaehltNurWasDurchDieseStellungGeht()
    {
        await AnalysisAsync("e4 e5 Nf3 Nc6");
        await AnalysisAsync("e4 e5 Nf3 Nf6");
        await AnalysisAsync("e4 c5 Nf3");

        var ast = await _tree.BranchAsync("e4 e5", onlyPlayable: true);

        Assert.Equal(2, ast.Total);
        Assert.Single(ast.Moves);
        Assert.Equal("Nf3", ast.Moves[0].San);
        Assert.Equal(2, ast.Moves[0].Games);
    }

    /// <summary>Der Umschalter ist der Punkt: ohne ihn fuehrt der Baum entweder in eine fast leere
    /// Auswahl oder auf Partien, die man nicht spielen kann.</summary>
    [Fact]
    public async Task Branch_alle_zaehltDenRohbestandMit()
    {
        await AnalysisAsync("e4 e5");
        await LibraryAsync("e4 c5 Nf3 d6");
        await LibraryAsync("e4 c5 Nc3");

        var nurSpielbar = await _tree.BranchAsync("e4", onlyPlayable: true);
        var alle = await _tree.BranchAsync("e4", onlyPlayable: false);

        Assert.Equal(1, nurSpielbar.Total);
        Assert.Equal("e5", nurSpielbar.Moves.Single().San);
        Assert.Equal(2, alle.Total);
        Assert.Equal("c5", alle.Moves.Single().San);
    }

    /// <summary>Eine private Partie steht nicht im Bestand und darf auch nicht mitgezaehlt
    /// werden — sonst verspricht der Baum eine Partie, die niemand oeffnen kann.</summary>
    [Fact]
    public async Task Branch_privatePartien_zaehlenNicht()
    {
        await AnalysisAsync("e4 e5", oeffentlich: false);

        var ast = await _tree.BranchAsync(null, onlyPlayable: true);

        Assert.Equal(0, ast.Total);
        Assert.Empty(ast.Moves);
    }

    [Fact]
    public async Task Branch_amEndeDerZeile_liefertKeineFortsetzung()
    {
        await AnalysisAsync(string.Join(' ', Enumerable.Repeat("e4", GuessOpeningTree.MaxDepth)));

        var ast = await _tree.BranchAsync(
            string.Join(' ', Enumerable.Repeat("e4", GuessOpeningTree.MaxDepth)), onlyPlayable: true);

        Assert.Empty(ast.Moves);
    }

    /// <summary>„Nf3+" und „Nf3" sind derselbe Zug — ein Baum, der sie trennt, hat zwei Aeste fuer
    /// eine Stellung.</summary>
    [Fact]
    public void Normalize_wirftBewertungsUndSchachzeichenWeg()
    {
        Assert.Equal("e4 Nf3 Qxd5", GuessOpeningTree.Normalize(" e4  Nf3+  Qxd5!? "));
        Assert.Equal(string.Empty, GuessOpeningTree.Normalize(null));
    }

    /// <summary>Tiefer als die gespeicherte Zeile kann der Filter nicht fragen.</summary>
    [Fact]
    public void Normalize_kuerztAufDieGespeicherteTiefe()
    {
        var lang = string.Join(' ', Enumerable.Repeat("e4", GuessOpeningTree.MaxDepth + 10));

        var teile = GuessOpeningTree.Normalize(lang).Split(' ');

        Assert.Equal(GuessOpeningTree.MaxDepth, teile.Length);
    }
}
