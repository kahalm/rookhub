namespace RookHub.Api.DTOs;

/// <summary>Ein Ast des Eroeffnungsbaums: die Stellung und was von hier aus gespielt wird.</summary>
public class OpeningTreeDto
{
    /// <summary>Die Halbzuege bis hierhin, normalisiert („e4 e5 Nf3").</summary>
    public string Line { get; set; } = string.Empty;

    /// <summary>Wurde nur unter den schon gerechneten Partien gezaehlt?</summary>
    public bool OnlyPlayable { get; set; }

    /// <summary>Wie viele Partien diese Stellung ueberhaupt erreichen.</summary>
    public int Total { get; set; }

    /// <summary>Die Fortsetzungen, haeufigste zuerst.</summary>
    public List<OpeningMoveDto> Moves { get; set; } = [];
}

public class OpeningMoveDto
{
    public string San { get; set; } = string.Empty;
    public int Games { get; set; }
}
