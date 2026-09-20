using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Partie rekonstruieren". Zwei Dinge sind hier die eigentliche Logik und deshalb getestet:
/// die VERKETTUNG der Bruchstücke (was hängt woran, was ist prüfbar) und die Normalisierung der
/// Eingaben (Zugnummern raus, unbrauchbare Stellung abgewiesen).
/// </summary>
public class ReconstructionChainTests
{
    private static GameReconstructionPart Moves(int ordinal, string moves, bool continues = false) =>
        new() { Id = ordinal + 1, Ordinal = ordinal, Kind = ReconstructionPartKind.Moves, Moves = moves, ContinuesPrevious = continues };

    private static GameReconstructionPart Position(int ordinal, string fen, bool continues = false) =>
        new() { Id = ordinal + 1, Ordinal = ordinal, Kind = ReconstructionPartKind.Position, Fen = fen, ContinuesPrevious = continues };

    [Fact]
    public void SplitMoves_StripsNumbersResultAndComments()
    {
        var moves = ReconstructionChain.SplitMoves("1. e4 e5 2.Nf3 {ein Kommentar} Nc6 3. Bb5 (3. Bc4 Bc5) a6 1-0");
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" }, moves);
    }

    [Fact]
    public void Analyze_FirstMovesStartFromTheInitialPosition()
    {
        var result = ReconstructionChain.Analyze(new[] { Moves(0, "e4 e5 Nf3") });

        var part = Assert.Single(result.Parts);
        Assert.True(part.Anchored);
        Assert.True(part.Valid);
        Assert.Equal(0, part.StartPly);
        Assert.Equal(3, part.PlyCount);
        Assert.Equal(3, result.KnownPlies);
        Assert.Equal("e4 e5 Nf3", result.PrefixSan);
        Assert.Equal(0, result.Gaps);
    }

    [Fact]
    public void Analyze_IllegalMoveIsReportedAndStopsTheChain()
    {
        var result = ReconstructionChain.Analyze(new[] { Moves(0, "e4 e5 Qh9") });

        var part = Assert.Single(result.Parts);
        Assert.False(part.Valid);
        Assert.Equal("Qh9", part.FirstBadMove);
        // Die zwei gültigen Züge davor bleiben gezählt — der Fehler liegt am dritten.
        Assert.Equal(2, result.KnownPlies);
    }

    [Fact]
    public void Analyze_MovesAfterAPositionContinueFromIt()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3";
        var result = ReconstructionChain.Analyze(new[] { Position(0, fen), Moves(1, "Bc5 c3", continues: true) });

