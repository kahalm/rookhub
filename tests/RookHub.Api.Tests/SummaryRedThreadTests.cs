using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

/// <summary>
/// <see cref="SummaryRedThread"/> (Wunsch 2026-11-25: roter Faden oben auf der Zusammenfassung und in der Link-Vorschau). Die Absätze
/// stammen aus den fertigen Zusammenfassungen auf Prod (Stand 25.11., Vereinspartien 8–268 und eigene Partien), Namen ersetzt.
/// Über den ganzen Bestand (143 Texte) trifft die Regel 45 — jeden Text mit „## Die Partie“ bis auf einen ohne roten Faden.
/// </summary>
public class SummaryRedThreadTests
{
    private const string Head = "# Schwaz – Gegner, Max (0-1)\n\n";
    private const string GameParagraph =
        "Die Partie begann als offener Sizilianer: 1.e4 c5 2.Sf3 Sc6 3.Sc3 Sf6 4.d4 cxd4 5.Sxd4 h5 Statt der Hauptzüge (5...e5 oder 5...d6) "
        + "schiebt Schwarz den h-Bauern. 29...Txc3!! Das Qualitätsopfer beseitigt den Verteidiger.";
    private const string Tail = "\n\n## Schlüsselstellungen\n\n### 29...Txc3!! – das Opfer\n\n```diagram\nply: 58\ncaption: Vor dem Opfer\n```\n\n"
        + "## Was man mitnehmen kann\n\n- Läufer brauchen offene Linien.\n";

    private static string NewText(string redThread) => $"{Head}## Die Partie\n\n{GameParagraph}\n\n{redThread}{Tail}";
    private static string OldText(string lastParagraph) =>
        $"{Head}## Zusammenfassung\n\nAus einer ruhigen Katalanisch-Struktur heraus geriet Schwarz in eine passive Stellung (14...Sd4, 23.Td2).\n\n{lastParagraph}{Tail}";

    [Theory]
    // Vereinspartie 264 (das Beispiel des Wunschs)
    [InlineData("Der rote Faden ist das Läuferpaar. Schwarz bekam es im siebten Zug und spielte danach nur noch darauf, ihm Linien zu verschaffen. "
        + "Dazu kam die Uhr: Laut den Zeitangaben in der Notation hatte Weiß nach dem 17. Zug noch 25 Minuten.")]
    // 235: Doppelpunkt direkt hinter „Faden“
    [InlineData("Der rote Faden: Der g- und der f-Bauer gingen vor, während der König in der Mitte blieb; rochiert wurde erst spät.")]
    // 255: „Der rote Faden der Partie …“
    [InlineData("Der rote Faden der Partie sind die Tauschentscheidungen rund um den Bauern e5. Der Springertausch auf e5 brachte Schwarz den Doppelbauern.")]
    // 246/249: „Der rote Faden für Schwarz / bei Weiß …“
    [InlineData("Der rote Faden für Schwarz ist der König: Auf e8 ließ er die Rochade aus, auf f8 sperrte er den eigenen Turm ein.")]
    // 48: „Zwei rote Fäden ziehen sich durch die Partie.“
    [InlineData("Zwei rote Fäden ziehen sich durch die Partie. Bei Schwarz ist es der Zug des c-Bauern nach c5, den die Engine ab dem 16. Zug immer wieder nennt.")]
    // 257/247/245: ohne „rote“
    [InlineData("Zwei Fäden ziehen sich durch die Partie. Der erste ist der Läufer, der zum Ziel wird: Der weiße auf h4 machte die Bauerngabel möglich.")]
    // eigene Partie 46: „Drei Fäden …“
    [InlineData("Drei Fäden ziehen sich durch die Partie. Weiß gab zweimal ein Schach, das eine eigene Deckung aufhob.")]
    // 251: „Motive“ statt „Fäden“
    [InlineData("Drei Motive ziehen sich durch die Partie: der Vorstoß des schwarzen e-Bauern, der zweimal gewann und zweimal unterblieb.")]
    // 254
    [InlineData("Durch die ganze Partie zieht sich ein einziger Bauernzug. Achtmal nennt die Engine f4-f5 als besten weißen Zug, zwischen Zug 14 und Zug 33.")]
    // 265: Felder in Klammern sind keine Partiezüge
    [InlineData("Der rote Faden ist der weißfeldrige Läufer. Er zog viermal (b5, c4, b3, d5), wurde seit dem zwölften Zug vom schwarzen c-Bauern bedrängt.")]
    // Fett am Absatzanfang ist keine Liste
    [InlineData("**Der rote Faden** ist die e-Linie. Der Vorstoß des e-Bauern schuf das Ziel für den weißen f-Hebel.")]
    public void Extract_LastParagraphOfTheGame_IsTheRedThread(string redThread)
    {
        Assert.Equal(redThread, SummaryRedThread.Extract(NewText(redThread)));
    }

