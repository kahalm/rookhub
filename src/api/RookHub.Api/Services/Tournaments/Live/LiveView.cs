using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// Welche Bretter ein Zuschauer gerade SIEHT (1.027.0, Wunsch 2026-11-26: „nur die Bretter rechnen, die jemand ansieht“) —
/// dieselbe Regel wie die Seite (<c>live-boards.component.ts</c>): <c>team</c> = die Begegnung dieser Mannschaft,
/// <c>player</c> = das Brett dieses Spielers (Einzelturnier), dazu die hinzugefügten Bretter (<c>add</c>). Ohne Filter alle
/// Bretter der Runde (<c>null</c>). Fremde Bretter (<c>ext</c>) filtert schon ihr Abo.
/// </summary>
public static partial class LiveView
{
    /// <summary>So viele Bretter derselben Runde nimmt <c>?add=</c> höchstens (wie <c>LIVE_MAX_ADDED</c> im Frontend).</summary>
    public const int MaxAdded = 24;

    /// <summary><c>?add=</c>: Begegnung.Brett (<c>2.3</c>), Brett (<c>7</c>) oder Lichess-Kennung; gültig, verschieden, gedeckelt.</summary>
    public static List<string> ParseAdd(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => AddKeyRe().IsMatch(x)).Distinct(StringComparer.Ordinal).Take(MaxAdded).ToList();

    [GeneratedRegex(@"^(\d{1,3}(\.\d{1,3})?|[A-Za-z0-9]{8})$")]
    private static partial Regex AddKeyRe();

    /// <summary>Partie-Kennungen der sichtbaren Bretter; <c>null</c> = ohne Filter (alle).</summary>
    public static IReadOnlySet<string>? Visible(LiveRoundLayout layout, string? team, string? player, IReadOnlyList<string> add)
    {
        team = string.IsNullOrWhiteSpace(team) ? null : team.Trim();
        player = string.IsNullOrWhiteSpace(player) ? null : player.Trim();
        if (team is null && player is null) return null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (layout.Team)
        {
            foreach (var m in layout.Matches.Where(m => team is not null && (m.Home == team || m.Away == team)))
                foreach (var b in m.Boards) if (b.GameId is { } g) ids.Add(g);
        }
        else if (player is not null)
        {
            var key = LiveBoardAssembler.NameKey(player);
            foreach (var b in layout.Boards.Where(b => LiveBoardAssembler.NameKey(b.White) == key || LiveBoardAssembler.NameKey(b.Black) == key))
                if (b.GameId is { } g) ids.Add(g);
        }
        foreach (var k in add)
        {
            var parts = k.Split('.');
            string? g = null;
            if (parts.Length == 2 && layout.Team && int.TryParse(parts[0], out var mn) && int.TryParse(parts[1], out var bn))
                g = layout.Matches.FirstOrDefault(m => m.MatchNumber == mn)?.Boards.FirstOrDefault(b => b.Board == bn)?.GameId;
            else if (parts.Length == 1 && !layout.Team && k.Length <= 3 && int.TryParse(k, out var sb))
                g = layout.Boards.FirstOrDefault(b => b.Board == sb)?.GameId;
            else if (k.Length == 8 && layout.Unassigned.Contains(k))
                g = k;
            if (g is not null) ids.Add(g);
        }
        return ids;
    }
}