        Assert.True(result.Parts[1].Anchored);
        Assert.True(result.Parts[1].Valid);
        Assert.Equal(fen, result.Parts[1].StartFen);
        // Ohne Weg von der Grundstellung dorthin gibt es keine Halbzug-Nummer.
        Assert.Null(result.Parts[1].StartPly);
        Assert.Equal(0, result.KnownPlies);
    }

    [Fact]
    public void Analyze_FragmentWithoutAnchorIsNotJudgedAsWrong()
    {
        // Ein Bruchstueck mitten aus der Partie: ohne den Weg dorthin ist es weder zu bestaetigen
        // noch zu widerlegen — genau dieser Weg ist ja das, was rekonstruiert werden soll.
        var result = ReconstructionChain.Analyze(new[] { Moves(0, "e4 e5"), Moves(1, "Rxf7 Kxf7") });

        Assert.True(result.Parts[0].Valid);
        Assert.False(result.Parts[1].Anchored);
        Assert.False(result.Parts[1].Valid);
        Assert.Null(result.Parts[1].FirstBadMove);   // kein Vorwurf, nur keine Aussage
        Assert.Equal(1, result.Gaps);
        Assert.Equal(2, result.KnownPlies);          // der gesicherte Anfang bleibt gezaehlt
    }

    [Fact]
    public void Analyze_PositionAfterMovesOpensAGap()
    {
        const string fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 60";
        var result = ReconstructionChain.Analyze(new[] { Moves(0, "e4 e5"), Position(1, fen) });

        Assert.True(result.Parts[1].Valid);
        Assert.Equal(1, result.Gaps);
        Assert.Equal(2, result.KnownPlies);
    }

    [Fact]
    public void Analyze_ContinuationOfMovesExtendsTheKnownGame()
    {
        // Ausdrücklich angeschlossen: die Kette wächst weiter, ohne Lücke.
        var result = ReconstructionChain.Analyze(new[] { Moves(0, "e4 e5"), Moves(1, "Nf3 Nc6", continues: true) });

        Assert.True(result.Parts[1].Anchored);
        Assert.True(result.Parts[1].Valid);
        Assert.Equal(2, result.Parts[1].StartPly);
        Assert.Equal(4, result.KnownPlies);
        Assert.Equal("e4 e5 Nf3 Nc6", result.PrefixSan);
        Assert.Equal(0, result.Gaps);
    }

    [Fact]
    public void Analyze_ClaimedContinuationThatDoesNotFitIsReportedAsMismatch()
    {
        // „Danach stand DAS auf dem Brett" — steht es aber nicht: sagen statt still übernehmen.
        const string elsewhere = "8/8/8/4k3/8/8/4K3/8 w - - 0 60";
        var result = ReconstructionChain.Analyze(new[] { Moves(0, "e4 e5"), Position(1, elsewhere, continues: true) });

        Assert.True(result.Parts[1].Mismatch);
        Assert.False(result.Parts[1].Valid);
        Assert.Equal(0, result.Gaps);          // behauptet wurde Anschluss, nicht Lücke
        Assert.Equal(2, result.KnownPlies);
    }

    [Fact]
    public void Analyze_UnloadableFenIsInvalid()
    {
        var result = ReconstructionChain.Analyze(new[] { Position(0, "keine stellung") });
        Assert.False(Assert.Single(result.Parts).Valid);
    }
}

