using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>UI-Sweep 2026-10-11 (r2-l-land-codes): Länderkürzel in den Hinweisen der Konto-Suche ausgeschrieben.</summary>
public class CountryNamesTests
{
    [Fact]
    public void GermanName_KnownCodes_AreSpelledOut_UnknownStay()
    {
        Assert.Equal("Slowakei", CountryNames.GermanName("SK"));
        Assert.Equal("Deutschland", CountryNames.GermanName("de"));
        Assert.Equal("Österreich", CountryNames.GermanName("AT"));
        Assert.Equal("QQ", CountryNames.GermanName("qq"));
    }

    [Fact]
    public void ExpandLand_RewritesOnlyLandCodes()
    {
        Assert.Equal("Klarname im Profil („Tomas Danada“); Land Slowakei; chess.com Blitz 2414",
            CountryNames.ExpandLand("Klarname im Profil („Tomas Danada“); Land SK; chess.com Blitz 2414"));
        Assert.Equal("Land Deutschland; Land Deutschland", CountryNames.ExpandLand("Land DE; Land Deutschland"));
        Assert.Equal("SK Weilheim", CountryNames.ExpandLand("SK Weilheim"));
        Assert.Null(CountryNames.ExpandLand(null));
    }

    [Fact]
    public void SuggestionJson_ExpandsStoredEvidence()
    {
        var x = new RookHub.Api.Models.LeagueAccountSuggestion { Id = 1, FideId = "123", Site = "chess.com", UserName = "danada", Url = "u", Evidence = "Land SK" };
        var o = LeagueOnlineAccountService.SuggestionJson(x);
        Assert.Equal("Land Slowakei", (string?)o["evidence"]);
    }
}
