using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Og;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Og;

namespace RookHub.Api.Tests.Og;

/// <summary>LeagueHub-Vorschauen (2026-11-09): eigene Site <c>leaguehub</c>, geteilte Begegnung <c>/s/{token}</c> und Ligapartie
/// <c>/partie/{token}</c>; das Bild zeigt nur, was die Seite des Links selbst zeigt; abgelaufen/ungültig → kein Bild.</summary>
public class OgLeagueCardTests
{
    private const string LH = OgIndexHtmlProvider.LeagueHubSite;

    [Theory]
    [InlineData("/s/abcDEF_12", "league", "abcDEF_12")]
    [InlineData("/s/abcDEF_12?runde=3", "league", "abcDEF_12~3")]
    [InlineData("/s/abcDEF_12?runde=x", "league", "abcDEF_12")]
    [InlineData("/partie/CfDJ8tok-en_x", "lgame", "CfDJ8tok-en_x")]
    public void ParsePath_LeagueHub(string path, string kind, string id)
    {
        var r = OgMetaService.ParsePath(path, LH);
        Assert.Equal(kind, r!.Value.Kind);
        Assert.Equal(id, r.Value.Id);
    }

    [Theory]
    [InlineData("/s/abc/hochladen", LH)]        // Upload-Seite: keine Vorschau
    [InlineData("/s/abc/formular/k", LH)]
    [InlineData("/s/abc~3", LH)]                // Runde nur über ?runde=
    [InlineData("/g/abc", LH)]                  // RookHub-Routen gibt es auf LeagueHub nicht
    [InlineData("/puzzles/5", LH)]
    [InlineData("/s/abc", "")]                  // LeagueHub-Routen gibt es auf RookHub nicht
    [InlineData("/partie/abc", "")]
    [InlineData("/partie/abc", "turnier")]
    public void ParsePath_RoutesStayOnTheirSite(string path, string site) => Assert.Null(OgMetaService.ParsePath(path, site));

    private static JsonObject Share(string status = "open") => JsonNode.Parse("""
        {
          "league": "Landesliga Tirol", "season": "2026/27", "round": 4, "team": "SK Schwaz 1/",
          "club": { "id": 990601, "name": "SK Schwaz", "anonName": "Schwaz" },
          "fixture": {
            "opp": "Spg Fügen-Mayrhofen/Zillertal/", "home": false, "date": "08.11.2026", "time": "14:00", "status": "STATUS", "score": "2½:3½",
            "boards": [
              { "board": 1, "opp_color": "w", "cand": [ { "n": "Huber, Josef", "elo": 2105, "p": 0.62 }, { "n": "Maier, Franz", "elo": 1990, "p": 0.2 } ], "other": 0.1,
                "actual": { "n": "Maier, Franz", "elo": 1990, "vs": "x", "own": "y" } },
              { "board": 2, "opp_color": "s", "cand": [ { "n": "Gruber, Eva", "elo": null, "p": 0.457 } ], "other": 0 }
            ]
          }
        }
        """.Replace("STATUS", status))!.AsObject();

