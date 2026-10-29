using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>
/// Buch-Übersetzung (0.876.0): Blöcke herauslösen und wieder einsetzen — Markup (ids, Klassen, Bilder, Anker) und
/// Schachnotation bleiben Byte für Byte, nur Prosa geht ans Modell; was nicht passt, bleibt im Original.
/// </summary>
public class EbookTextSegmenterTests
{
    private static List<EbookTextSegment> Segments(string html) => EbookTextSegmenter.Parse(html).Segments.ToList();

    private static string Rebuild(string html, Func<string, string?> t) => EbookTextSegmenter.Parse(html).Rebuild(t, out _);

    [Fact]
    public void Parse_IdentityRebuild_GivesTheSameDocumentBack()
    {
        const string html = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"de\"><head><title>Kapitel</title>" +
            "<style>p{margin:0}</style></head><body>\n<h1 id=\"c1\">Kapitel 1</h1>\n<p class=\"t\" id=\"p2\">Der Läufer ist <b>stark</b>, nicht wahr?</p>" +
            "<div><img src=\"../Images/d1.png\" alt=\"\"/></div></body></html>";
        Assert.Equal(html, Rebuild(html, m => m));
        Assert.Equal(html, Rebuild(html, _ => null));
    }

    [Fact]
    public void Parse_KeepsInlineMarkupAsPairedPlaceholders()
    {
        var seg = Assert.Single(Segments("<p class=\"x\" id=\"a1\">Der Läufer ist <b>stark</b>.</p>"));
        Assert.Equal("Der Läufer ist <t1>stark</t1>.", seg.ModelText);
        var html = Rebuild("<p class=\"x\" id=\"a1\">Der Läufer ist <b>stark</b>.</p>", _ => "The bishop is <t1>strong</t1>.");
        Assert.Equal("<p class=\"x\" id=\"a1\">The bishop is <b>strong</b>.</p>", html);
    }

