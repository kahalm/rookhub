namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Versuchszähler je Buch für den eigenen Diagramm-Worker (R3-009): bringt ein Buch den Prozess hart zum Absturz
/// (OOM-Kill mitten in der Erkennung), gibt der Neustart die Leihe zurück und der Worker nähme es endlos wieder. Darum legt
/// der Worker VOR der Erkennung eine Marke an (<c>&lt;Upload-Wurzel&gt;/diagram-attempts/&lt;id&gt;</c>, Inhalt = Zahl der
/// begonnenen Versuche) und löscht sie nach jedem geordneten Ende (Erfolg, Fehler, Abbruch beim Herunterfahren) — nur ein
/// harter Absturz lässt sie stehen. Ab dem <see cref="MaxAttempts"/>. abgebrochenen Versuch gilt das Buch als
/// <c>Failed</c>. Eine Datei statt einer Spalte, weil der Zähler nur Abstürze überdauern muss; ohne Upload-Wurzel zählt
/// nichts.
/// </summary>
public sealed class EbookDiagramAttempts
{
    /// <summary>So viele Versuche, die ohne geordnetes Ende abbrachen, bekommt ein Buch.</summary>
    public const int MaxAttempts = 3;

    private readonly string? _dir;

    public EbookDiagramAttempts(string? uploadRoot) => _dir = uploadRoot == null ? null : Path.Combine(uploadRoot, "diagram-attempts");

    private string? FileOf(int ebookId) => _dir == null ? null : Path.Combine(_dir, ebookId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Wie viele frühere Versuche ohne geordnetes Ende das Buch hatte (0 = keiner).</summary>
    public int Interrupted(int ebookId)
    {
        var f = FileOf(ebookId);
        try { return f != null && File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out var n) ? n : 0; }
        catch (IOException) { return 0; }
    }

    /// <summary>Einen Versuch beginnen: Marke mit Zählerstand schreiben. Gibt <c>false</c> zurück, wenn das Buch schon
    /// <see cref="MaxAttempts"/> abgebrochene Versuche hatte (dann nicht erkennen, sondern als Fehler abschließen).</summary>
    public bool TryBegin(int ebookId)
    {
        var n = Interrupted(ebookId);
        if (n >= MaxAttempts) return false;
        var f = FileOf(ebookId);
        if (f == null) return true;
        try
        {
            Directory.CreateDirectory(_dir!);
            File.WriteAllText(f, (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (IOException) { /* ohne Marke wie bisher */ }
        catch (UnauthorizedAccessException) { /* ohne Marke wie bisher */ }
        return true;
    }

    /// <summary>Geordnetes Ende (Erfolg, Fehler, Abbruch): Marke löschen.</summary>
    public void End(int ebookId)
    {
        var f = FileOf(ebookId);
        if (f == null) return;
        try { File.Delete(f); }
        catch (IOException) { /* egal */ }
        catch (UnauthorizedAccessException) { /* egal */ }
    }
}