    [Fact]
    public void FixtureCard_Open_TopCandidatePerBoard_WithPercent_AwayTeamFirst()
    {
        var card = OgMetaService.LeagueFixtureCard(Share());
        Assert.Equal(OgBrand.LeagueHub, card.Brand);
        Assert.Equal("Landesliga Tirol · Runde 4", card.Kicker);
        Assert.Equal("Spg Fügen-Mayrhofen/Zillertal – SK Schwaz 1", card.Title);   // auswärts: Gegner zuerst, „/" am Ende weg
        Assert.Equal("08.11.2026 · 14:00", Assert.Single(card.Lines));
        Assert.Equal("Wahrscheinlichste Aufstellung Spg Fügen-Mayrhofen/Zillertal", card.Table!.Caption);
        Assert.Equal(new[] { "Huber, Josef", "Gruber, Eva" }, card.Table.Rows.Select(r => r.Name));
        Assert.Equal(new[] { "62 %", "46 %" }, card.Table.Rows.Select(r => r.Value));
        Assert.Equal(new bool?[] { true, false }, card.Table.Rows.Select(r => r.White));
        var png = OgCardTests.Images().RenderCard(card);
        if (Environment.GetEnvironmentVariable("OG_DUMP_DIR") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, "league.png"), png);
        OgCardTests.AssertCardPng(png);
    }

    [Fact]
    public void FixtureCard_Played_ShowsWhoPlayed_AndScore()
    {
        var card = OgMetaService.LeagueFixtureCard(Share("played"));
        Assert.Equal("Spg Fügen-Mayrhofen/Zillertal 2½:3½ SK Schwaz 1", card.Title);
        Assert.Equal("Maier, Franz", card.Table!.Rows[0].Name);
        Assert.Null(card.Table.Rows[0].Value);
    }

    [Fact]
    public void FixtureCard_MissingData_NoException()
    {
        var card = OgMetaService.LeagueFixtureCard(new JsonObject { ["team"] = "X" });
        Assert.Equal("Noch keine Prognose", card.Table!.Caption);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(card));
    }

    [Fact]
    public void GameCard_Anonymized_NoBoardNoTeams()
    {
        var anon = new LeagueGameShares.SharedGame("Landesliga Tirol", "2026/27", 4, null, null, null, "Schwaz", null, "Huber, Josef", 2105,
            "0-1", "1. e4 e5 0-1", "2027-02-08");
        var card = OgMetaService.LeagueGameCard(anon);
        Assert.Equal("Landesliga Tirol · Runde 4", card.Kicker);       // kein „Brett"
        Assert.Empty(card.Lines);                                     // keine Mannschaften
        Assert.Equal("Schwaz", card.Players[0].Name);
        Assert.Null(card.Players[0].Elo);
        var png = OgCardTests.Images().RenderCard(card);
        if (Environment.GetEnvironmentVariable("OG_DUMP_DIR") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, "lgame.png"), png);
        OgCardTests.AssertCardPng(png);

        var open = anon with { Board = 3, Home = "SK Schwaz 1/", Away = "Absam" };
        var full = OgMetaService.LeagueGameCard(open);
        Assert.Equal("Landesliga Tirol · Runde 4 · Brett 3", full.Kicker);
        Assert.Equal("SK Schwaz 1 – Absam", Assert.Single(full.Lines));
    }

    [Fact]
    public async Task ExpiredOrInvalidLinks_NoPageNoImage()
    {
        using var db = OgCardTests.NewDb();
        db.LeagueShares.Add(new LeagueShare { Token = "ltok990601", ClubId = 990601, Tnr = 990601, Round = 1, Team = "SK Schwaz 1",
            Expires = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), CreatedAt = DateTime.UtcNow.AddMonths(-4) });
        await db.SaveChangesAsync();
        var protection = new EphemeralDataProtectionProvider();
        var meta = new OgMetaService(null!, null!, null!, null!, db, NullLogger<OgMetaService>.Instance,
            league: new LeagueService(db, null!, NullLogger<LeagueService>.Instance),
            leagueGames: new LeagueGameShares(db, null!, protection));

        Assert.Null(await meta.ResolvePageAsync("/s/ltok990601", "https://lh.example", default, LH));   // abgelaufen
        Assert.Null(await meta.ResolveBoardAsync("league", "ltok990601"));
        Assert.Null(await meta.ResolveBoardAsync("league", "gibtsnicht"));

        // Ligapartie: kaputte Marke und eine echt versiegelte, aber abgelaufene
        Assert.Null(await meta.ResolveBoardAsync("lgame", "kaputt"));
        var expired = protection.CreateProtector("RookHub.League.GameShare.v1").Protect("990601|990601|1|1|1|0|20200101");
        Assert.Null(await meta.ResolvePageAsync($"/partie/{expired}", "https://lh.example", default, LH));
        Assert.Null(await meta.ResolveBoardAsync("lgame", expired));
    }

    // ── Controller: Basis-URL, Escaping, site_name ──────────────────────────────────

    [Fact]
    public void ConfiguredBaseUrl_LeagueHub_UsesLeagueHubBaseUrl_ElseAppBaseUrl()
    {
        Assert.Equal("https://lh.example", OgController.ConfiguredBaseUrl(LH, "https://rh.example", "https://t.example", "https://lh.example"));
        Assert.Equal("https://rh.example", OgController.ConfiguredBaseUrl(LH, "https://rh.example", "https://t.example", null));
        Assert.Equal("https://rh.example", OgController.ConfiguredBaseUrl("", "https://rh.example", "https://t.example", "https://lh.example"));
    }

    [Fact]
    public void Inject_EscapesTitleAndDescription_AndNamesTheSite()
    {
        var page = new OgPage("Huber \"Sepp\" <script>alert(1)</script> – B", "a & b", "https://lh.example/api/og/img/league/x.png?c=2&v=1",
            "https://lh.example/s/x", SiteName: "LeagueHub");
        var html = OgController.Inject("<html><head><title>x</title></head><body></body></html>", page);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&quot;Sepp&quot;", html);
        Assert.Contains("<meta property=\"og:site_name\" content=\"LeagueHub\">", html);
        Assert.Contains("content=\"a &amp; b\"", html);
        Assert.Contains("x.png?c=2&amp;v=1", html);
    }
}