    [Theory]
    // 232: alte Fassung mit „## Zusammenfassung“
    [InlineData("Zwei rote Fäden: Erstens der Tausch auf d5. In Zug 3 hätte er Weiß das Zentrum gebracht, in Zug 10 kam er im ungünstigen Moment.")]
    // 226: „Roter Faden …“ ohne Artikel
    [InlineData("Roter Faden aus schwarzer Sicht: Zweimal wurde eine klar bessere Stellung durch einen Abtausch oder einen Bauernzug verschenkt.")]
    // 241: „...Lf8“ ohne Zugnummer bleibt erlaubt
    [InlineData("Der rote Faden ist in beiden Fällen der Zug, der fehlte: ...Lf8. Der Läufer e7 stand auf der e-Linie im Schussfeld.")]
    public void Extract_OldSummarySection_AlsoCounts(string redThread)
    {
        Assert.Equal(redThread, SummaryRedThread.Extract(OldText(redThread)));
    }

    [Theory]
    // 234: mit Partiezügen
    [InlineData("Der rote Faden der zweiten Partiehälfte ist die Verwertung. Zweimal gab Weiß den Gewinn fast aus der Hand: 30.Lg3 kostete b2, und 44.gxh5 tauschte.")]
    [InlineData("Der rote Faden ist das Tempo. Ab 11. Sf3 droht jeder weiße Zug etwas.")]
    [InlineData("Der rote Faden ist das Tempo, Schwarz zog 21…Kg7 zu spät.")]
    // 9 / 224: zweiter Faden allein — gehört zu einem Absatz davor
    [InlineData("Auf weißer Seite gibt es einen zweiten roten Faden: Der Hebel exd5 war ab dem 14. Zug die erste Wahl der Engine.")]
    [InlineData("Der zweite rote Faden ist das Abwarten in der gedrückten Stellung. Schwarz zieht mit König und Turm hin und her.")]
    // 230: roter Faden erst im zweiten Satz
    [InlineData("Der dritte Teil ist ein Schwerfigurenendspiel, in dem Schwarz eine Reihe einziger Züge findet. Der rote Faden dort: Aktivität vor Material.")]
    // kein roter Faden
    [InlineData("Soweit die Notation trägt, fiel die Entscheidung zwischen dem achten und dem siebzehnten Zug.")]
    public void Extract_NoRedThread_ReturnsNull(string lastParagraph)
    {
        Assert.Null(SummaryRedThread.Extract(NewText(lastParagraph)));
        Assert.Null(SummaryRedThread.Extract(OldText(lastParagraph)));
    }

    [Fact]
    public void Extract_NotTheLastParagraph_ReturnsNull()
    {
        // 34: „Drei Fäden …“, aber „Zweitens …“ folgt im nächsten Absatz — allein nach oben gestellt ergäbe es keinen Sinn.
        var md = NewText("Drei Fäden ziehen sich durch das weiße Spiel. Erstens blieb der König bis zum Schluss auf e1.\n\nZweitens wanderte der Läufer und kostete zwei Tempi.");
        Assert.Null(SummaryRedThread.Extract(md));
    }

