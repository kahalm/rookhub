using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Tempo der Hintergrund-Analyse: nur der juengste zusammenhaengende Lauf zaehlt. Anlass (2026-09-24): die
/// Tempo-Zeile mittelte ueber die letzte Stunde samt einer Pause darin — „0,56 Stellungen/min · noch ca. 1 h 44 min"
/// fuer 58 Stellungen, die in einer Viertelstunde durch waren.
/// </summary>
public class AnalysisPaceTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PauseVorDemLauf_zaehltNicht()
    {
        // Eine alte Partie vor 50..41 Minuten, dann Pause, dann fuenf Stellungen in den letzten fuenf Minuten.
        var old = Enumerable.Range(41, 10).Select(m => Now.AddMinutes(-m));
        var fresh = Enumerable.Range(1, 5).Select(m => Now.AddMinutes(-m));

        var run = AnalysisPace.Measure(old.Concat(fresh), Now, 200)!.Value;

        Assert.Equal(5, run.Count);
        Assert.Equal(300, run.Seconds, 3);   // von vor fuenf Minuten bis jetzt
        Assert.Equal(1.0, run.PerMinute, 3);
    }

    [Fact]
    public void LueckenUnterDerPausengrenze_gehoerenZumLauf()
    {
        // Eine zaehe Stellung darf dauern: 14 Minuten Luecke zerschneiden den Lauf nicht.
        var stamps = new[] { Now.AddMinutes(-1), Now.AddMinutes(-15), Now.AddMinutes(-16) };
        Assert.Equal(3, AnalysisPace.Measure(stamps, Now, 200)!.Value.Count);
    }

    [Fact]
    public void LaufLebt_gemessenBisJetzt_ruht_gemessenBisZumLetztenErgebnis()
    {
        var stamps = new[] { Now.AddMinutes(-4), Now.AddMinutes(-2) };

        // Letztes Ergebnis vor 2 min: der Lauf lebt → bis jetzt (4 min).
        Assert.Equal(240, AnalysisPace.Measure(stamps, Now, 200)!.Value.Seconds, 3);
        // Drei Stunden spaeter ohne Ergebnis: die Analyse ruht → das Tempo, das der Lauf hatte (2 min), nicht drei Stunden.
        Assert.Equal(120, AnalysisPace.Measure(stamps, Now.AddHours(3), 200)!.Value.Seconds, 3);
    }

    [Fact]
    public void Untergrenze_EineMinute_undHoechstzahl()
    {
        Assert.Equal(60, AnalysisPace.Measure(new[] { Now, Now, Now }, Now, 200)!.Value.Seconds, 3);
        var many = Enumerable.Range(0, 50).Select(s => Now.AddSeconds(-s * 10));
        Assert.Equal(12, AnalysisPace.Measure(many, Now, 12)!.Value.Count);
        Assert.Null(AnalysisPace.Measure(Array.Empty<DateTime>(), Now, 200));
    }
}
