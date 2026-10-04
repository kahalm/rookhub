using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class StockfishPathTests
{
    [Fact]
    public void Resolve_ConfiguredPath_Wins()
        => Assert.Equal("/opt/sf/stockfish", StockfishPath.Resolve(" /opt/sf/stockfish ", _ => true));

    [Fact]
    public void Resolve_NothingConfigured_TakesTheDebianPackage_WhichIsNotOnThePath()
        // Im API-Image liegt das Debian-Paket unter /usr/games — nicht im PATH; „stockfish" allein fand dort nichts.
        => Assert.Equal("/usr/games/stockfish", StockfishPath.Resolve(null, p => p == "/usr/games/stockfish"));

    [Fact]
    public void Resolve_NoPackage_FallsBackToThePath()
        => Assert.Equal("stockfish", StockfishPath.Resolve("", _ => false));
}