    [Fact]
    public void Extract_DiagramAfterTheParagraph_ReturnsNull()
    {
        var md = $"{Head}## Die Partie\n\n{GameParagraph}\n\nDer rote Faden ist das Läuferpaar.\n\n```diagram\nply: 14\n```\n\n## Schlüsselstellungen\n";
        Assert.Null(SummaryRedThread.Extract(md));
    }

    [Fact]
    public void Extract_OtherSection_ReturnsNull()
    {
        var md = $"{Head}## Die Partie\n\n{GameParagraph}\n\n## Was man mitnehmen kann\n\nDer rote Faden ist das Läuferpaar: es braucht offene Linien.\n";
        Assert.Null(SummaryRedThread.Extract(md));
    }

    [Fact]
    public void Extract_ParagraphOverSeveralLines_KeepsTheLinesTrimmed()
    {
        // CRLF und Leerraum an den Zeilenenden wie aus einem Editor; die Seite sucht genau diese Zeilen.
        var md = $"{Head}## Die Partie\r\n\r\n{GameParagraph}\r\n\r\n  Der rote Faden ist das Läuferpaar.  \r\nSchwarz bekam es im siebten Zug.\r\n";
        Assert.Equal("Der rote Faden ist das Läuferpaar.\nSchwarz bekam es im siebten Zug.", SummaryRedThread.Extract(md));
    }

    [Fact]
    public void Extract_EnglishText()
    {
        var md = "# A – B (1-0)\n\n## The game\n\n1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 White kept the pin.\n\nThe common thread is the pin on the e-file: Black never broke it.\n";
        Assert.Equal("The common thread is the pin on the e-file: Black never broke it.", SummaryRedThread.Extract(md));
    }

    /// <summary>Alle drei Antworten der Zusammenfassung tragen <c>redThread</c> (angemeldet, Teilen-Links, eigene Partie) — aus dem Text,
    /// den die jeweilige Antwort zeigt (bei <c>with { Markdown = … }</c> also aus dem gefilterten).</summary>
    [Fact]
    public void Dtos_SerializeRedThread_FromTheirOwnMarkdown()
    {
        const string thread = "Der rote Faden ist das Läuferpaar.";
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var club = new RookHub.Api.Services.League.ClubGameSummaryDto("done", NewText(thread), null, null, false, null, null);
        Assert.Contains("\"redThread\":\"Der rote Faden ist das L", System.Text.Json.JsonSerializer.Serialize(club, json).Replace("\\u00E4", "ä"));
        Assert.Null((club with { Markdown = null }).RedThread);
        Assert.Equal(thread, new RookHub.Api.Services.Games.SharedOwnGameSummaryDto("done", NewText(thread), null, false, null).RedThread);
        Assert.Equal(thread, new RookHub.Api.Services.Games.OwnGameSummaryDto("done", NewText(thread), null, null, false, null, null, null, "own", null, 2).RedThread);
        Assert.Null(new RookHub.Api.Services.League.ClubGameSummaryDto("none", null, null, null, false, null, null).RedThread);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# Nur ein Titel")]
    public void Extract_Empty_ReturnsNull(string? md)
    {
        Assert.Null(SummaryRedThread.Extract(md));
    }

    [Fact]
    public void Plain_StripsMarkdownAndJoinsLines()
    {
        Assert.Equal("Der rote Faden ist der Hebel f4-f5 und die Partie Kosakowski – Nie.",
            SummaryRedThread.Plain("Der **rote Faden** ist der Hebel *f4-f5*\nund die Partie [Kosakowski – Nie](/explorer/game/masters/6kBV5lDP?ply=14)."));
    }

    [Fact]
    public void Plain_ShortensAtAWordEnd()
    {
        var text = "Der rote Faden ist das Läuferpaar. " + string.Join(" ", Enumerable.Repeat("Schwarz spielte darauf, ihm Linien zu verschaffen.", 10));
        var plain = SummaryRedThread.Plain(text, 200);
        Assert.True(plain.Length <= 200, plain);
        Assert.EndsWith("…", plain);
        Assert.StartsWith("Der rote Faden ist das Läuferpaar. Schwarz", plain);
        Assert.DoesNotContain(" …", plain);
    }
}
