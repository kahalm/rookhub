using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>dots.ocr als Formular-Leser: Layout-Antwort → Formular-Einträge, und der Weg bis zur aufgelösten Partie.</summary>
public class DotsOcrLayoutTests
{
    private static string Layout(params (string Category, string Text)[] elements)
        => JsonSerializer.Serialize(elements.Select((e, i) => new { bbox = new[] { 10, 10 + i * 100, 500, 90 + i * 100 }, category = e.Category, text = e.Text }));

    private static string Table(params string[][] rows)
        => "<table>" + string.Concat(rows.Select(r => "<tr>" + string.Concat(r.Select(c => $"<td>{c}</td>")) + "</tr>")) + "</table>";

    private static ScoresheetTranscription Transcribe(string raw)
    {
        var json = DotsOcrLayout.ToTranscriptionJson(raw);
        Assert.NotNull(json);
        return ScoresheetTranscription.Parse(json)!;
    }

    private static List<string> Written(ScoresheetTranscription t) => t.Moves.Select(m => m.Written).ToList();

    private static List<string> Resolved(ScoresheetTranscription t, string language)
        => ScoresheetResolver.Resolve(t.Scanned(), new ScoresheetResolver.Options(ScoresheetNotation.Find(language)))
            .Plies.Select(p => p.San.TrimEnd('+', '#')).ToList();

    [Fact]
    public void TwoBlocksSideBySide_AreReadBlockAfterBlock_AndResolveInPortuguese()
    {
        var raw = Layout(
            ("Title", "Súmula"),
            ("Text", "**Brancas:** João Silva\nPretas: Maria Souza"),
            ("Table", Table(
                ["Nº", "Brancas", "Pretas", "Nº", "Brancas", "Pretas"],
                ["1", "e4", "e5", "4", "c3", "Cf6"],
                ["2", "Cf3", "Cc6", "5", "d4", "exd4"],
                ["3", "Bc4", "Bc5", "6", "cxd4", "Bb4+"])));

        var t = Transcribe(raw);

        Assert.Equal(["e4", "e5", "Cf3", "Cc6", "Bc4", "Bc5", "c3", "Cf6", "d4", "exd4", "cxd4", "Bb4+"], Written(t));
        Assert.Equal([1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6], t.Moves.Select(m => m.MoveNumber).ToList());
        Assert.All(t.Moves, m => Assert.Null(m.San)); // dots.ocr deutet nicht — das macht der Auflöser
        Assert.Equal("João Silva", t.White);
        Assert.Equal("Maria Souza", t.Black);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6", "d4", "exd4", "cxd4", "Bb4"], Resolved(t, "pt"));
    }

    [Fact]
    public void TimeColumns_AreSkipped_MissingNumberIsInferred_AndResultCellBecomesResult()
    {
        var raw = Layout(("Table", Table(
            ["#", "White", "Time", "Black", "Time"],
            ["1", "e4", "1:05", "e5", "2"],
            ["", "Nf3", "3", "Nc6 4'", "5"], // Nummer nicht erkannt, Zeit in der Zugzelle
            ["3", "Bb5", "10", "a6", "12"],
            ["4", "1-0", "", "", ""])));

        var t = Transcribe(raw);

        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5", "a6"], Written(t));
        Assert.Equal(2, t.Moves[2].MoveNumber);
        Assert.Equal("1-0", t.Result);
        Assert.Equal("", t.White); // „White" ist hier eine Spaltenüberschrift, kein Name
    }

