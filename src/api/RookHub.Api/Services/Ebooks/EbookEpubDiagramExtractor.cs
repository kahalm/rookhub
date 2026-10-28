using System.IO.Compression;
using System.Text.RegularExpressions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Diagramme aus EPUB-Büchern (0.775.0): dort sind sie BILDER. Alle Bilder des Buchs werden geprüft
/// (<see cref="EbookBoardImage.FindBoard"/>), die Bretter eines Buchs teilen sich Hintergrund und Figurensatz — deshalb erst
/// alle Bretter sammeln, dann die leeren Felder je Position lernen und erst danach erkennen. Nur plausible Stellungen
/// (je ein König, keine Bauern auf Grundreihen) werden Diagramme. Anker im Leser: <see cref="EbookDiagram.Page"/> = Kapitel
/// (Spine, 1-basiert), <see cref="EbookDiagram.ImageRef"/> = Bildpfad im EPUB. Lösungssuche wie beim PDF über die
/// Textabsätze (<see cref="EbookSolutionFinder"/>); ein Absatz zählt als Zeile, die Reihenfolge als „Höhe“.
/// </summary>
public class EbookEpubDiagramExtractor
{
    /// <summary>Höchstens so viele Bretter fließen in die Hintergrund-Schätzung (gleichmäßig verteilt) — Speicher.</summary>
    public const int BackgroundSample = 240;

    private static readonly Regex SideText = new(@"\b(White|Black|Weiß|Schwarz)\s+(to\s+(?:move|play)|am\s+Zug|zieht)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LabelText = new(@"^\s*(?:(?:Diagram|Diagramm|Exercise|Position|Problem|Puzzle|Aufgabe|Übung|Study)\s*)?(\d{1,4})[.):]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<EbookEpubDiagramExtractor> _logger;

    public EbookEpubDiagramExtractor(ILogger<EbookEpubDiagramExtractor> logger) => _logger = logger;

