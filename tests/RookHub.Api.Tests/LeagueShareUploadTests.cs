using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Uploads in die Vereins-Datenbank OHNE Konto über einen Teilen-Link (Codereview 2026-09-29, A2-009): jede Partie trägt
/// den Link als Hash (auch „Schwaz"-Partien, aber ohne Zeitpunkt), ein Verwalter entfernt alles eines Links, und es gibt
/// einen Deckel je Aufruf und je Link und Tag.
/// </summary>
public class LeagueShareUploadTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly ManualTime _time = new(new DateTimeOffset(Now));
    private readonly LeagueShareUploadQuota _quota;

    public LeagueShareUploadTests() => _quota = new LeagueShareUploadQuota(_time);
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now, shareQuota: _quota);

    private const string LongGame = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0";
    private const string OtherGame = "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. Qc2 O-O 5. a3 Bxc3+ 6. Qxc3 b6 7. Bg5 Bb7 8. f3 h6 9. Bh4 d5 10. e3 Nbd7 1-0";

    private static string Pgn(string white, string black, int year = 2024, string moves = LongGame) =>
        $"[Event \"Vereinsmeisterschaft\"]\n[Date \"{year}.05.12\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n\n{moves}\n";

    /// <summary><paramref name="count"/> verschiedene Partien zweier Gegner aus der Liga (gleiche Züge, andere Jahre —
    /// bei langen Partien ist das keine Dublette).</summary>
    private static string Games(int count, int firstYear = 1950, string moves = LongGame) =>
        string.Concat(Enumerable.Range(0, count).Select(i => Pgn("Hengl, Philip", "Schnabl, Andreas", firstYear + i, moves)));

    private async Task<int> SeedAsync()
    {
        void Player(string team, string name, string fide) =>
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        Player("Schwaz", "Oberschmid, Patrik", "900");
        Player("Absam", "Hengl, Philip", "222");
        Player("Absam", "Schnabl, Andreas Dr.", "333");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var token in new[] { "tokA", "tokB" })
            _db.LeagueShares.Add(new LeagueShare { Token = token, Tnr = 7, Round = 1, Team = "Schwaz", Expires = today.AddDays(7), CreatedAt = DateTime.UtcNow });
        var u = new AppUser { Username = "patrik", Email = "p@test", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private LeagueShareClubController ShareController(LeagueClubService club) => new(
        new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance), club,
        new ScoresheetScanService(_db, null!, TestServices.SavedGames(_db), new NotificationService(_db), NullLogger<ScoresheetScanService>.Instance),
        new ScoresheetScanSignal());

    [Fact]
    public async Task ShareImport_ViaTheController_StampsTheLink_EvenOnSchwazGames_ButTimeOnlyOnNamedOnes()
    {
        await SeedAsync();
        var pgn = Pgn("Hengl, Philip", "Schnabl, Andreas") + Pgn("Oberschmid, Patrik", "Hengl, Philip", 2023);

        var response = await ShareController(Club()).Import("tokA", new LeagueClubImportRequest { Pgn = pgn }, default);

        var result = Assert.IsType<LeagueClubImportResultDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal((2, 1), (result.Added, result.Anonymized));
        var games = _db.LeagueClubGames.AsNoTracking().ToList();
        Assert.All(games, g => Assert.Equal(LeagueClubService.ShareHashOf("tokA"), g.UploadShareHash));
        Assert.All(games, g => Assert.Null(g.UploadedByUserId));                         // ein Teilen-Link speichert nie, wer es war
        Assert.Equal(Now, games.Single(g => !g.Anonymized).CreatedAt);                    // mit Namen: wann
        Assert.Null(games.Single(g => g.Anonymized).CreatedAt);                          // mit „Schwaz": weder wer noch wann
        Assert.DoesNotContain(games, g => g.UploadShareHash!.Contains("tokA"));           // der Link selbst steht nirgends
    }

    [Fact]
    public async Task ShareAdd_ViaTheController_StampsTheLink()
    {
        await SeedAsync();
        var response = await ShareController(Club()).Add("tokB", new LeagueClubGameRequest
        {
            Moves = new() { "e4", "e5" }, White = "Hengl, Philip", Black = "Schnabl, Andreas", Year = 2024,
        }, null, default);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(LeagueClubService.ShareHashOf("tokB"), _db.LeagueClubGames.AsNoTracking().Single().UploadShareHash);
    }

    [Fact]
    public async Task DeleteByShare_RemovesExactlyTheGamesOfThatLink_DryRunOnlyCounts()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportViaShareAsync("tokA", Pgn("Hengl, Philip", "Schnabl, Andreas") + Pgn("Oberschmid, Patrik", "Hengl, Philip", 2023), null);
        await club.ImportViaShareAsync("tokB", Pgn("Schnabl, Andreas", "Hengl, Philip", 2022), null);
        await club.ImportPgnAsync(me, Pgn("Hengl, Philip", "Schnabl, Andreas", 2021), null);
        var controller = new LeagueClubController(club, null!, new ScoresheetScanSignal());

        var dry = Assert.IsType<OkObjectResult>(await controller.DeleteShareGames("tokA", dryRun: true));
        Assert.Equal(2, (int)dry.Value!.GetType().GetProperty("count")!.GetValue(dry.Value)!);
        Assert.Equal(4, _db.LeagueClubGames.Count());

        var done = Assert.IsType<OkObjectResult>(await controller.DeleteShareGames("tokA"));
        Assert.Equal(2, (int)done.Value!.GetType().GetProperty("count")!.GetValue(done.Value)!);
        var left = _db.LeagueClubGames.AsNoTracking().OrderBy(g => g.Year).ToList();
        Assert.Equal(new int?[] { 2021, 2022 }, left.Select(g => g.Year));                // Link B und der angemeldete Upload bleiben
        Assert.Equal(new string?[] { null, LeagueClubService.ShareHashOf("tokB") }, left.Select(g => g.UploadShareHash));
        Assert.Equal(0, await club.DeleteByShareAsync("tokA", dryRun: false));           // zweimal = nichts mehr da
        Assert.IsType<BadRequestObjectResult>(await controller.DeleteShareGames(new string('x', 65)));
    }

    [Fact]
    public async Task ShareImport_AtMost50PerCall_RestIsShareLimit()
    {
        await SeedAsync();
        var result = await Club().ImportViaShareAsync("tokA", Games(60), null);

        Assert.Equal(LeagueShareUploadQuota.PerCall, result.Added);
        Assert.Equal(10, result.Failed.Count);
        Assert.All(result.Failed, f => Assert.Equal(LeagueClubService.ShareLimitReason, f.Reason));
        Assert.Equal(50, _db.LeagueClubGames.Count());
    }

    [Fact]
    public async Task ShareUploads_AtMost200PerLinkAndDay_OtherLinksAndAccountsUnaffected_NextDayFreeAgain()
    {
        var me = await SeedAsync();
        var club = Club();
        using (var earlier = _quota.Reserve(LeagueClubService.ShareHashOf("tokA"), 195)) earlier.Kept = 195;

        var a = await club.ImportViaShareAsync("tokA", Games(10), null);
        Assert.Equal((5, 5), (a.Added, a.Failed.Count(f => f.Reason == LeagueClubService.ShareLimitReason)));
        var (single, reason, _) = await club.AddGameViaShareAsync("tokA", new LeagueClubGameRequest
        {
            Moves = new() { "e4", "e5" }, White = "Hengl, Philip", Black = "Schnabl, Andreas", Year = 2024,
        });
        Assert.Equal((null, LeagueClubService.ShareLimitReason), (single, reason));   // das Formular-Add ohne Foto zählt mit

        Assert.Equal(3, (await club.ImportViaShareAsync("tokB", Games(3, 1990), null)).Added);   // anderer Link: eigener Deckel
        Assert.Equal(60, (await club.ImportPgnAsync(me, Games(60, 1900, OtherGame), null)).Added);   // angemeldet: kein Deckel

        _time.Now = _time.Now.AddDays(1);
        Assert.Equal(5, (await club.ImportViaShareAsync("tokA", Games(5, 2010), null)).Added);
    }

    [Fact]
    public void Lease_WhatIsNotKeptGoesBack()
    {
        var hash = LeagueClubService.ShareHashOf("tokA");
        using (var aborted = _quota.Reserve(hash, 50)) Assert.Equal(50, aborted.Granted);   // Aufruf abgebrochen: nichts gespeichert
        using (var half = _quota.Reserve(hash, 50)) half.Kept = 20;
        using var rest = _quota.Reserve(hash, 500);
        Assert.Equal(LeagueShareUploadQuota.PerLinkPerDay - 20, rest.Granted);
    }

    [Fact]
    public void ProgramCs_RegistersTheShareQuotaAsSingleton()
    {
        // Ohne Registrierung bekäme jeder LeagueClubService seinen eigenen Zähler — der Deckel je Link und Tag wäre wirkungslos.
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("AddSingleton<RookHub.Api.Services.League.LeagueShareUploadQuota>()", src);
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
