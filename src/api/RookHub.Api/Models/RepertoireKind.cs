namespace RookHub.Api.Models;

/// <summary>
/// Kategorisierung eines Repertoires. Wird u. a. vom Extension-Endpoint
/// (<c>/api/extension/repertoires?kind=opening</c>) genutzt, damit z. B. die
/// chess.com-Tampermonkey-Erweiterung gezielt nur Eroeffnungs-Repertoires
/// laden kann.
/// </summary>
public enum RepertoireKind
{
    /// <summary>Ungetagged — Default fuer Altbestand.</summary>
    None = 0,
    /// <summary>Eroeffnungs-Repertoire (fuer Deviation-Check etc.).</summary>
    Opening = 1,
    /// <summary>Mittelspiel-Stellungen/Plaene.</summary>
    Middlegame = 2,
    /// <summary>Endspiel-Theorie.</summary>
    Endgame = 3,
}
