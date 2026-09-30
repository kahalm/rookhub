using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Reihenfolge-Regeln der Partie-Pumpe als reine Funktionen (<see cref="GameAnalysisTurnRules"/>) — ohne
/// Datenbank und ohne Pumpe. Das Verhalten der Pumpe selbst (welche Partie tatsaechlich Auftraege bekommt) pruefen
/// weiter die Fassaden-Tests in <c>GameAnalysisServiceTests</c> (<c>Pump_naechstePartie…</c>,
/// <c>Vertiefung_wartet…</c>); <c>RefineJobCap</c> und <c>SuspectPlies</c> stehen dort ebenfalls.
/// </summary>
public class GameAnalysisTurnRulesTests
{
    [Theory]
    [InlineData(0, 1, true)]     // nichts Aelteres offen: immer dran
    [InlineData(0, 16, true)]
    [InlineData(0, 0, true)]     // auch ohne Engine-Zahl — der Dienst fragt sie dann gar nicht erst ab
    [InlineData(15, 16, true)]   // Schwanz: weniger offen als Engines → sonst stuende eine Engine still
    [InlineData(1, 16, true)]
    [InlineData(16, 16, false)]  // genau so viele wie Engines: alle beschaeftigt, die neue Partie wartet
    [InlineData(40, 16, false)]
    [InlineData(1, 1, false)]    // feste Engine (1 Platz): eine Partie nach der anderen
    public void TailMayAdvance_nurWennDavorWenigerOffenIstAlsEnginesDaSind(int openAhead, int engineSlots, bool expected)
    {
        Assert.Equal(expected, GameAnalysisTurnRules.TailMayAdvance(openAhead, engineSlots));
    }

    [Theory]
    [InlineData(1, 1)]      // Vertiefung wartet, solange der erste Durchgang mindestens so viele offene
    [InlineData(16, 16)]    // Stellungen hat, wie Engines da sind (GameAnalysisService.IsOwnersRefineTurnAsync)
    [InlineData(30, 16)]
    public void TailMayAdvance_ersterDurchgangMitMindestensSovielenOffenenWieEngines_haeltDieVertiefungAuf(
        int firstPassOpen, int engineSlots)
    {
        Assert.False(GameAnalysisTurnRules.TailMayAdvance(firstPassOpen, engineSlots));
    }

    /// <summary>
    /// Doku-Bloecke der Pumpe stehen je über IHRER Methode: zwei <c>&lt;summary&gt;</c> in einem zusammenhaengenden
    /// <c>///</c>-Block heissen, dass einer über der falschen Methode steht (so stand bis W2 die Doku von
    /// <c>CreateForGuessAsync</c> samt Parametern über <c>CreateLibraryBatchAsync</c>).
    /// </summary>
    [Theory]
    [InlineData("Services/GameAnalysisService.cs")]
    [InlineData("Services/GameAnalysisTurnRules.cs")]
    public void DokuBloecke_stehenNichtGestapelt(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(ApiRoot(), file));
        var stacked = new List<int>();
        var summaries = 0;
        var start = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("///"))
            {
                if (summaries == 0 && (i == 0 || !lines[i - 1].TrimStart().StartsWith("///"))) start = i + 1;
                if (line.Contains("<summary>")) summaries++;
                continue;
            }
            if (summaries > 1) stacked.Add(start);
            summaries = 0;
        }
        Assert.True(stacked.Count == 0, $"{file}: gestapelte Doku-Bloecke ab Zeile {string.Join(", ", stacked)}");
    }

    private static string ApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");
    }
}
