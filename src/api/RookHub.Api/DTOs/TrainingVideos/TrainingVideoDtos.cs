namespace RookHub.Api.DTOs.TrainingVideos;

/// <summary>Eine Partie der Stunde (Liste und Stundenseite).</summary>
public record TrainingGameDto(string Key, string Kind, string? White, string? Black, string? Event, int? Year, string? Eco,
    string? Result, int? FromSec, int? ToSec, int? RookhubGameId, string? MatchConfidence, bool HasPgn);

/// <summary>Ein Eintrag in der Übersicht (ein Teil einer Stunde). <c>Part</c>: <c>private</c> (eigene Partien) |
/// <c>general</c> (Lehrbeispiele/Übungen). <c>OwnerName</c> nur für Admins (sie sehen die Stunden aller Konten).</summary>
public record TrainingLessonListItemDto(int Id, string Date, string? Summary, int? DurationSec, bool HasVideo,
    int SegmentCount, List<TrainingGameDto> Games, string? OwnerName, string Part = "private");

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
    TrainingSegmentDto Segment, string Part = "private");

/// <summary>Die Stundenseite: Kopf, Partien (mit PGN), Segmente in Reihenfolge. <c>Part</c> = dieser Teil der Stunde;
/// <c>OtherParts</c> = die übrigen Teile desselben Datums (für „zum anderen Teil“).</summary>
public record TrainingLessonDto(int Id, string Date, string? Summary, int? DurationSec, bool HasVideo, string? OwnerName,
    List<TrainingGameDto> Games, List<TrainingSegmentDto> Segments, Dictionary<string, string> Pgns,
    Dictionary<string, string> AnalysisPgns, string Part = "private", List<TrainingLessonPartRefDto>? OtherParts = null);

/// <summary>Verweis auf einen anderen Teil derselben Stunde.</summary>
public record TrainingLessonPartRefDto(int Id, string Part);

/// <summary>Treffer der Stellungs- oder Volltextsuche. <c>Kind</c>: <c>segment</c> oder <c>note</c>;
/// <c>SameSide</c> = auch das Zugrecht stimmt (nur Stellungssuche); <c>NoteId</c> = die Notiz (nur <c>note</c>, damit das
/// Notizbuch die Suche wiederverwenden kann).</summary>
public record TrainingSearchHitDto(string Kind, int LessonId, string Date, int? SegmentId, int? StartSec, string? Fen,
    string? Move, string? Text, string? GameLabel, string? Category, bool? SameSide, int? NoteId = null, string Part = "private");

public record TrainingSearchResultDto(List<TrainingSearchHitDto> Hits, bool Truncated);

/// <summary>Ein Trainerkommentar an einer RookHub-Partie. <c>FenIndex</c> = Stellung in der Partie (0 = Anfangsstellung,
/// n = nach dem n-ten Halbzug); <c>MatchedBy</c>: <c>move</c> (der genannte Zug ist der Partiezug, der zu dieser Stellung
/// führt), <c>analysis</c> (ein Zug der Besprechung aus dieser Stellung, als Variante), <c>position</c> (nur die
/// Stellung stimmt) oder <c>variation</c> (die Stellung kommt in der Partie nicht vor — eine Variante, angehängt an die
/// zuletzt besprochene Partiestellung; <c>Fen</c> zeigt sie).</summary>
public record TrainingGameCommentDto(int FenIndex, string MatchedBy, int LessonId, string Date, int SegmentId, int StartSec,
    int? EndSec, string? Fen, string? Move, string? MoveKind, string? Comment, string? Variations, List<string> Tags, bool HasVideo,
    string Part = "private");

public record TrainingGameCommentsDto(List<TrainingGameCommentDto> Items, int Unmatched);

public record TrainingAccessTokenDto(string Token, DateTime ExpiresAt);

