namespace RookHub.Api.DTOs.Puzzles;

/// <summary>Empfohlene Übungen einer Partie (0.960.0, <c>GET …/summary/exercises</c>): je Stelle der Partie eine Gruppe.</summary>
public sealed record GameExercisesDto(List<GameExerciseGroupDto> Groups);

/// <summary>
/// Eine Stelle der Partie mit ihren Aufgaben. <c>Ply</c> = Stellung VOR dem Halbzug (0-basiert, wie die Analyse); <c>White</c> = die
/// Seite, die das Motiv hatte. <c>Reason</c> ist der letzte Teil des i18n-Schlüssels <c>games.summary.exercises.reason.*</c>
/// (<c>missedMate</c>, <c>playedMate</c>, seit 0.961.0 <c>missedTactic</c>, <c>allowedTactic</c>), <c>MateIn</c> bzw. <c>Theme</c>
/// (Lichess-Thema wie <c>fork</c>) seine Parameter. <c>AlsoPlies</c>: weitere Stellen mit demselben Bild.
/// </summary>
public sealed record GameExerciseGroupDto(int Ply, bool White, string Reason, int? MateIn, List<int> AlsoPlies, List<GameExerciseDto> Exercises,
    string? Theme = null);

/// <summary>Eine Aufgabe im Format des Lösers. <c>Source = lichess</c>: <c>Fen</c> VOR dem Zug des Gegners, <c>Moves</c> = Gegnerzug, dann
/// abwechselnd Löser/Gegner (UCI). <c>Source = book</c> (0.962.0): <c>Id</c> = Diagramm, <c>Fen</c> = Diagrammstellung (Löser am Zug),
/// <c>Moves</c> = Buchlösung ab dem Löser (leer, wenn <see cref="GameExerciseBookDto.Solvable"/> falsch ist — dann nur „Beispiel im
/// Buch“), <c>Rating</c> 0. <c>Match</c>: <c>strict</c> (gleiches Mattbild), <c>family</c> (gleiche Familie), <c>theme</c> (nur gleiches Thema).</summary>
public sealed record GameExerciseDto(int Id, string Fen, List<string> Moves, int Rating, List<string> Themes, string Match,
    string Source = "lichess", GameExerciseBookDto? Book = null);

/// <summary>Quelle eines Buchbeispiels (0.962.0): Buch, Autor, Kapitel aus dem Inhaltsverzeichnis (sonst <c>null</c>), Seite des PDFs
/// bzw. Kapitel des EPUBs (<c>PdfPage</c>), Aufgabennummer im Buch; <c>Solvable</c> = Buchlösung von Stockfish bestätigt.</summary>
public sealed record GameExerciseBookDto(int EbookId, string Title, string? Author, string? Chapter, int Page, bool PdfPage, string? Label,
    bool Solvable);
