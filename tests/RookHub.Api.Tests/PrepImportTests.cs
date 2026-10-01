using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests;

/// <summary>
/// Spielervorbereitung, Phase 1 (2026-10-01): den Partiebestand aus Megabase und Lumbra paketweise einlesen — Zerlegen,
/// Verwerfen mit Grund, Dubletten im Paket und über beide Quellen (EINE Zeile, zwei Quellen-Bits), Fortsetzen und das
/// doppelt geschickte Paket. Unter InMemory; den SQL-Weg (Sammel-INSERT) prüft <c>PrepImportSqlTests</c> gegen MariaDB.
/// FIDE-IDs der Testdaten liegen im 99xxxx-Bereich.
/// </summary>
public class PrepImportTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private const string Najdorf = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7";
    private const string ShortDraw = "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6";

    private static string Game(string white, string black, string moves = Najdorf, string result = "1-0", string date = "2024.05.17",
        string? whiteFide = null, string? blackFide = null, string extra = "", string evt = "Open Schwaz", string site = "Schwaz") =>
        $"[Event \"{evt}\"]\n[Site \"{site}\"]\n[Date \"{date}\"]\n[Round \"3\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n"
        + $"[Result \"{result}\"]\n[WhiteElo \"2210\"]\n[BlackElo \"2105\"]\n[ECO \"B90\"]\n"
        + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n") + (blackFide is null ? "" : $"[BlackFideId \"{blackFide}\"]\n")
        + extra + "\n" + moves + " " + result + "\n\n";

    private PrepImportService Service() => new(_db);

    private Task<PrepImportService.ChunkResult> Import(byte source, int chunk, string pgn, long? first = null) =>
        Service().ImportChunkAsync(source, chunk, first ?? chunk * 5000L, pgn, default);

    // ── Zerlegen ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_HeadersAndMainline_OnlyMovesAndHeadData()
    {
        var pgn = Game("Huber, Franz", "Mair, Josef", whiteFide: "990001",
            moves: "1. e4 {Kommentar} c5 2. Nf3 (2. c3 d5) d6 $1 3. d4!? cxd4 4. Nxd4+ Nf6 5. O-O-O exd8=Q#");
        var r = PrepPgn.Parse(pgn);

        Assert.Equal(1, r.Read);
        var g = Assert.Single(r.Games);
        Assert.Equal("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 O-O-O exd8=Q", g.Moves);
        Assert.Equal(10, g.Plies);
        Assert.Equal("Huber, Franz", g.White!.Name);
        Assert.Equal("huber, franz", g.White.NameKey);
        Assert.Equal("990001", g.White.FideId);
        Assert.Null(g.Black!.FideId);
        Assert.Equal((short)2210, g.WhiteElo);
        Assert.Equal(PrepResult.WhiteWins, g.Result);
        Assert.Equal(20240517, g.PlayedOn);
        Assert.Equal((short)2024, g.Year);
        Assert.Equal("B90", g.Eco);
        Assert.Equal("3", g.Round);
        Assert.Equal("Open Schwaz", g.Event);
        Assert.Equal(PrepPgn.Hash(g.Moves), g.MovesHash);
    }

    [Theory]
    [InlineData("1975.??.??", 19750000)]
    [InlineData("2024.05.??", 20240500)]
    [InlineData("1475.??.??", 14750000)]
    [InlineData("2024-05-17", 20240517)]
    [InlineData("????.??.??", null)]
    [InlineData("", null)]
    public void PlayedOn_PartialDates_NumberWithZeros(string date, int? expected) => Assert.Equal(expected, PrepPgn.PlayedOn(date));

    [Fact]
    public void Parse_NamesTitlesAndUnknown_KeysWithoutTitlesAndAccents()
    {
        Assert.Equal("grimm, wolfgang", PrepPgn.NameKey("Grimm, Wolfgang, Dr."));
        Assert.Equal("ozel, ozgur", PrepPgn.NameKey("Özel, Özgür"));
        Assert.Equal("humer, wolfgang", PrepPgn.NameKey("FM Humer, Wolfgang"));
        Assert.Equal("robidoux", PrepPgn.Surname("Robidoux Michel"));
        Assert.Equal("robidoux", PrepPgn.Surname("Robidoux, Michel"));
        Assert.Equal("hoecher", PrepPgn.Surname("Höcher, Michael"));
        Assert.Equal("hoecher", PrepPgn.Surname("Hoecher, Michael"));
        Assert.True(PrepPgn.IsUnknownName("NN"));
        Assert.True(PrepPgn.IsUnknownName("?"));
        Assert.True(PrepPgn.IsUnknownName("N.N."));

        var g = Assert.Single(PrepPgn.Parse(Game("Huber, Franz", "NN", whiteFide: "0", extra: "[BlackFideId \"abc\"]\n")).Games);
        Assert.Null(g.Black);                // unbekannter Gegner → keine Seite
        Assert.Null(g.White!.FideId);        // FIDE „0" zählt nicht
    }

    [Fact]
    public void Parse_Discards_CountedByReason()
    {
        var pgn = Game("A, B", "C, D", extra: "[SetUp \"1\"]\n[FEN \"8/8/8/8/8/8/k7/K7 w - - 0 1\"]\n")       // eigene Stellung
            + Game("A, B", "C, D", extra: "[FEN \"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1\"]\n") // Grundstellung: bleibt
            + Game("A, B", "C, D", extra: "[Variant \"Chess960\"]\n")
            + Game("A, B", "C, D", moves: "")
            + Game("A, B", "C, D", moves: "1. e4 -- 2. d4")
            + Game("?", "NN")
            + Game("A, B", "C, D", moves: string.Join(' ', Enumerable.Repeat("Nf3 Nf6 Ng1 Ng8", 251)));
        var r = PrepPgn.Parse(pgn);

        Assert.Equal(7, r.Read);
        Assert.Single(r.Games);
        Assert.Equal(1, r.Discarded[PrepPgn.ReasonFen]);
        Assert.Equal(1, r.Discarded[PrepPgn.ReasonVariant]);
        Assert.Equal(1, r.Discarded[PrepPgn.ReasonNoMoves]);
        Assert.Equal(1, r.Discarded[PrepPgn.ReasonBadMoves]);
        Assert.Equal(1, r.Discarded[PrepPgn.ReasonNoPlayers]);
        Assert.Equal(1, r.Discarded[PrepPgn.ReasonTooLong]);
    }

    [Fact]
    public void Parse_CrLfAndBom_LikeMegabase()
    {
        var pgn = "﻿" + (Game("De Castellvi, Francisco", "Vinoles, Narcisco") + Game("Lucena, Luis Ramirez", "Quintana", moves: ShortDraw))
            .Replace("\n", "\r\n");
        var r = PrepPgn.Parse(pgn);
        Assert.Equal(2, r.Read);
        Assert.Equal(2, r.Games.Count);
        Assert.Equal("De Castellvi, Francisco", r.Games[0].White!.Name);
    }

    // ── Einlesen ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportChunk_NewGames_RowsPlayersEventsAndCounters()
    {
        var pgn = Game("Huber, Franz", "Mair, Josef", whiteFide: "990001", date: "2019.03.01")
            + Game("Mair, Josef", "Huber, Franz", moves: ShortDraw, result: "1/2-1/2", blackFide: "990001", date: "2023.??.??")
            + Game("A, B", "C, D", moves: "");
        var r = await Import(PrepSources.Mega, 0, pgn);

        Assert.Equal((3, 2, 0, 1, false), (r.Read, r.Added, r.Duplicates, r.Discarded, r.Already));
        Assert.Equal(1, r.Reasons[PrepPgn.ReasonNoMoves]);
        Assert.Equal(2, await _db.PrepGames.CountAsync());
        Assert.All(await _db.PrepGames.ToListAsync(), g => Assert.Equal(PrepSources.Mega, g.Sources));

        var huber = await _db.PrepPlayers.SingleAsync(p => p.FideId == "990001");
        Assert.Equal((2, (short?)2019, (short?)2023, (short?)2210), (huber.Games, huber.FirstYear, huber.LastYear, huber.MaxElo));
        var mair = await _db.PrepPlayers.SingleAsync(p => p.NameKey == "mair, josef");
        Assert.Null(mair.FideId);
        Assert.Equal(2, mair.Games);
        Assert.Single(await _db.PrepEvents.ToListAsync());                          // dasselbe Turnier einmal

        var stored = await _db.PrepImports.SingleAsync();
        Assert.Equal(("Mega", 0, 3, 2, 1), (stored.Source, stored.Chunk, stored.Read, stored.Added, stored.Discarded));
    }

    [Fact]
    public async Task ImportChunk_SameGameTwiceInChunk_OneRow()
    {
        var r = await Import(PrepSources.Mega, 0, Game("Huber, Franz", "Mair, Josef") + Game("Huber, F.", "Mair, J."));
        Assert.Equal((1, 1), (r.Added, r.Duplicates));
        Assert.Equal(1, await _db.PrepGames.CountAsync());
    }

    [Fact]
    public async Task ImportChunk_ShortDrawBetweenOtherPlayers_NotADuplicate()
    {
        await Import(PrepSources.Mega, 0, Game("Huber, Franz", "Mair, Josef", moves: ShortDraw, result: "1/2-1/2"));
        var r = await Import(PrepSources.Lumbra, 0, Game("Berger, Anna", "Mair, Josef", moves: ShortDraw, result: "1/2-1/2"));
        Assert.Equal((1, 0), (r.Added, r.Duplicates));
        Assert.Equal(2, await _db.PrepGames.CountAsync());
    }

    [Fact]
    public async Task ImportChunk_SameGameFromBothSources_OneRowTwoSourceBits()
    {
        await Import(PrepSources.Mega, 0, Game("Robidoux, Michel", "Obukhov, Alexander", date: "1997.??.??"));
        var r = await Import(PrepSources.Lumbra, 0, Game("Robidoux Michel", "Obukhov, Alexander", date: "1997.01.12"));

        Assert.Equal((0, 1), (r.Added, r.Duplicates));
        var g = await _db.PrepGames.SingleAsync();
        Assert.Equal(PrepSources.Mega | PrepSources.Lumbra, g.Sources);
        Assert.Equal(19970000, g.PlayedOn);                         // die erste Fassung bleibt
        Assert.Equal(1, (await _db.PrepPlayers.SingleAsync(p => p.NameKey == "robidoux, michel")).Games);
    }

    [Fact]
    public async Task ImportChunk_DuplicateKnowsFide_GameMovesToFidePlayer()
    {
        await Import(PrepSources.Mega, 0, Game("Obukhov, Alexander", "Kaunonen, Jouni"));
        var r = await Import(PrepSources.Lumbra, 0, Game("Obukhov, Alexander", "Kaunonen, Jouni", whiteFide: "990002"));

        Assert.Equal((0, 1), (r.Added, r.Duplicates));
        var g = await _db.PrepGames.SingleAsync();
        var fide = await _db.PrepPlayers.SingleAsync(p => p.FideId == "990002");
        var nameOnly = await _db.PrepPlayers.SingleAsync(p => p.FideId == null && p.NameKey == "obukhov, alexander");
        Assert.Equal(fide.Id, g.WhiteId);
        Assert.Equal((1, 0), (fide.Games, nameOnly.Games));
    }

    [Fact]
    public async Task ImportChunk_SentTwice_SecondChangesNothing()
    {
        var pgn = Game("Huber, Franz", "Mair, Josef") + Game("Mair, Josef", "Huber, Franz", moves: ShortDraw);
        var first = await Import(PrepSources.Lumbra, 7, pgn);
        var again = await Import(PrepSources.Lumbra, 7, pgn);

        Assert.False(first.Already);
        Assert.True(again.Already);
        Assert.Equal((first.Read, first.Added), (again.Read, again.Added));
        Assert.Equal(2, await _db.PrepGames.CountAsync());
        Assert.Equal(2, (await _db.PrepPlayers.SingleAsync(p => p.NameKey == "huber, franz")).Games);
        Assert.Single(await _db.PrepImports.ToListAsync());
    }

    [Fact]
    public async Task ImportChunk_SameChunkOtherFirstGame_Mismatch()
    {
        await Import(PrepSources.Mega, 3, Game("Huber, Franz", "Mair, Josef"), first: 15000);
        await Assert.ThrowsAsync<PrepImportService.ChunkMismatchException>(() =>
            Import(PrepSources.Mega, 3, Game("Huber, Franz", "Mair, Josef"), first: 6000));
    }

    [Fact]
    public async Task ImportChunk_ResumeAfterAbort_ListsDoneChunksPerSource()
    {
        await Import(PrepSources.Mega, 0, Game("Huber, Franz", "Mair, Josef"));
        await Import(PrepSources.Mega, 2, Game("Mair, Josef", "Huber, Franz", moves: ShortDraw));
        await Import(PrepSources.Lumbra, 0, Game("Berger, Anna", "Mair, Josef"));

        var done = await Service().ImportsAsync(PrepSources.Mega, default);
        Assert.Equal(new[] { 0, 2 }, done.Select(d => d.Chunk));
        Assert.Equal(new[] { 0L, 10000L }, done.Select(d => d.FirstGame));
    }

    [Fact]
    public async Task ImportChunk_TooManyGames_Rejected()
    {
        var sb = new StringBuilder();
        for (var i = 0; i <= PrepImportService.MaxGamesPerChunk; i++) sb.Append("[White \"A\"]\n[Black \"B\"]\n\n1. e4 *\n\n");
        await Assert.ThrowsAsync<PrepImportService.ChunkTooLargeException>(() => Import(PrepSources.Mega, 0, sb.ToString()));
        Assert.Empty(await _db.PrepGames.ToListAsync());
    }

    // ── Controller ─────────────────────────────────────────────────────────────────────────────

    private PrepController Controller(byte[] body, bool gzip)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(body);
        if (gzip) ctx.Request.Headers.ContentEncoding = "gzip";
        return new PrepController(Service()) { ControllerContext = new ControllerContext { HttpContext = ctx } };
    }

    private static byte[] Gzip(string s)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest)) gz.Write(Encoding.UTF8.GetBytes(s));
        return ms.ToArray();
    }

    [Fact]
    public async Task ImportGames_GzipBody_Imported()
    {
        var res = await Controller(Gzip(Game("Huber, Franz", "Mair, Josef")), gzip: true).ImportGames("Lumbra", 0, 0, default);
        Assert.IsType<OkObjectResult>(res);
        Assert.Equal(PrepSources.Lumbra, (await _db.PrepGames.SingleAsync()).Sources);
    }

    [Fact]
    public async Task ImportGames_BadSourceOrChunk_400()
    {
        var body = Encoding.UTF8.GetBytes(Game("Huber, Franz", "Mair, Josef"));
        Assert.IsType<BadRequestObjectResult>(await Controller(body, false).ImportGames("Chessbase", 0, 0, default));
        Assert.IsType<BadRequestObjectResult>(await Controller(body, false).ImportGames("Mega", -1, 0, default));
        Assert.IsType<BadRequestObjectResult>(await Controller(body, false).ImportGames("Mega", 0, null, default));
        Assert.Empty(await _db.PrepGames.ToListAsync());
    }

    [Fact]
    public async Task ImportGames_ChunkMismatch_409()
    {
        await Controller(Encoding.UTF8.GetBytes(Game("Huber, Franz", "Mair, Josef")), false).ImportGames("Mega", 1, 5000, default);
        var res = await Controller(Encoding.UTF8.GetBytes(Game("Huber, Franz", "Mair, Josef")), false).ImportGames("Mega", 1, 2000, default);
        Assert.IsType<ConflictObjectResult>(res);
    }

    [Fact]
    public void PrepController_EveryAction_BehindPrepManage()
    {
        var actions = typeof(PrepController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.Equal(PermissionPolicyProvider.Prefix + Permissions.PrepManage,
            a.GetCustomAttribute<HasPermissionAttribute>()?.Policy));
        Assert.Contains(Permissions.PrepView, Permissions.All);
        Assert.Contains(Permissions.PrepManage, Permissions.All);
    }
}