/// <summary>Bericht eines Imports (auch im Probelauf). <c>LessonId</c> = der erste Teil (privat vor allgemein);
/// <c>Parts</c> = je Eintrag (Teil) die Zahlen; <c>ProgressMoved</c> = Übungsstand-Zeilen, die zu einem anderen Eintrag
/// umgezogen sind (im Probelauf 0).</summary>
public record TrainingImportReportDto(string? Date, bool DryRun, bool Ok, bool Replaced, int? LessonId, int Games,
    int Segments, int Questions, int Notes, int InvalidFens, bool VideoFound, List<string> Warnings, List<string> Errors,
    List<TrainingImportPartDto> Parts, int ProgressMoved);

/// <summary>Ein Eintrag (Teil) der eingelesenen Stunde: <c>Part</c> <c>private</c> | <c>general</c>; <c>LessonId</c> null
/// = im Probelauf neu; <c>Replaced</c> = eine bestehende Zeile wird weiterverwendet.</summary>
public record TrainingImportPartDto(string Part, int? LessonId, bool Replaced, int Games, int Segments, int Questions, int Notes);

public record TrainingFolderImportDto(bool DryRun, string Owner, List<TrainingImportReportDto> Files);

/// <summary>Eine Notiz im Trainer-Notizbuch (stundenübergreifend). <c>GameKind</c>: <c>own</c> | <c>lesson</c> |
/// <c>exercise</c> der Partie, auf die die Notiz verweist (null = keine Partie). <c>GameLessonId</c> = der Eintrag (Teil
/// der Stunde), in dem diese Partie liegt — Notizen stehen im privaten Teil, ihre Partie kann im allgemeinen liegen.</summary>
public record TrainingNotebookNoteDto(int Id, int LessonId, string Date, int? StartSec, string Category, string Text,
    string? Quote, string? GameKey, string? GameWhite, string? GameBlack, string? GameKind, int? RookhubGameId,
    bool HasVideo, int? LessonDurationSec, int? GameLessonId = null);

/// <summary>„Sagt er immer wieder“: Notizen mit sehr ähnlichem Inhalt aus mindestens zwei Stunden. <c>Lessons</c> =
/// Anzahl verschiedener Stunden, <c>NoteId</c> = die stellvertretende Notiz, <c>NoteIds</c> = alle (neueste zuerst).</summary>
public record TrainingNotebookThemeDto(int Lessons, int NoteId, string Category, List<int> NoteIds);

/// <summary>Treffer im EIGENEN Repertoire zu einer Eröffnungsstellung: die erste Linie (für „im Repertoire öffnen“) und
/// alle Züge, die das Repertoire dort spielt (Hauptzug zuerst). <c>SameSide</c> = auch das Zugrecht stimmt.</summary>
public record TrainingNotebookRepertoireDto(int RepertoireId, string RepertoireName, string Chapter, string LineName,
    int GameIndex, int Ply, List<string> Moves, bool SameSide);

/// <summary>Eine Eröffnungsempfehlung: die Notiz (Kategorie <c>eroeffnung</c>) mit der Stellung des nächstgelegenen
/// Segments derselben Partie (<c>Fen</c> null = keine Stellung) und den Treffern im eigenen Repertoire.</summary>
public record TrainingNotebookOpeningDto(int NoteId, string? Fen, int? SegmentId, string? SegmentMove,
    List<TrainingNotebookRepertoireDto> Repertoire);

/// <summary>Das Trainer-Notizbuch. <c>HasRepertoire</c> = das Konto hat eigene Repertoire-Linien (sonst gibt es keinen
/// Abgleich); <c>RepertoireTruncated</c> = der Abgleich lief ins Zeitbudget.</summary>
public record TrainingNotebookDto(List<TrainingNotebookNoteDto> Notes, List<TrainingNotebookThemeDto> Themes,
    List<TrainingNotebookOpeningDto> Openings, bool HasRepertoire, bool RepertoireTruncated);

/// <summary>Ein aktiver Teilen-Link eines Eintrags (nur für den Besitzer). Geteilt wird <c>/tl/{Token}</c>.</summary>
public record TrainingShareDto(int Id, string Token, DateTime CreatedAt, DateTime ExpiresAt);

/// <summary>Neuer Teilen-Link: <c>Days</c> 1 | 7 | 30 (fehlt: 7).</summary>
public record TrainingShareCreateDto(int? Days);