    /// <param name="keepUnread">Ergänzen-Modus (0.811.0): ein Brettbild mit unplausibler Lesung wird nicht verworfen, sondern
    /// mit der rohen Lesung als <see cref="EbookDiagramVisibility.Unread"/> angelegt (für Nutzer unsichtbar).</param>
    /// <param name="cnn">Scan-Netz als zweite Meinung, wenn der Vorlagen-Klassifikator kein stimmiges Brett liest (Standard: eingebautes Netz).</param>
    public EbookDiagramExtractor.Result Extract(string epubPath, int ebookId, CancellationToken ct, EbookBoardClassifier? model = null,
        bool keepUnread = false, EbookBoardCnn? cnn = null)
    {
        model ??= EbookBoardClassifier.Instance;
        cnn ??= EbookBoardCnn.Instance;
        using var zip = ZipFile.OpenRead(epubPath);
        var blocks = EbookEpubContent.Read(zip);
        // Durchgang 1: welche Bilder sind Bretter (nur das Rechteck merken — die Felder ALLER Bretter wären bei 1 000
        // Diagrammen ~300 MB). Durchgang 2: Hintergrund aus einer Stichprobe. Durchgang 3: Bild für Bild erkennen.
        var boards = new List<(int Block, (int X0, int Y0, int X1, int Y1) Box)>();
        var seen = new HashSet<string>();
        for (var i = 0; i < blocks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var img = blocks[i].Image;
            if (img == null || !seen.Add(img)) continue;   // dasselbe Bild zweimal (Wiederholung) nur einmal
            var g = LoadGray(zip, img);
            var box = g == null ? null : EbookBoardImage.FindBoard(g);
            if (box != null) boards.Add((i, box.Value));
        }

        var diagrams = new List<EbookDiagram>();
        var meta = new List<(int Index, int Page, int Column, double Bottom, string Fen, bool SideKnown, string? Label)>();
        if (boards.Count > 0)
        {
            var sampleIdx = boards.Count <= BackgroundSample ? Enumerable.Range(0, boards.Count)
                : Enumerable.Range(0, BackgroundSample).Select(k => (int)((long)k * boards.Count / BackgroundSample));
            var sample = new List<float[][,]>();
            foreach (var k in sampleIdx)
            {
                ct.ThrowIfCancellationRequested();
                var cells = LoadCells(zip, blocks[boards[k].Block].Image!, boards[k].Box);
                if (cells != null) sample.Add(cells);
            }
            if (sample.Count == 0) boards.Clear();
            var bg = sample.Count == 0 ? [] : EbookBoardImage.Backgrounds(sample);
            sample.Clear();
            var bgBlur = bg.Select(b => BoardImageMath.Blur(b, 1.6)).ToArray();
            foreach (var (bi, box) in boards)
            {
                ct.ThrowIfCancellationRequested();
                var block = blocks[bi];
                var g = LoadGray(zip, block.Image!);
                var cells = g == null ? null : EbookBoardImage.Cells(g, box);
                var grid = cells == null ? null : EbookBoardImage.ReadGrid(cells, bg, bgBlur, model);
                var board = grid == null ? null : EbookBoardImage.GridToBoard(grid);
                // Kein stimmiges Brett: das Scan-Netz probieren (0.865.0). Der Vorlagen-Klassifikator kennt manche Figurensätze
                // nicht (z. B. Merida-artige Figuren auf grauen hellen Feldern, „50 Chess Traps in the Opening“) und liest dann
                // Unsinn — so blieben 56 EPUB-Bücher bei 0 Diagrammen; das Scan-Netz liest dieselben Bilder sicher.
                float? cnnConfidence = null;
                if (board == null && g != null && cnn != null)
                {
                    var reading = cnn.Read(g, box.X0, box.Y0, box.X1, box.Y1);
                    if (EbookBoardImage.GridToBoard(reading.Grid) is string cnnBoard)
                        (grid, board, cnnConfidence) = (reading.Grid, cnnBoard, reading.MinConfidence);
                }
                if (board == null)
                {
                    if (!keepUnread || grid == null) continue;
                    // Unplausible Lesung: aufheben statt verwerfen — ohne Lösungssuche (die Stellung stimmt ja nicht).
                    diagrams.Add(new EbookDiagram
                    {
                        EbookId = ebookId, Page = block.Spine, Y = OrderY(block),
                        ImageRef = block.Image!.Length <= 300 ? block.Image : null,
                        Fen = $"{EbookBoardImage.GridToPlacement(grid)} w - - 0 1",
                        Label = GuessLabel(blocks, bi), Caption = Caption(blocks, bi),
                        FontFamily = EbookDiagramVisibility.Unread, Confidence = 0,
                    });
                    continue;
                }
                var (side, sideKnown) = GuessSide(blocks, bi);
                // Zugrecht-Zeichen im Bild (■/□, ▼/△) schlägt den Text.
                if (EbookBoardImage.SideMarker(g!, box) is char marked) (side, sideKnown) = (marked, true);
                var label = GuessLabel(blocks, bi);
                var fen = $"{board} {side} {DiagramGridBuilder.GuessCastling(board)} - 0 1";
                meta.Add((diagrams.Count, block.Spine, 0, block.Order * 10.0, fen, sideKnown, label));
                diagrams.Add(new EbookDiagram
                {
                    EbookId = ebookId,
                    Page = block.Spine,
                    Y = OrderY(block),
                    ImageRef = block.Image!.Length <= 300 ? block.Image : null,
                    Fen = fen,
                    Label = label,
                    Caption = Caption(blocks, bi),
                    SideKnown = sideKnown,
                    FontFamily = cnnConfidence == null ? "image" : "image-cnn",   // eigener Wert: „cnn“ meint Scan-PDF-Bretter (Opus-Stapel schneiden sie aus PDF-Seiten)
                    Confidence = cnnConfidence,
                });
            }
        }

        var lines = blocks.Where(b => b.Text != null)
            .Select(b => new PageLine(b.Spine, 0, b.Order * 10.0, b.Order * 10.0 + 5, 0, 0, b.Text!)).ToList();
        var solutions = diagrams.Count == 0 ? new Dictionary<int, FoundSolution>() : EbookSolutionFinder.Match(meta, lines);
        foreach (var (index, sol) in solutions)
        {
            var d = diagrams[index];
            var parts = d.Fen.Split(' ');
            d.Fen = $"{parts[0]} {sol.Side} {parts[2]} - 0 {sol.FirstMoveNumber}";
            d.SideKnown = true;
            d.SolutionUci = Clip(string.Join(' ', sol.Uci), 1200);
            d.SolutionSan = Clip(string.Join(' ', sol.San), 1500);
            d.SolutionText = sol.Text;
            d.SolutionPage = sol.Page;
            d.SolutionSource = "book";
        }
        _logger.LogInformation("E-Book {EbookId} (EPUB): {Boards} Brettbilder, {Count} Diagramme ({Unread} ungelesen), {Solutions} mit Lösung",
            ebookId, boards.Count, diagrams.Count, diagrams.Count(d => !EbookDiagramVisibility.IsReadable(d)), solutions.Count);
        return new EbookDiagramExtractor.Result(diagrams, boards.Count > 0, 0, solutions.Count);
    }