    [Fact]
    public void TwoTables_AndAnAnswerCutMidElement_AreMerged()
    {
        var left = Table(["1", "e4", "c5"], ["2", "Sf3", "d6"], ["3", "d4", "cxd4"]);
        var right = Table(["4", "Sxd4", "Sf6"], ["5", "Sc3", "a6"]);
        var full = Layout(("Table", left), ("Table", right), ("Text", "Unterschrift Weiß / Schwarz"));
        var cut = full[..(full.LastIndexOf("Unterschrift", StringComparison.Ordinal) + 5)]; // mitten im dritten Element

        var t = Transcribe(cut);

        Assert.Equal(["e4", "c5", "Sf3", "d6", "d4", "cxd4", "Sxd4", "Sf6", "Sc3", "a6"], Written(t));
        Assert.Equal(["e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "a6"], Resolved(t, "de"));
    }

    [Fact]
    public void TableWithoutNumbers_ReadsColumnPairs_BlockAfterBlock()
    {
        var raw = Layout(("Table", Table(
            ["White", "Black", "White", "Black"],
            ["e4", "e5", "Bc4", "Bc5"],
            ["Nf3", "Nc6", "c3", "Nf6"])));

        var t = Transcribe(raw);

        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6"], Written(t));
        Assert.Equal([1, 1, 2, 2, 3, 3, 4, 4], t.Moves.Select(m => m.MoveNumber).ToList());
    }

    [Fact]
    public void NoTable_TextLinesWithMoveNumbers_AreTheFallback()
    {
        var t = Transcribe(Layout(("Text", "Partie 3\n1. e4 e5\n2.Sf3 Sc6\n3. Lb5 a6")));

        Assert.Equal(["e4", "e5", "Sf3", "Sc6", "Lb5", "a6"], Written(t));
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5", "a6"], Resolved(t, "de"));
    }

    [Theory]
    [InlineData("I cannot read this image.")]
    [InlineData("[{\"bbox\":[1,2,3,4],\"category\":\"Picture\"}]")]
    [InlineData("")]
    public void NothingUsable_IsNull(string raw) => Assert.Null(DotsOcrLayout.ToTranscriptionJson(raw));

    [Fact]
    public void ColspanKeepsColumnsAligned_AndCellMarkupIsStripped()
    {
        var rows = DotsOcrLayout.ParseTable(
            "<table><tr><th colspan=\"3\">Moves</th><td>x</td></tr><tr><td>1</td><td><b>e4</b></td><td>e5&nbsp;</td><td>y</td></tr></table>");

        Assert.Equal(["Moves", "", "", "x"], rows[0]);
        Assert.Equal(["1", "e4", "e5", "y"], rows[1]);
    }

    [Fact]
    public void LayoutPrompt_IsTheVerbatimDotsOcrPrompt()
    {
        // Auf diesen Wortlaut ist das Modell trainiert — Einrückung und Zeilenende gehören dazu.
        Assert.StartsWith("Please output the layout information from the PDF image, including each layout element's bbox",
            DotsOcrScoresheetVisionClient.LayoutPrompt);
        Assert.Contains("\n\n3. Text Extraction & Formatting Rules:\n    - Picture:", DotsOcrScoresheetVisionClient.LayoutPrompt);
        Assert.EndsWith("5. Final Output: The entire output must be a single JSON object.\n", DotsOcrScoresheetVisionClient.LayoutPrompt);
    }

    [Fact]
    public async Task Client_SendsLayoutPromptWithImageMarker_AndReaderAsksOnlyOnce()
    {
        // Ab 3… geht die Lesung nicht mehr auf (Zz9, Qq1, Kk0) — trotzdem nur EIN Aufruf: eine Nachfrage ergäbe
        // bei dots.ocr dieselbe Lesung.
        var handler = new ChatCompletionHandler().Reply(Layout(("Table", Table(
            ["1", "e4", "e5"], ["2", "Df3", "Cc6"], ["3", "Bc4", "Zz9"], ["4", "Qq1", "Kk0"]))), input: 2000, output: 700);
        var client = new DotsOcrScoresheetVisionClient(new HttpClient(handler),
            new DotsOcrScoresheetVisionClient.Settings("http://spark/v1", "rednote-hilab/dots.ocr"), NullLogger.Instance);

        var outcome = await new ScoresheetReader(client).ReadAsync(new byte[] { 1, 2, 3 }, "pt", CancellationToken.None,
            maxRounds: 1);

        var request = Assert.Single(handler.Requests);
        var content = request.Body["messages"]![0]!["content"]!.AsArray();
        Assert.Equal("data:image/jpeg;base64,AQID", (string?)content[0]!["image_url"]!["url"]);
        Assert.Equal(DotsOcrScoresheetVisionClient.ImageMarker + DotsOcrScoresheetVisionClient.LayoutPrompt,
            (string?)content[1]!["text"]);
        Assert.Equal(0.1, (double?)request.Body["temperature"]);
        Assert.Single(request.Body["messages"]!.AsArray()); // kein System-Auftrag
        Assert.Null(outcome.Error);
        Assert.Equal(1, outcome.Rounds);
        Assert.Equal(["e4", "e5", "Qf3", "Nc6", "Bc4"], outcome.Resolution!.Plies.Take(5).Select(p => p.San).ToList());
        Assert.NotNull(client.LastRaw);
    }

    [Fact]
    public async Task Client_CutAnswerWithoutMoves_IsTruncated()
    {
        var handler = new ChatCompletionHandler().Reply("[{\"bbox\":[1,2,3,4],\"category\":\"Table\",\"text\":\"<table><tr><td>1", finish: "length");
        var client = new DotsOcrScoresheetVisionClient(new HttpClient(handler),
            new DotsOcrScoresheetVisionClient.Settings("http://spark/v1", "dots"), NullLogger.Instance);

        var result = await client.ReadAsync(new byte[] { 1 }, "ignored", 8000);

        Assert.Equal("truncated", result.Error);
    }
}
