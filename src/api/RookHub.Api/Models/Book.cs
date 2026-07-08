using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Ein importiertes Puzzle-Buch (aus einer PGN-Datei). Gruppiert mehrere
/// <see cref="BookPuzzle"/> und legt fest, in welchen Pools (Daily/Random/Blind)
/// die Puzzles dieses Buchs ausgewählt werden dürfen.
/// </summary>
public class Book
{
    public int Id { get; set; }

    /// <summary>Eindeutiger Dateiname der Quelle, z. B. "1001 Deadly Checkmates.pgn".</summary>
    [Required, MaxLength(200)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Besitzer eines persönlichen Buchs (z. B. ein selbst aus Chessable importierter Kurs).
    /// <c>null</c> = globales/Admin-Buch (klassisches Verhalten, Sichtbarkeit via Gruppen).
    /// Ist es gesetzt, sieht NUR dieser User das Buch als Kurs (zusätzlich zu Admins).
    /// </summary>
    public int? OwnerUserId { get; set; }

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Difficulty { get; set; }

    /// <summary>Schwierigkeit 1–10 (wie in der schach-bot books.json), optional.</summary>
    public int? Rating { get; set; }

    /// <summary>Empfohlene Elo-Spanne (von/bis) für die Puzzles dieses Buchs, optional.</summary>
    public int? MinElo { get; set; }
    public int? MaxElo { get; set; }

    [MaxLength(200)]
    public string? Tags { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Art des Buchs fürs Trainingsziel-Routing: Puzzle-Buch → Kurszeit zählt in die
    /// Kategorie „Puzzles"; Studienbuch → Kategorie „Buch/Kurs". Default Puzzle (klassisches Verhalten).</summary>
    public BookKind Kind { get; set; } = BookKind.Puzzle;

    /// <summary>
    /// Themen-Tags dieses Buchs (CSV der Theme-Keys „opening/middlegame/endgame/tactics/other",
    /// z. B. "tactics" oder "tactics,endgame"). Steuern die Themen-Aufschlüsselung der Kurszeit im
    /// Trainingsziele-Tracker: die Zeit eines Kurs-Versuchs wird gleichmäßig auf die gesetzten Themen
    /// aufgeteilt. <c>null</c>/leer = Default „Taktik" (jedes Buch ist standardmäßig Taktik).
    /// Bearbeitbar vom Admin (alle Bücher) bzw. dem Besitzer eines persönlichen Kurses.
    /// </summary>
    [MaxLength(100)]
    public string? Themes { get; set; }

    /// <summary>Für das deterministische Tagespuzzle nutzbar.</summary>
    public bool ForDaily { get; set; }

    /// <summary>Für /randompuzzle nutzbar.</summary>
    public bool ForRandom { get; set; }

    /// <summary>Für /blindpuzzle nutzbar.</summary>
    public bool ForBlind { get; set; }

    /// <summary>
    /// Öffentlich = ohne Registrierung als Kurs nutzbar. Ist es gesetzt, darf JEDER (auch anonym,
    /// ohne Login) diesen Kurs über den Direkt-Link öffnen und durchspielen; der Fortschritt eines
    /// anonymen Besuchers wird nur lokal im Browser gehalten (zählt nicht in Bestenlisten/Ziele).
    /// Vom Admin je Buch im Bücher-/Kurs-Tab umschaltbar. Default false (klassisches Verhalten:
    /// Sichtbarkeit nur via Gruppen/Besitzer/Teilen).
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Roh-PGN, aus dem dieses Buch importiert wurde (LONGTEXT, nullable). Quelle fürs
    /// verlustfreie Neu-Aufbereiten (Reprocessing), wenn die Import-Pipeline weiterentwickelt
    /// wurde — z. B. um nachträglich Pro-Zug-Kommentare zu extrahieren. <c>null</c> bei
    /// Altbestand (vor Pipeline-Version 1) und bei reinen JSON-Bulk-Importen (kein PGN).
    /// </summary>
    public string? SourcePgn { get; set; }

    /// <summary>
    /// Version der Import-Pipeline (<see cref="Services.ImportPipeline"/>), mit der die Puzzles
    /// dieses Buchs zuletzt aufbereitet wurden. <c>&lt; CurrentVersion</c> ⇒ „veraltet", über den
    /// Reprocess-Knopf neu aufbereitbar. Default 0 = Altbestand.
    /// </summary>
    public int ImportVersion { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<BookPuzzle> Puzzles { get; set; } = new();
}
