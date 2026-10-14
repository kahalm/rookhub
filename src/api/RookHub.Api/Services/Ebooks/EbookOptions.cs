namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Konfiguration der E-Book-Bibliothek (Abschnitt <c>Ebooks</c>, ENV <c>Ebooks__Root</c> usw.).
///
/// <para><b>Zwei Wurzeln:</b> <see cref="Root"/> ist das beschreibbare Verzeichnis für Uploads und Titelbilder
/// (ein Volume des API-Containers); <see cref="LibraryRoot"/> die vorhandene Bibliothek auf dem Server, nur lesend
/// eingehängt — ihre Dateien werden beim Einlesen NICHT kopiert (28 GB), sondern an Ort und Stelle gelesen.
/// Ohne <see cref="Root"/> ist Hochladen aus, ohne <see cref="LibraryRoot"/> das Einlesen; die Übersicht und der
/// Leser laufen trotzdem.</para>
/// </summary>
public class EbookOptions
{
    public const string Section = "Ebooks";

    /// <summary>Beschreibbares Verzeichnis für Uploads (<c>files/</c>) und Titelbilder (<c>covers/</c>); leer = Hochladen aus.</summary>
    public string? Root { get; set; }

    /// <summary>Die Bibliothek auf dem Server (nur lesend); leer = kein Einlesen möglich.</summary>
    public string? LibraryRoot { get; set; }

    /// <summary>Größte Datei, die ein Upload haben darf (Vorgabe 1 GiB; die Anfrage-Grenze liegt bei
    /// <see cref="EbookService.MaxUploadRequestBytes"/>).</summary>
    public long MaxFileBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    /// <summary>Summe der eigenen Uploads je Nutzer (Vorgabe 5 GiB); 0 = unbegrenzt. Gilt nicht für Admins.</summary>
    public long UserQuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>Werkzeuge fürs Titelbild (poppler-utils, djvulibre-bin im API-Image).</summary>
    public string PdftoppmPath { get; set; } = "pdftoppm";
    public string PdfinfoPath { get; set; } = "pdfinfo";
    public string DdjvuPath { get; set; } = "ddjvu";

    /// <summary>Stockfish-Prüfung der Lösungen + Engine-Lösungen (0.772.0); <c>false</c> schaltet den Dienst ab.</summary>
    public bool EngineCheck { get; set; } = true;

    /// <summary>Bedenkzeit je Stellung in ms (Prüfung); Engine-Lösungen bekommen das Dreifache.</summary>
    public int EngineMoveTimeMs { get; set; } = 400;

    /// <summary>Pfad zu Stockfish; leer = wie der Tipp-Generator (<c>/usr/games/stockfish</c> aus dem Image).</summary>
    public string? StockfishPath { get; set; }

    /// <summary>Diagramme aus Scans erkennen (0.776.0): PDFs ohne Schach-Font werden seitenweise gerendert und nach Brettern
    /// durchsucht; <c>false</c> schaltet das ab (die Bücher bleiben dann „Unsupported“).</summary>
    public bool ScanDiagrams { get; set; } = true;

    /// <summary>Hochladen OHNE Anmeldung (0.778.0): erlaubt, eigenes Kontingent je anonymer Kennung, Gesamtdeckel über alle
    /// anonymen Uploads, nie übernommene Bücher werden nach <see cref="AnonymousRetentionDays"/> gelöscht.</summary>
    public bool AnonymousUploads { get; set; } = true;
    public long AnonymousQuotaBytes { get; set; } = 300L * 1024 * 1024;
    public long AnonymousTotalBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public int AnonymousRetentionDays { get; set; } = 30;

    /// <summary>Titelbilder je Worker-Durchlauf (alle 20 s) — klein, damit ein Einlesen von 800 Büchern die API nicht lahmlegt.</summary>
    public int CoverBatchSize { get; set; } = 4;

    /// <summary>Zeitdeckel je Werkzeug-Aufruf (ein 1,4-GB-PDF braucht für Seite 1 trotzdem nur Sekunden).</summary>
    public int CoverTimeoutSeconds { get; set; } = 90;
}