    /// <summary>„Höhe“ eines EPUB-Diagramms im Kapitel (0.811.0): die Blockreihenfolge — der Leser sortiert nach Kapitel, dann
    /// <see cref="EbookDiagram.Y"/>; vorher war Y immer 0 und die Reihenfolge im Kapitel zufällig. Die Lage (<see cref="EbookDiagram.W"/>)
    /// bleibt 0, daran erkennt man weiter ein EPUB-Diagramm.</summary>
    internal static double OrderY(EpubBlock block) => block.Order * 10.0;

    /// <summary>Höchstgröße EINES Bildeintrags im EPUB (das ganze Buch ist nicht begrenzt).</summary>
    internal const long MaxImageBytes = 8_000_000;

    private static float[,]? LoadGray(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry == null || entry.Length > MaxImageBytes) return null;
        try
        {
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return EbookBoardImage.DecodeGray(ms.ToArray());
        }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }
    }

    private static float[][,]? LoadCells(ZipArchive zip, string path, (int X0, int Y0, int X1, int Y1) box)
    {
        var g = LoadGray(zip, path);
        return g == null ? null : EbookBoardImage.Cells(g, box);
    }

    /// <summary>Zugrecht aus „White to move“ / „Schwarz am Zug“ in den zwei Absätzen davor oder danach (im selben Kapitel,
    /// nicht über ein anderes Bild hinweg).</summary>
    internal static (char Side, bool Known) GuessSide(IReadOnlyList<EpubBlock> blocks, int at)
    {
        foreach (var i in new[] { at + 1, at - 1, at + 2, at - 2 })
        {
            if (i < 0 || i >= blocks.Count || blocks[i].Spine != blocks[at].Spine) continue;
            if (Math.Abs(i - at) == 2 && blocks[(i + at) / 2].Image != null) continue;
            var t = blocks[i].Text;
            if (t == null || t.Length > 200) continue;
            var m = SideText.Match(t);
            if (m.Success) return (m.Groups[1].Value.ToLowerInvariant() is "white" or "weiß" ? 'w' : 'b', true);
        }
        return ('w', false);
    }

    /// <summary>Aufgabennummer: ein kurzer Absatz direkt vor (oder nach) dem Bild, der nur aus einer Zahl besteht
    /// („4“, „Exercise 12“).</summary>
    internal static string? GuessLabel(IReadOnlyList<EpubBlock> blocks, int at)
    {
        foreach (var i in new[] { at - 1, at - 2, at - 3, at + 1 })
        {
            if (i < 0 || i >= blocks.Count || blocks[i].Spine != blocks[at].Spine || blocks[i].Text == null) continue;
            var m = LabelText.Match(blocks[i].Text!);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>Überschrift: der kurze Absatz direkt vor dem Bild (Spieler, Ort, Jahr), wenn er keine Nummer ist.</summary>
    private static string? Caption(IReadOnlyList<EpubBlock> blocks, int at)
    {
        if (at == 0 || blocks[at - 1].Spine != blocks[at].Spine) return null;
        var t = blocks[at - 1].Text;
        if (t == null || t.Length > 120 || LabelText.IsMatch(t) || SideText.IsMatch(t)) return null;
        return Clip(t, 200);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
