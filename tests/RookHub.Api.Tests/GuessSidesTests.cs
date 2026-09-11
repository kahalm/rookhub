using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Welche Seite man in einer Punktepartie uebernimmt — die des GEWINNERS. Die Regel steht an EINER
/// Stelle, weil sie an zweien gebraucht wird: beim Starten einer Sitzung und in der Bestandsliste,
/// die vorher sagt, welche Farbe man spielt.
/// </summary>
public class GuessSidesTests
{
    [Fact]
    public void WinnerWhite_folgtDemErgebnis()
    {
        Assert.True(GuessSides.WinnerWhite("1-0", null, null));
        Assert.False(GuessSides.WinnerWhite("0-1", null, null));
    }

    /// <summary>Der Normalfall unserer Meisterpartien: das Buch nennt das Ergebnis nur im
    /// Fliesstext, die Kopfzeile traegt „*". Dann entscheidet die Bewertung der letzten Stellung.</summary>
    [Fact]
    public void WinnerWhite_ohneErgebnis_entscheidetDieBewertung()
    {
        // Halbzug 40 = Weiss am Zug, +3 Bauern → Weiss hat gewonnen.
        Assert.True(GuessSides.WinnerWhite("*", 40, 3.0));
        // Derselbe Vorsprung, aber Schwarz am Zug → er gehoert Schwarz.
        Assert.False(GuessSides.WinnerWhite("*", 41, 3.0));
        // Und mit umgekehrtem Vorzeichen jeweils andersherum.
        Assert.False(GuessSides.WinnerWhite("*", 40, -3.0));
        Assert.True(GuessSides.WinnerWhite("*", 41, -3.0));
    }

    /// <summary>Sagt die Bewertung nichts Deutliches (knapper Vorsprung, oder erst die Eroeffnung
    /// gerechnet), bleibt es bei Weiss — eine Seite muss es sein.</summary>
    [Fact]
    public void WinnerWhite_ohneDeutlichenVorsprung_bleibtBeiWeiss()
    {
        Assert.True(GuessSides.WinnerWhite("*", 40, 0.5));
        Assert.True(GuessSides.WinnerWhite("*", null, null));
        Assert.True(GuessSides.WinnerWhite(null, null, null));
    }

    /// <summary>Ein REMIS entscheidet nichts — dort gibt es keinen Gewinner, dessen Seite man
    /// uebernehmen koennte. Gewaehlt wird dann die Seite, die am Ende besser stand: in einem
    /// vereinbarten Remis aus gewonnener Stellung ist genau das die lehrreiche.</summary>
    [Fact]
    public void WinnerWhite_remis_folgtDerSchlussbewertung()
    {
        Assert.False(GuessSides.WinnerWhite("1/2-1/2", 40, -3.0));
        Assert.True(GuessSides.WinnerWhite("1/2-1/2", 40, 3.0));
    }

    [Fact]
    public void PawnsFromEvalText_liestDieKurzeSchreibweise()
    {
        Assert.Equal(0.35, GuessSides.PawnsFromEvalText("+0.35"));
        Assert.Equal(-1.2, GuessSides.PawnsFromEvalText("-1.2"));
        Assert.Equal(0, GuessSides.PawnsFromEvalText("0.00"));
    }

    /// <summary>Ein Matt schlaegt jede Materialbewertung, und ein kuerzeres schlaegt ein laengeres —
    /// dieselbe Uebersetzung wie in der Wertung.</summary>
    [Fact]
    public void PawnsFromEvalText_liestMatt()
    {
        var mateIn3 = GuessSides.PawnsFromEvalText("#3");
        var mateIn5 = GuessSides.PawnsFromEvalText("#5");
        var matedIn2 = GuessSides.PawnsFromEvalText("#-2");

        Assert.NotNull(mateIn3);
        Assert.True(mateIn3 > mateIn5);
        Assert.True(mateIn3 > 900);
        Assert.True(matedIn2 < -900);
    }

    [Fact]
    public void PawnsFromEvalText_ohneLesbaresGibtNull()
    {
        Assert.Null(GuessSides.PawnsFromEvalText(null));
        Assert.Null(GuessSides.PawnsFromEvalText(""));
        Assert.Null(GuessSides.PawnsFromEvalText("keine Zahl"));
        Assert.Null(GuessSides.PawnsFromEvalText("#"));
    }
}