    [Fact]
    public void Parse_MasksMoveSequencesAndEvaluationSymbols()
    {
        const string html = "<p>Nach 12.Sf3 Sc6 13.Lb5 steht Weiß besser ±, denn 13...a6? scheitert an 14.Lxc6+!</p>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Nach <t1/> steht Weiß besser <t2/>, denn <t3/> scheitert an <t4/>", seg.ModelText);
        var result = Rebuild(html, _ => "After <t1/> White is better <t2/>, since <t3/> fails to <t4/>");
        Assert.Equal("<p>After 12.Sf3 Sc6 13.Lb5 White is better ±, since 13...a6? fails to 14.Lxc6+!</p>", result);
    }

    [Fact]
    public void Parse_FigurineInsideSpan_StaysOneNotationPlaceholder()
    {
        const string html = "<p>Nur 33.<span class=\"fig\">♖</span>d1! gewinnt die Partie.</p>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Nur <t1/> gewinnt die Partie.", seg.ModelText);
        Assert.Equal("<p>Only 33.<span class=\"fig\">♖</span>d1! wins the game.</p>", Rebuild(html, _ => "Only <t1/> wins the game."));
    }

    [Theory]
    [InlineData("<p>1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.0-0</p>")]
    [InlineData("<p>12...Dxd4 13.Txd4 ⩲</p>")]
    [InlineData("<p>27</p>")]
    [InlineData("<p>[White \"Carlsen, Magnus\"]</p>")]
    [InlineData("<p>Karpow – Kasparow, Moskau 1985</p>")]
    [InlineData("<p><b>Anand, V. – Topalov, V.</b> Sofia 2010</p>")]
    [InlineData("<p>r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R</p>")]
    public void Parse_NotationOnlyOrGameHeaders_AreNotTranslated(string html)
    {
        Assert.Empty(Segments(html));
        Assert.Equal(html, Rebuild(html, _ => "SHOULD NOT APPEAR"));
    }

    [Fact]
    public void Parse_HeadScriptSvgAndPre_AreNeverTouched()
    {
        const string html = "<html><head><title>Titel des Buchs</title></head><body><svg><text x=\"1\">a8 Turm</text></svg>" +
            "<pre>Weiß zieht und gewinnt</pre><script>var a = 'Text';</script><p>Weiß zieht und gewinnt.</p></body></html>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Weiß zieht und gewinnt.", seg.ModelText);
    }

    [Fact]
    public void Parse_ImageInsideParagraph_IsKeptWithItsPath()
    {
        const string html = "<p>Das Diagramm <img src=\"../Images/Board%201.png\" alt=\"Diagramm\"/> zeigt die Stellung.</p>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Das Diagramm <t1/> zeigt die Stellung.", seg.ModelText);
        Assert.Equal("<p>The diagram <img src=\"../Images/Board%201.png\" alt=\"Diagramm\"/> shows the position.</p>",
            Rebuild(html, _ => "The diagram <t1/> shows the position."));
    }

    [Fact]
    public void Parse_LeadingAndTrailingWrappers_StayOutsideTheModelText()
    {
        const string html = "<li><a href=\"ch2.xhtml#s3\"><span class=\"n\">Die Damengambit-Struktur</span></a></li>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Die Damengambit-Struktur", seg.ModelText);
        Assert.Equal("<li><a href=\"ch2.xhtml#s3\"><span class=\"n\">The Queen's Gambit structure</span></a></li>",
            Rebuild(html, _ => "The Queen's Gambit structure"));
    }

    [Fact]
    public void Rebuild_EscapesTextAndDecodesEntities()
    {
        const string html = "<p>Tom &amp; Jerry spielen&nbsp;Schach &lt;heute&gt;.</p>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Tom & Jerry spielen Schach <heute>.", seg.ModelText);
        Assert.Equal("<p>Tom &amp; Jerry play chess &lt;today&gt;.</p>", Rebuild(html, _ => "Tom & Jerry play chess <today>."));
    }

    [Fact]
    public void Rebuild_MissingOrInventedPlaceholder_KeepsTheOriginalAndCountsIt()
    {
        const string html = "<p>Der Läufer ist <b>stark</b>.</p>";
        var doc = EbookTextSegmenter.Parse(html);
        Assert.Equal(html, doc.Rebuild(_ => "The bishop is strong.", out var rejected));
        Assert.Equal(1, rejected);
        Assert.Equal(html, doc.Rebuild(_ => "The <t2/> bishop is <t1>strong</t1>.", out rejected));
        Assert.Equal(1, rejected);
        Assert.Equal(html, doc.Rebuild(_ => "The bishop is </t1>strong<t1>.", out rejected));
        Assert.Equal(1, rejected);
    }

    [Fact]
    public void Rebuild_BrokenNesting_KeepsTheOriginal()
    {
        const string html = "<p>Ein <b>sehr <i>guter</i> Zug</b> hier.</p>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Ein <t1>sehr <t2>guter</t2> Zug</t1> hier.", seg.ModelText);
        Assert.True(EbookTextSegmenter.SamePlaceholders(seg.ModelText, "A <t1>very <t2>good</t1> move</t2> here."));
        Assert.Null(EbookTextSegmenter.Apply(seg, "A <t1>very <t2>good</t1> move</t2> here."));
        Assert.Equal("A <b>very <i>good</i> move</b> here.", EbookTextSegmenter.Apply(seg, "A <t1>very <t2>good</t2> move</t1> here."));
        // Umstellen ist erlaubt, solange die Verschachtelung stimmt.
        Assert.Equal("<i>Good</i> and <b>very</b>.", EbookTextSegmenter.Apply(
            EbookTextSegmenter.Parse("<p><b>Sehr</b> und <i>gut</i>.</p>").Segments.Single(), "<t2>Good</t2> and <t1>very</t1>."));
    }

    [Fact]
    public void Parse_LineBreakAndAdjacentTags_BecomeOneVoidPlaceholder()
    {
        const string html = "<p>Erste Zeile<br/>zweite Zeile</p>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Erste Zeile<t1/>zweite Zeile", seg.ModelText);
        Assert.Equal("<p>First line<br/>second line</p>", Rebuild(html, _ => "First line<t1/>second line"));
    }

    [Fact]
    public void Parse_NcxNavLabels_AreSegments()
    {
        const string ncx = "<?xml version=\"1.0\"?><ncx><head><meta name=\"dtb:uid\" content=\"x\"/></head><docTitle><text>Mein Buch</text></docTitle>" +
            "<navMap><navPoint id=\"n1\"><navLabel><text>Einleitung</text></navLabel><content src=\"ch1.xhtml\"/></navPoint></navMap></ncx>";
        Assert.Equal(["Mein Buch", "Einleitung"], Segments(ncx).Select(s => s.ModelText));
        var result = Rebuild(ncx, m => m == "Einleitung" ? "Introduction" : null);
        Assert.Contains("<navLabel><text>Introduction</text></navLabel><content src=\"ch1.xhtml\"/>", result);
        Assert.Contains("<text>Mein Buch</text>", result);
    }

    [Fact]
    public void Parse_PrivateUseFigurines_DoNotClashWithTagMarkers()
    {
        // Schachschrift mit Figurinen im Privatbereich (U+E000 ff.) und mehreren Tags im selben Absatz.
        const string html = "<p>Hier <i>ist</i> f3 mit <b>Idee</b> g5.</p>";
        var seg = Assert.Single(Segments(html));
        Assert.Equal("Hier <t1>ist</t1> <t2/> mit <t3>Idee</t3> <t4/>.", seg.ModelText);
        Assert.Equal("<p>Here <i>is</i> f3 with <b>idea</b> g5.</p>",
            Rebuild(html, _ => "Here <t1>is</t1> <t2/> with <t3>idea</t3> <t4/>."));
    }

    [Theory]
    [InlineData("12.Sf3", true)]
    [InlineData("Nf3", true)]
    [InlineData("exd5", true)]
    [InlineData("O-O-O", true)]
    [InlineData("0-0", true)]
    [InlineData("e8=Q+", true)]
    [InlineData("Kxe4#", true)]
    [InlineData("12...", true)]
    [InlineData("12…", true)]
    [InlineData("Ng1-f3", true)]
    [InlineData("±", true)]
    [InlineData("!?", true)]
    [InlineData("1-0", true)]
    [InlineData("½-½", true)]
    [InlineData("Крf2", true)]
    [InlineData("Nf3.", true)]
    [InlineData("Weiß", false)]
    [InlineData("the", false)]
    [InlineData("a", false)]
    [InlineData("1985", false)]
    [InlineData("Chapter", false)]
    public void IsNotation_RecognisesMovesAndSymbols(string token, bool expected)
        => Assert.Equal(expected, EbookTextSegmenter.IsNotation(token));

    [Fact]
    public void EpubTranslation_SetsTheTargetLanguage()
    {
        Assert.Equal("<html xmlns=\"x\" lang=\"en\" xml:lang=\"en\"><body/></html>",
            EbookEpubTranslation.SetHtmlLanguage("<html xmlns=\"x\" lang=\"de\" xml:lang=\"de\"><body/></html>", "en"));
        Assert.Equal("<metadata><dc:language>en</dc:language></metadata>",
            EbookEpubTranslation.SetOpfLanguage("<metadata><dc:language>de-DE</dc:language></metadata>", "en"));
    }
}
