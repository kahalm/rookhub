namespace RookHub.Api.DTOs;

/// <summary>„Züge vergleichen" anlegen: Stellung + 2–4 Kandidatenzüge (UCI, Rochade auch als König-schlägt-Turm).</summary>
public class CreateMoveComparisonRequest
{
    public string? Fen { get; set; }
    public List<string>? Moves { get; set; }
    /// <summary>Zieltiefe; fehlt sie, gilt <c>MoveComparisonService.DefaultDepth</c>.</summary>
    public int? Depth { get; set; }
    /// <summary>Sprache der Begründungen (Oberfläche).</summary>
    public string? Lang { get; set; }
    public string? Title { get; set; }
}

/// <summary>Kann der Nutzer vergleichen (Engine vorhanden) und wird erklärt (Modell auf eigener Hardware)?</summary>
public record MoveComparisonStatusDto(bool EngineAvailable, bool OwnEngine, bool Explanations, int MaxCandidates,
    int DefaultDepth, int MaxDepth, int OpenComparisons, int MaxOpen);

/// <summary>Eine Antwort des Gegners nach einem Kandidaten (aus dessen eigener Rechnung).</summary>
public record MoveComparisonReplyDto(string Uci, string San, string? EvalText, IReadOnlyList<string> Line);

/// <summary>Eine der besten Antworten auf einen schwächeren Kandidaten, gespielt nach dem BESTEN: geht sie, wie steht
/// es danach, und was antwortet man selbst (<see cref="Line"/> beginnt mit dem eigenen Zug).</summary>
public record MoveComparisonTestDto(string ReplyUci, string ReplySan, string State, int Depth, string? EvalText,
    IReadOnlyList<string> Line);

public record MoveComparisonCandidateDto(
    string Uci, string San, string State, int Depth, string? EvalText, bool IsBest,
    IReadOnlyList<MoveComparisonReplyDto> Replies, IReadOnlyList<MoveComparisonTestDto> Tests, string? Explanation);

/// <summary>Bewertungen immer aus WEISS-Sicht wie auf dem Analysebrett (<c>+0.35</c>, <c>#3</c>, <c>#-2</c>);
/// Kandidaten nach Stärke für die Seite am Zug, der beste zuerst (noch nicht gerechnete hinten).</summary>
public record MoveComparisonDto(
    int Id, string Fen, string? Title, int Depth, string Status, string? Error, bool WhiteToMove, string? BestUci,
    string Language, bool ExplanationsAvailable, int Pending, int Total, DateTime CreatedAt, DateTime? FinishedAt,
    IReadOnlyList<MoveComparisonCandidateDto> Candidates);

public record MoveComparisonSummaryDto(int Id, string Fen, string? Title, string Status, DateTime CreatedAt,
    IReadOnlyList<string> Moves, string? BestSan);
