using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary><see cref="EbookFigurineRepair"/>: welche Zugwörter als Figurensalat gelten und wann überhaupt Seiten gerendert werden (0.818.0).</summary>
public class EbookFigurineRepairTests
{
    [Theory]
    [InlineData("lt:Jxc4", 4)]
    [InlineData("�h3", 1)]
    [InlineData("'§'b8", 3)]
    [InlineData("::.g1", 3)]
    [InlineData("Nf3", 0)]       // echter Figurenbuchstabe
    [InlineData("Rxe5", 0)]
    [InlineData("♘xd5", 0)]      // schon Figurine
    [InlineData("xd5", 0)]       // nacktes Schlagen
    [InlineData("-xd5", 0)]
    [InlineData("e4", 0)]        // Bauernzug ohne Präfix
    [InlineData("exd5", 0)]      // Bauernschlag
    [InlineData("Wie", 0)]       // kein Feld
    public void SaladPrefix_ErkenntNurSalatVorDemFeld(string word, int expected) =>
        Assert.Equal(expected, EbookFigurineRepair.SaladPrefix(word));

    /// <summary>Eine Seite mit <paramref name="words"/> Wörtern „1.lt:Jxc4“ in einer Zeile je Wort.</summary>
    private static ReflowPage SaladPage(int number, int words)
    {
        var gs = new List<ReflowGlyph>();
        for (var w = 0; w < words; w++)
        {
            double x = 50, y = 60 + w * 14;
            foreach (var ch in "1.lt:Jxc4")
            {
                gs.Add(new ReflowGlyph(x, x + 5, y, 10, ch.ToString(), "Times", false, false));
                x += 5;
            }
        }
        return new ReflowPage(number, 400, 600, gs);
    }

    [Fact]
    public void WenigSalat_RendertNichtsUndLaesstDenTextStehen()
    {
        var doc = new EbookReflowPdfReader.Document([SaladPage(1, 10), SaladPage(2, 10)], [], null, null);
        var rendered = 0;
        var outDoc = EbookFigurineRepair.Repair(doc, _ => { rendered++; return null; }, out var repaired);
        Assert.Same(doc, outDoc);
        Assert.Equal(0, rendered);
        Assert.Equal(0, repaired);
    }

    [Fact]
    public void VielSalat_RendertJedeBetroffeneSeiteEinmal()
    {
        var doc = new EbookReflowPdfReader.Document([SaladPage(1, 30), SaladPage(2, 0), SaladPage(3, 30)], [], null, null);
        var pages = new System.Collections.Concurrent.ConcurrentBag<int>();   // Seiten werden parallel gerendert
        EbookFigurineRepair.Repair(doc, p => { pages.Add(p); return null; }, out var repaired);
        Assert.Equal([1, 3], pages.Order());
        Assert.Equal(0, repaired);   // ohne Seitenbild keine Lesung
    }
}
