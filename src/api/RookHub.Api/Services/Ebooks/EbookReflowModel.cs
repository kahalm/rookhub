namespace RookHub.Api.Services.Ebooks;

// Zwischenmodell des Fließtext-EPUBs (0.802.0): PDF-Seiten → Glyphen → Absätze/Bilder → Kapitel → EPUB.
// Die Stufen sind getrennt, damit Tests die Layout-Regeln ohne echte PDF-Datei prüfen können.

/// <summary>Ein Zeichen (oder eine Ligatur) des normalen Texts: Vorschub X0…X1 und Grundlinie in PDF-Punkten (oben = 0),
/// Schriftgröße, schon übersetzter Text (Figurinen → ♔…, Ligaturen aufgelöst), Schrift + fett/kursiv.</summary>
public sealed record ReflowGlyph(double X0, double X1, double Baseline, double Size, string Text, string Font, bool Bold, bool Italic);

/// <summary>Eine PDF-Seite (Nummer ab 1, Größe in Punkten) mit ihren Text-Glyphen (Diagramm-Fonts sind schon draußen).</summary>
public sealed record ReflowPage(int Number, double Width, double Height, IReadOnlyList<ReflowGlyph> Glyphs);

/// <summary>Lesezeichen des PDF: Titel, Zielseite, Ebene (0 = oberste).</summary>
public sealed record ReflowOutline(string Title, int Page, int Level);

/// <summary>Ein erkanntes Diagramm (<c>EbookDiagram</c>) mit Lage als Anteil der Seite; <see cref="Key"/> = Index des Aufrufers.</summary>
public sealed record ReflowDiagram(int Key, int Page, double X, double Y, double W, double H, string Fen, string? Label, bool SideKnown);

/// <summary>Ein Stück Text mit einheitlicher Auszeichnung.</summary>
public sealed record ReflowRun(string Text, bool Bold, bool Italic);

/// <summary>Block des Buchs: Absatz/Überschrift oder Diagramm. <see cref="Page"/> = PDF-Seite, auf der er beginnt.</summary>
public abstract record ReflowBlock(int Page);

/// <summary>Absatz (<see cref="Level"/> 0) oder Überschrift (1–3 = h1–h3).</summary>
public sealed record ReflowParagraph(int Page, int Level, List<ReflowRun> Runs) : ReflowBlock(Page)
{
    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>Diagramm an seiner Stelle im Text — wird zum Brettbild aus der FEN.</summary>
public sealed record ReflowFigure(int Page, ReflowDiagram Diagram) : ReflowBlock(Page);

/// <summary>Ein Kapitel (eine XHTML-Datei im EPUB). <see cref="Part"/> &gt; 1: Fortsetzung eines langen Kapitels.</summary>
public sealed class ReflowChapter
{
    public string Title { get; set; } = string.Empty;
    public int Part { get; set; } = 1;
    public List<ReflowBlock> Blocks { get; } = new();
    /// <summary>Unterpunkte des Inhaltsverzeichnisses (Lesezeichen der Ebene darunter): Titel + Zielseite.</summary>
    public List<(string Title, int Page)> Sections { get; } = new();
}

/// <summary>Das fertige Buch vor dem Schreiben.</summary>
public sealed record ReflowBook(string Title, string Author, string Language, string Identifier, List<ReflowChapter> Chapters);

/// <summary>Ergebnis einer Umwandlung: Kennzahlen + je Diagramm (Key) der Bildpfad im EPUB und das Kapitel (Spine, ab 1).</summary>
public sealed record ReflowResult(int Pages, int Chapters, int Paragraphs, int TextChars, IReadOnlyDictionary<int, (string ImageRef, int Spine)> Images);
