namespace RookHub.Api.Models;

/// <summary>
/// „Sag mir per Glocke, wenn diese Partie fertig TIEF analysiert ist" (0.904.0, Wunsch 2026-11-08: „wenn ein registrierter
/// user eine partie hochlädt informier ihn via glocke wenn fertig tief analysiert … ausserdem wenn man bei einer partie
/// analyse klickt"). Eine Zeile je (Analyse bzw. Vereinspartie, Nutzer); beim Ende der Analyse geht die Glocke
/// (<see cref="NotificationType.GameAnalysisFinished"/>) und die Zeile wird gelöscht — sie lebt also nur, solange gerechnet wird.
///
/// <para>Genau EINES der beiden Ziele ist gesetzt: <see cref="GameAnalysisId"/> (die Analyse gibt es schon — „Partie
/// analysieren", Upload einer Partie, deren Analyse schon läuft) oder <see cref="LeagueClubGameId"/> (eine in LeagueHub
/// hochgeladene Vereinspartie: deren Analyse legt erst der Stapel an, <c>MasterAnalysisScheduler</c>). Mehrere Nutzer an
/// einer Analyse sind gewollt — eine Analyse dient über den Zug-Schlüssel mehreren Kopien.</para>
///
/// <para>Auch bei „Schwaz"-Partien (dort steht <see cref="LeagueClubGame.UploadedByUserId"/> bewusst leer): die Zeile ist
/// intern, kein DTO trägt sie, und sie verschwindet mit der Glocke.</para>
/// </summary>
public class GameAnalysisWatcher
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int? GameAnalysisId { get; set; }
    public GameAnalysis? GameAnalysis { get; set; }

    public int? LeagueClubGameId { get; set; }
    public LeagueClubGame? LeagueClubGame { get; set; }

    /// <summary>Ziel der Glocke — die Partie dieses Nutzers (<c>/games/{id}</c>, <c>/g/{token}</c>, <c>/club-games/{id}</c>).</summary>
    public string Link { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
