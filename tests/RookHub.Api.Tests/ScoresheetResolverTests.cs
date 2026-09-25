using System.Diagnostics;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ScoresheetResolverTests
{
    /// <summary>
    /// Die Partie vom Foto, das den Anlass gab (Simultan Schwaz, 5.6.2026): so, wie sie auf dem deutschen
    /// Formular steht — inklusive des Lesefehlers in Zug 25 („Qxd4" statt exd4) und der mehrdeutigen
    /// Springerzüge „Sd2"/„Sd4". Von Hand abgeschrieben und mit python-chess und Stockfish geprüft.
    /// </summary>
    private static readonly string[] SheetGerman =
    {
        "Sf3", "d5", "g3", "Sf6", "Lg2", "e6", "0-0", "Le7", "b3", "0-0", "Lb2", "c5", "d4", "Sc6", "Sd2", "b5",
        "dxc5", "Lxc5", "c4", "bxc4", "bxc4", "Le7", "Tc1", "Db6", "Lxf6", "Lxf6", "cxd5", "exd5", "e3", "Lf5",
        "Sb3", "Tad8", "Sd4", "Sxd4", "Sxd4", "Le4", "Dd2", "Tc8", "h4", "h5", "Lh3", "Tc4", "Txc4", "dxc4",
        "Tc1", "Ld3", "Lf1", "Lxd4", "Qxd4", "Dxd4", "Td1", "Td8", "Dg5", "Td5", "De7", "Tf5", "De8+", "Kh7",
        "De1", "Tf3", "Lxd3", "Txd3", "Txd3", "Dxd3", "De7", "Df3",
    };

    private static readonly string[] Expected =
    {
        "Nf3", "d5", "g3", "Nf6", "Bg2", "e6", "O-O", "Be7", "b3", "O-O", "Bb2", "c5", "d4", "Nc6", "Nbd2", "b5",
        "dxc5", "Bxc5", "c4", "bxc4", "bxc4", "Be7", "Rc1", "Qb6", "Bxf6", "Bxf6", "cxd5", "exd5", "e3", "Bf5",
        "Nb3", "Rad8", null!, "Nxd4", "Nxd4", "Be4", "Qd2", "Rc8", "h4", "h5", "Bh3", "Rc4", "Rxc4", "dxc4",
        "Rc1", "Bd3", "Bf1", "Bxd4", "exd4", "Qxd4", "Rd1", "Rd8", "Qg5", "Rd5", "Qe7", "Rf5", "Qe8+", "Kh7",
        "Qe1", "Rf3", "Bxd3+", "Rxd3", "Rxd3", "Qxd3", "Qe7", "Qf3",
    };

    private static List<ScannedPly> WrittenOnly(IEnumerable<string> written)
        => written.Select(w => new ScannedPly(w, null)).ToList();

    private static readonly ScoresheetResolver.Options German = new(ScoresheetNotation.Find("de"));

    [Fact]
    public void Resolve_GermanSheetWithoutModelReading_ReconstructsTheWholeGame()
    {
        var sw = Stopwatch.StartNew();
        var r = ScoresheetResolver.Resolve(WrittenOnly(SheetGerman), German);
        sw.Stop();

        Assert.Null(r.StuckAt);
        Assert.Equal(SheetGerman.Length, r.Plies.Count);
        for (var i = 0; i < Expected.Length; i++)
        {
            if (Expected[i] == null) continue; // 17. Sd4: beide Springer führen nach 18. Sxd4 zur selben Stellung
            Assert.True(Expected[i] == r.Plies[i].San, $"Halbzug {i}: erwartet {Expected[i]}, gelesen {r.Plies[i].San}");
        }
        // Rechenzeit-Deckel: die ganze Partie samt Ausgängen in wenigen Sekunden.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"zu langsam: {sw.Elapsed}");
    }

    [Fact]
    public void Resolve_AmbiguousKnightMove_IsDecidedByALaterMove_NotAskedAbout()
    {
        // „Sd2" allein kann Sbd2 oder Sfd2 sein; erst 18. Sxd4 (der f3-Springer muss noch stehen) entscheidet.
        var r = ScoresheetResolver.Resolve(WrittenOnly(SheetGerman), German);
        Assert.Equal("Nbd2", r.Plies[14].San);
        // Entschieden, nicht geraten: die andere Lesart trägt nicht weit genug, um den Nutzer zu fragen.
        Assert.False(r.Plies[14].Uncertain);
    }

    [Fact]
    public void Resolve_MisreadCapture_IsRepairedAndMarkedUncertain_WithOptions()
    {
        var r = ScoresheetResolver.Resolve(WrittenOnly(SheetGerman), German);
        var ply = r.Plies[48]; // 25. „Qxd4" → exd4
        Assert.Equal("exd4", ply.San);
        Assert.True(ply.Uncertain);
        Assert.NotNull(ply.Options);
        Assert.Equal("exd4", ply.Options![0].San);
        Assert.Equal(SheetGerman.Length - 49, ply.Options[0].Reach); // die gewählte Lesart trägt den Rest glatt
        Assert.InRange(ply.Options.Count, 1, ScoresheetResolver.BranchWidth);
    }

    [Fact]
    public void Resolve_TranspositionAmbiguity_IsShownAsEquallyGood()
    {
        // 17. „Sd4": beide Springer führen nach 17…Sxd4 18. Sxd4 zur selben Stellung — nur der Spieler weiß,
        // welcher es war. Beide Lesarten tragen gleich weit, die Stelle ist ehrlich unsicher.
        var r = ScoresheetResolver.Resolve(WrittenOnly(SheetGerman), German);
        var ply = r.Plies[32];
        Assert.True(ply.Uncertain);
        Assert.Contains(ply.Options!, o => o.San == "Nbd4");
        Assert.Contains(ply.Options!, o => o.San == "Nfd4");
        Assert.Equal(ply.Options![0].Reach, ply.Options.Single(o => o.San != ply.San && o.San is "Nbd4" or "Nfd4").Reach);
    }

    [Fact]
    public void Resolve_WrittenEntryWins_OverADifferentModelReading_WhenBothAreLegal_AndIsMarked()
    {
        // Das Modell deutet „Sf6" (Zug 2) als Nd7 — beides legal. Was DASTEHT gewinnt (Beleg 06 des Testsatzes: das
        // Modell „korrigierte" ein richtiges „De4"), und der Widerspruch wird angezeigt, mit beiden Zügen.
        var sheet = WrittenOnly(SheetGerman.Take(6));
        sheet[3] = new ScannedPly("Sf6", "Nd7");
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Equal("Nf6", r.Plies[3].San);
        Assert.True(r.Plies[3].Uncertain);
        Assert.Contains(r.Plies[3].Options!, o => o.San == "Nd7");
    }

    [Fact]
    public void Resolve_ModelReading_CarriesOn_WhereTheEntryIsIllegal()
    {
        // Der Eintrag geht nicht, die Deutung des Modells schon — dann trägt die Deutung (wie „bxa4" → bxc4 in Beleg 01).
        var sheet = new List<ScannedPly>
        {
            new("e4", null), new("d5", null), new("exc5", "exd5"), new("Dxd5", null), new("Sc3", null),
        };
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Equal(new[] { "e4", "d5", "exd5", "Qxd5", "Nc3" }, r.Plies.Select(p => p.San));
        Assert.Equal(ScoresheetResolver.Matches.Exact, r.Plies[2].Match);
    }

    [Fact]
    public void Resolve_ALegalAlternativeNamedByTheModel_MarksThePly_EvenAtHighConfidence()
    {
        // 08 des Testsatzes: „high", aber mit Alternative — und die Alternative war die Wahrheit.
        var sheet = WrittenOnly(SheetGerman.Take(8));
        sheet[3] = new ScannedPly("Sf6", "Nf6", new[] { "Nh6" }, "high");
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Equal("Nf6", r.Plies[3].San);
        Assert.True(r.Plies[3].Uncertain);
        Assert.Contains(r.Plies[3].Options!, o => o.San == "Nh6");
    }

    [Fact]
    public void Resolve_MediumConfidence_WithoutAlternative_IsNotMarked()
    {
        // „medium" vergibt das Modell freigiebig — ohne genannte Alternative ist das kein Grund zu markieren.
        var sheet = WrittenOnly(SheetGerman.Take(8));
        sheet[3] = new ScannedPly("Sf6", "Nf6", null, "medium");
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.False(r.Plies[3].Uncertain);
    }

    [Fact]
    public void Resolve_UnreadableEntry_IsInferredFromTheFollowingMoves()
    {
        // 9. dxc5 ist unleserlich („??"), aber 9…Lxc5 legt ihn fest: nur ein weißer Schlag auf c5 lässt den
        // Läufer dort schlagen.
        var sheet = WrittenOnly(SheetGerman.Take(20));
        sheet[16] = new ScannedPly("??", null, Confidence: "low");
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Null(r.StuckAt);
        Assert.Equal("dxc5", r.Plies[16].San);
        Assert.Equal(ScoresheetResolver.Matches.Guess, r.Plies[16].Match);
        Assert.True(r.Plies[16].Uncertain);
    }

    [Fact]
    public void Resolve_Guess_NeedsConfirmationByTheNextEntry()
    {
        // Der LETZTE Eintrag ist Unsinn, aber nicht als unleserlich markiert: kein Nachfolger bestätigt einen
        // Joker, also wird nichts erfunden.
        var sheet = WrittenOnly(new[] { "e4", "e5", "Sf3", "Zz9" });
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Equal(3, r.StuckAt);
        Assert.Equal(3, r.Plies.Count);
    }

    [Fact]
    public void Resolve_Nonsense_StopsWithTheRestUnresolved()
    {
        var sheet = WrittenOnly(new[] { "e4", "e5", "Kxh8", "Qxh1", "Zz9", "Yy8", "Xw7", "Vv6", "Uu5" });
        var r = ScoresheetResolver.Resolve(sheet, new ScoresheetResolver.Options(null));
        Assert.NotNull(r.StuckAt);
        Assert.True(r.Plies.Count < sheet.Count);
        Assert.NotEmpty(r.Unresolved);
        Assert.NotNull(r.StuckFen);
    }

    [Fact]
    public void Resolve_FromAPrefix_ResolvesOnlyTheRest()
    {
        // Der Nutzer hat 25. exd4 selbst bestätigt — der Rest wird ab dort neu aufbereitet.
        var prefix = Expected.Take(49).Select((s, i) => s ?? "Nbd4").ToList();
        var r = ScoresheetResolver.Resolve(WrittenOnly(SheetGerman), German, prefix, writtenFrom: 49);
        Assert.Null(r.StuckAt);
        Assert.Equal(SheetGerman.Length - 49, r.Plies.Count);
        Assert.Equal("Qxd4", r.Plies[0].San);
        Assert.Equal(49, r.Plies[0].W);
    }

    [Fact]
    public void Resolve_AutoLanguage_ReadsAFrenchSheet()
    {
        // Französisch: C = Springer, F = Läufer, R = König.
        var sheet = WrittenOnly(new[] { "e4", "e5", "Cf3", "Cc6", "Fb5", "a6", "Fa4", "Cf6", "0-0", "Fe7" });
        var r = ScoresheetResolver.Resolve(sheet, new ScoresheetResolver.Options(null));
        Assert.Null(r.StuckAt);
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "O-O", "Be7" }, r.Plies.Select(p => p.San));
    }

    [Fact]
    public void Resolve_LongNotation_AndFigurines()
    {
        var sheet = WrittenOnly(new[] { "e2-e4", "e7-e5", "♘g1-f3", "Sb8-c6", "Lf1:b5" });
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" }, r.Plies.Select(p => p.San));
    }

    [Fact]
    public void Resolve_AMoveMissingOnTheSheet_IsInserted_AndTheRestStaysInStep()
    {
        // Der Spieler hat 3…Sf6 nicht notiert: ab dort stehen alle Einträge einen Halbzug zu früh (Farben vertauscht).
        var sheet = WrittenOnly(new[] { "e4", "e5", "Sf3", "Sc6", "Lb5", "a6", "La4", "Sf6", "0-0", "Le7" }
            .Where((_, i) => i != 3).ToList());
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Null(r.StuckAt);
        Assert.Equal(10, r.Plies.Count);
        Assert.Equal(ScoresheetResolver.Matches.Inserted, r.Plies[3].Match);
        Assert.Null(r.Plies[3].W);
        Assert.True(r.Plies[3].Uncertain);
        Assert.Equal(new[] { "Bb5", "a6", "Ba4", "Nf6", "O-O", "Be7" }, r.Plies.Skip(4).Select(p => p.San));
    }

    [Fact]
    public void Resolve_AnEntryWrittenTwice_IsSkipped()
    {
        var sheet = WrittenOnly(new[] { "e4", "e5", "Sf3", "Sf3", "Sc6", "Lb5", "a6", "La4", "Sf6" });
        var r = ScoresheetResolver.Resolve(sheet, German);
        Assert.Null(r.StuckAt);
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6" }, r.Plies.Select(p => p.San));
        var skip = Assert.Single(r.Skipped);
        Assert.Equal("Sf3", skip.Written);
    }

    [Fact]
    public void Candidates_LowercaseFileLetter_PrefersThePawn()
    {
        // „bxa4" in englischer Notation: Bauer (b-Linie) oder Läufer? Klein geschrieben: der Bauer ist billiger.
        var c = ScoresheetNotation.Candidates("bxa4", ScoresheetNotation.Find("en"));
        Assert.True(c.Single(x => x.Key == "ba4").Cost < c.Single(x => x.Key == "Ba4").Cost);
    }

    [Fact]
    public void Fuzzy_ConfusableCharacters_AreCheaper_AndDeletionsDearer()
    {
        Assert.True(ScoresheetNotation.IsConfusable("Rf8", "Rf6"));
        Assert.False(ScoresheetNotation.IsConfusable("Rf8", "Ra8"));
        Assert.Equal(1.0, ScoresheetNotation.WeightedDistance("ba4", "bc4"));
        Assert.Equal(1.2, ScoresheetNotation.WeightedDistance("ba4", "a4"));
    }
}
