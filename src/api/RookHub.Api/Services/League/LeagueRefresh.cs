namespace RookHub.Api.Services.League;

/// <summary>
/// Ein Aktualisierungs-Lauf: laufende Saison von chess-results neu holen (über den Crawler),
/// Partien der wahrscheinlichen Gegner nachladen, Ansichten neu rechnen.
/// </summary>
public sealed class LeagueRefresh
{
    private readonly LeagueService _league;
    private readonly ILogger<LeagueRefresh> _log;

    public LeagueRefresh(LeagueService league, ILogger<LeagueRefresh> log)
    {
        _league = league; _log = log;
    }

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var n = await _league.RebuildViewsAsync(ct);
        return $"{n} Ligen neu gerechnet";
    }
}
