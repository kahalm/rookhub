using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Wo eine in einer Nebenvariante markierte Stellung hängt (0.848.0, Wunsch 2026-10-26: „nicht jede interessante Stellung muss
/// in der Partie vorkommen, kann auch in einer Variation markiert werden"): Abzweig-Halbzug der Partie + die Züge der Variante
/// bis zur Stellung. Damit kann die Partie eine Sprungmarke an den Abzweig setzen und die Variante nachspielen.
/// </summary>
public static partial class MarkedPositionVariation
{
    /// <summary>Höchstens so lang (≈ 160 Halbzüge) — eine eigene Variante ist kurz.</summary>
    public const int MaxUciLength = 1000;

    [GeneratedRegex("^[a-h][1-8][a-h][1-8][qrbn]?( [a-h][1-8][a-h][1-8][qrbn]?)*$")]
    private static partial Regex UciLine();

    /// <summary>Bereinigt die Angaben des Aufrufers: ohne gültigen Abzweig oder ohne gültige Zugfolge fällt BEIDES weg.</summary>
    public static (int? FromPly, string? Uci) Clean(int? fromPly, string? uci)
    {
        var line = uci?.Trim().ToLowerInvariant();
        if (fromPly is not (>= 0 and < GameMistakeProgressService.MaxPly) || string.IsNullOrEmpty(line)
            || line.Length > MaxUciLength || !UciLine().IsMatch(line)) return (null, null);
        return (fromPly, line);
    }
}
