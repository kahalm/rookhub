using System.Text.Json;
using System.Text.Json.Serialization;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Das feste Austauschformat einer Trainingsstunde (<c>schema: 1</c>, oder <c>2</c> mit den optionalen Feldern
/// <c>video.originalFile</c>, <c>moveKind</c>, <c>quoteFrom</c>/<c>quoteTo</c>, <c>analysisPgn</c>; eine Datei je Datum). Unbekannte Felder werden
/// ignoriert (System.Text.Json-Vorgabe), Zeiten sind <c>h:mm:ss</c>-Zeichenketten.
/// </summary>
public sealed record TrainingLessonFile
{
    public int Schema { get; init; }
    public string? Date { get; init; }
    public TrainingVideoRef? Video { get; init; }
    public string? Summary { get; init; }
    public List<TrainingGameFile>? Games { get; init; }
    public List<TrainingSegmentFile>? Segments { get; init; }
    public List<TrainingNoteFile>? Notes { get; init; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

public sealed record TrainingVideoRef
{
    public string? File { get; init; }
    public string? OriginalFile { get; init; }
    public double? DurationSec { get; init; }
}

public sealed record TrainingGameFile
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? White { get; init; }
    public string? Black { get; init; }
    public string? Event { get; init; }
    public int? Year { get; init; }
    public string? Eco { get; init; }
    public string? Result { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Pgn { get; init; }
    public string? AnalysisPgn { get; init; }
    public int? RookhubGameId { get; init; }
    public string? MatchConfidence { get; init; }
}

public sealed record TrainingSegmentFile
{
    public string? T { get; init; }
    public string? TEnd { get; init; }
    public string? GameId { get; init; }
    public string? Fen { get; init; }
    public string? Move { get; init; }
    public string? MoveKind { get; init; }
    public string? QuoteFrom { get; init; }
    public string? QuoteTo { get; init; }
    public string? Comment { get; init; }
    public string? Quote { get; init; }
    public string? Variations { get; init; }
    public List<string>? Tags { get; init; }
    public TrainingQuestionFile? Question { get; init; }
}

public sealed record TrainingQuestionFile
{
    public string? Prompt { get; init; }
    public string? Answer { get; init; }
    public string? AnswerLine { get; init; }
    public string? AnswerT { get; init; }
    public string? Explanation { get; init; }
}

public sealed record TrainingNoteFile
{
    public string? T { get; init; }
    public string? GameId { get; init; }
    public string? Category { get; init; }
    public string? Text { get; init; }
    public string? Quote { get; init; }
}