public class GameReconstructionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GameReconstructionService _service;

    public GameReconstructionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new GameReconstructionService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateAsync(string title = "Vereinsmeisterschaft, Runde 3")
    {
        var dto = await _service.CreateAsync(1, new ReconstructionHeadRequest { Title = title });
        return dto.Id;
    }

    private static ReconstructionPartRequest MovesPart(string moves, bool continues = false) =>
        new() { Kind = ReconstructionPartKind.Moves, Moves = moves, ContinuesPrevious = continues };

    [Fact]
    public async Task AddPart_NormalizesMovesAndCountsTheChain()
    {
        var id = await CreateAsync();
        var dto = await _service.AddPartAsync(1, id, MovesPart("1. e4 e5 2. Nf3 Nc6"));

        var part = Assert.Single(dto!.Parts);
        Assert.Equal("e4 e5 Nf3 Nc6", part.Moves);      // Zugnummern sind weg
        Assert.True(part.Valid);
        Assert.Equal(4, dto.KnownPlies);
        Assert.Equal("e4 e5 Nf3 Nc6", dto.PrefixSan);
    }

    [Fact]
    public async Task AddPart_RejectsEmptyMovesAndBrokenFen()
    {
        var id = await CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddPartAsync(1, id, MovesPart("1. 2. *")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AddPartAsync(1, id, new ReconstructionPartRequest { Kind = ReconstructionPartKind.Position, Fen = "kaputt" }));
        var dto = await _service.GetAsync(1, id);
        Assert.Empty(dto!.Parts);
    }

    [Fact]
    public async Task Reorder_PutsThePartsInTheGivenOrderAndKeepsMissingOnesAtTheEnd()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5"));
        await _service.AddPartAsync(1, id, MovesPart("Nf3 Nc6"));
        var dto = await _service.AddPartAsync(1, id, MovesPart("Bb5 a6"));
        var ids = dto!.Parts.Select(p => p.Id).ToList();

        // Nur die letzten beiden genannt — das erste Teil darf NICHT verschwinden.
        var reordered = await _service.ReorderAsync(1, id, new[] { ids[2], ids[1] });

        Assert.Equal(new[] { ids[2], ids[1], ids[0] }, reordered!.Parts.Select(p => p.Id));
        Assert.Equal(new[] { 0, 1, 2 }, reordered.Parts.Select(p => p.Ordinal));
    }

    [Fact]
    public async Task DeletePart_ClosesTheNumberingGap()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5"));
        var dto = await _service.AddPartAsync(1, id, MovesPart("Nf3 Nc6"));
        var first = dto!.Parts[0].Id;

        var afterDelete = await _service.DeletePartAsync(1, id, first);

        Assert.Equal(new[] { 0 }, afterDelete!.Parts.Select(p => p.Ordinal));
        Assert.Single(afterDelete.Parts);
    }

    [Fact]
    public async Task ForeignReconstructionIsInvisible()
    {
        var id = await CreateAsync();
        Assert.Null(await _service.GetAsync(2, id));
        Assert.Null(await _service.AddPartAsync(2, id, MovesPart("e4")));
        Assert.False(await _service.DeleteAsync(2, id));
        Assert.Empty(await _service.ListAsync(2));
    }

    [Fact]
    public async Task Create_StopsAtTheCap()
    {
        for (var i = 0; i < GameReconstructionService.MaxPerUser; i++) await CreateAsync($"Partie {i}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync("eine zu viel"));
        Assert.Equal("too-many", ex.Message);
    }

    // ----- Lücke schließen -----

    /// <summary>Stellung nach 1.e4 e5 2.Nf3 Nc6 3.Bb5 — zwei Halbzüge hinter „e4 e5 Nf3".</summary>
    private const string AfterFivePlies = "r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3";

    private static ReconstructionPartRequest PositionPart(string fen, bool continues = false) =>
        new() { Kind = ReconstructionPartKind.Position, Fen = fen, ContinuesPrevious = continues };

    [Fact]
    public async Task SolveGap_FindsTheMovesBetweenTheLastKnownPositionAndTheRememberedOne()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5 Nf3"));
        var dto = await _service.AddPartAsync(1, id, PositionPart(AfterFivePlies));
        var target = dto!.Parts[1].Id;

        var gap = await _service.SolveGapAsync(1, id, target, null);

        Assert.NotNull(gap);
        Assert.Null(gap!.Reason);
        Assert.Equal("Nc6 Bb5", Assert.Single(gap.Solutions).San);
        Assert.Equal(2, gap.Solutions[0].Plies);
        Assert.Equal(AfterFivePlies, gap.ToFen);
    }

    [Fact]
    public async Task SolveGap_SaysWhyThereIsNothingToSearch()
    {
        var id = await CreateAsync();
        var first = (await _service.AddPartAsync(1, id, MovesPart("e4 e5 Nf3")))!.Parts[0].Id;
        var joined = (await _service.AddPartAsync(1, id, PositionPart(AfterFivePlies, continues: true)))!.Parts[1].Id;
        var movesAfterGap = (await _service.AddPartAsync(1, id, MovesPart("Rxf7 Kxf7")))!.Parts[2].Id;

        Assert.Equal("no-previous", (await _service.SolveGapAsync(1, id, first, null))!.Reason);
        Assert.Equal("no-gap", (await _service.SolveGapAsync(1, id, joined, null))!.Reason);
        // Eine Zugfolge nach einer Lücke hat selbst keine bekannte Ausgangsstellung — sie kann kein Ziel sein.
        Assert.Equal("target-not-a-position", (await _service.SolveGapAsync(1, id, movesAfterGap, null))!.Reason);
    }

    [Fact]
    public async Task SolveGap_SaysNoAnchor_WhenTheStateBeforeTheGapIsUnknown()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5"));
        await _service.AddPartAsync(1, id, MovesPart("Rxf7 Kxf7"));    // Bruchstück ohne Anker
        var dto = await _service.AddPartAsync(1, id, PositionPart(AfterFivePlies));

        var gap = await _service.SolveGapAsync(1, id, dto!.Parts[2].Id, null);

        Assert.Equal("no-anchor", gap!.Reason);
        Assert.Null(gap.FromFen);
    }

    [Fact]
    public async Task ApplyGap_PutsTheMovesInFrontOfThePart_AndClosesTheChain()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5 Nf3"));
        var target = (await _service.AddPartAsync(1, id, PositionPart(AfterFivePlies)))!.Parts[1].Id;

        var dto = await _service.ApplyGapAsync(1, id, target, "Nc6 Bb5");

        Assert.Equal(3, dto!.Parts.Count);
        Assert.Equal("Nc6 Bb5", dto.Parts[1].Moves);
        Assert.True(dto.Parts[1].ContinuesPrevious);
        Assert.True(dto.Parts[2].ContinuesPrevious);   // die Stellung hängt jetzt an den Zügen
        Assert.Equal(target, dto.Parts[2].Id);
        Assert.Equal(0, dto.Gaps);
        Assert.Equal(5, dto.KnownPlies);
        Assert.Equal("e4 e5 Nf3 Nc6 Bb5", dto.PrefixSan);
    }

    [Fact]
    public async Task ApplyGap_RefusesMovesThatEndSomewhereElse()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5 Nf3"));
        var target = (await _service.AddPartAsync(1, id, PositionPart(AfterFivePlies)))!.Parts[1].Id;

        // Spielbar, aber die Stellung danach ist eine andere — es entsteht KEIN Teil.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.ApplyGapAsync(1, id, target, "Nc6 Bc4"));
        Assert.Equal("does-not-fit", ex.Message);
        Assert.Equal(2, (await _service.GetAsync(1, id))!.Parts.Count);
    }

    [Fact]
    public async Task Parts_AreCertainUnlessSomebodySaysOtherwise()
    {
        var id = await CreateAsync();

        // Ein Client, der die Frage nicht kennt, darf nicht „unsicher" für den Nutzer behaupten.
        var dto = await _service.AddPartAsync(1, id, MovesPart("e4 e5"));
        Assert.True(dto!.Parts[0].Certain);

        var partId = dto.Parts[0].Id;
        var unsure = await _service.UpdatePartAsync(1, id, partId,
            new ReconstructionPartRequest { Kind = ReconstructionPartKind.Moves, Moves = "e4 e5", Certain = false });
        Assert.False(unsure!.Parts[0].Certain);
    }

    [Fact]
    public async Task ApplyGap_MarksTheInsertedMovesAsUnsure()
    {
        // Die Züge sind GEFUNDEN, nicht erinnert — oft führen mehrere Wege in dieselbe Stellung.
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5 Nf3"));
        var target = (await _service.AddPartAsync(1, id, PositionPart(AfterFivePlies)))!.Parts[1].Id;

        var dto = await _service.ApplyGapAsync(1, id, target, "Nc6 Bb5");

        Assert.False(dto!.Parts[1].Certain);
        Assert.True(dto.Parts[0].Certain);
    }

    [Fact]
    public async Task Gap_IsInvisibleForOtherAccounts()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5 Nf3"));
        var target = (await _service.AddPartAsync(1, id, PositionPart(AfterFivePlies)))!.Parts[1].Id;

        Assert.Null(await _service.SolveGapAsync(2, id, target, null));
        Assert.Null(await _service.ApplyGapAsync(2, id, target, "Nc6 Bb5"));
    }

    [Fact]
    public async Task List_ShowsPartCountKnownPliesAndGaps()
    {
        var id = await CreateAsync();
        await _service.AddPartAsync(1, id, MovesPart("e4 e5 Nf3"));
        await _service.AddPartAsync(1, id, MovesPart("Rxf7 Kxf7"));   // Bruchstueck ohne Anker

        var item = Assert.Single(await _service.ListAsync(1));
        Assert.Equal(2, item.PartCount);
        Assert.Equal(3, item.KnownPlies);
        Assert.Equal(1, item.Gaps);
    }
}
