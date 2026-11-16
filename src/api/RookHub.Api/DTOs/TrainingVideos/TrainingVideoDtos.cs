namespace RookHub.Api.DTOs.TrainingVideos;

/// <summary>Eine Partie der Stunde (Liste und Stundenseite).</summary>
public record TrainingGameDto(string Key, string Kind, string? White, string? Black, string? Event, int? Year, string? Eco,
    string? Result, int? FromSec, int? ToSec, int? RookhubGameId, string? MatchConfidence, bool HasPgn);

/// <summary>Eine Stunde in der Übersicht. <c>OwnerName</c> nur für Admins (sie sehen die Stunden aller Konten).</summary>
public record TrainingLessonListItemDto(int Id, string Date, string? Summary, int? DurationSec, bool HasVideo,
    int SegmentCount, List<TrainingGameDto> Games, string? OwnerName);

/// <summary>Ein Segment der Stunde. <c>Progress</c> = eigener Stand im Nachspielen (null = noch nie gespielt).
/// <c>TaskFen</c>/<c>TaskMove</c> = die Aufgabe im Nachspielen: Stellung VOR dem gesuchten Zug und der Zug (SAN, legal aus
/// <c>TaskFen</c>); beide <c>null</c> = keine Aufgabe, nur ansehen. <c>TaskFen</c> kann von <c>Fen</c> abweichen, wenn die
/// Aufbereitung die Stellung NACH dem Zug lieferte.</summary>
public record TrainingSegmentDto(int Id, int StartSec, int? EndSec, string? GameKey, string? Fen, string? Move,
    string? MoveKind, string? Comment, string? Quote, string? Variations, List<string> Tags, bool HasQuestion,
    int? QuoteStartSec, int? QuoteEndSec, TrainingQuestionDto? Question, TrainingProgressDto? Progress,
    string? TaskFen = null, string? TaskMove = null);

public record TrainingQuestionDto(string Prompt, string? Answer, string? AnswerLine, int? AnswerSec, string? Explanation);

/// <summary><c>Status</c>: <c>solved</c> | <c>wrong</c> | <c>skipped</c>.</summary>
public record TrainingProgressDto(string Status, int Attempts, int Plays, DateTime UpdatedAt);

public record TrainingProgressUpdateDto(string Status, int Attempts);

/// <summary>Eine Aufgabe des stundenübergreifenden Fragen-Satzes.</summary>
public record TrainingDrillItemDto(int LessonId, string Date, bool HasVideo, string? GameLabel, int? RookhubGameId,
    TrainingSegmentDto Segment);

/// <summary>Die Stundenseite: Kopf, Partien (mit PGN), Segmente in Reihenfolge.</summary>
public record TrainingLessonDto(int Id, string Date, string? Summary, int? DurationSec, bool HasVideo, string? OwnerName,
    List<TrainingGameDto> Games, List<TrainingSegmentDto> Segments, Dictionary<string, string> Pgns,
    Dictionary<string, string> AnalysisPgns);

/// <summary>Treffer der Stellungs- oder Volltextsuche. <c>Kind</c>: <c>segment</c> oder <c>note</c>;
/// <c>SameSide</c> = auch das Zugrecht stimmt (nur Stellungssuche).</summary>
public record TrainingSearchHitDto(string Kind, int LessonId, string Date, int? SegmentId, int? StartSec, string? Fen,
    string? Move, string? Text, string? GameLabel, string? Category, bool? SameSide);

public record TrainingSearchResultDto(List<TrainingSearchHitDto> Hits, bool Truncated);

/// <summary>Ein Trainerkommentar an einer RookHub-Partie. <c>FenIndex</c> = Stellung in der Partie (0 = Anfangsstellung,
/// n = nach dem n-ten Halbzug); <c>MatchedBy</c>: <c>move</c> (der genannte Zug ist der Partiezug, der zu dieser Stellung
/// führt), <c>analysis</c> (ein Zug der Besprechung aus dieser Stellung, als Variante), <c>position</c> (nur die
/// Stellung stimmt) oder <c>variation</c> (die Stellung kommt in der Partie nicht vor — eine Variante, angehängt an die
/// zuletzt besprochene Partiestellung; <c>Fen</c> zeigt sie).</summary>
public record TrainingGameCommentDto(int FenIndex, string MatchedBy, int LessonId, string Date, int SegmentId, int StartSec,
    int? EndSec, string? Fen, string? Move, string? MoveKind, string? Comment, string? Variations, List<string> Tags, bool HasVideo);

public record TrainingGameCommentsDto(List<TrainingGameCommentDto> Items, int Unmatched);

public record TrainingAccessTokenDto(string Token, DateTime ExpiresAt);

/// <summary>Bericht eines Imports (auch im Probelauf).</summary>
public record TrainingImportReportDto(string? Date, bool DryRun, bool Ok, bool Replaced, int? LessonId, int Games,
    int Segments, int Questions, int Notes, int InvalidFens, bool VideoFound, List<string> Warnings, List<string> Errors);

public record TrainingFolderImportDto(bool DryRun, string Owner, List<TrainingImportReportDto> Files);
