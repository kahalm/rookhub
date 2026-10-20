namespace RookHub.Api.DTOs;

/// <summary>Ein Hintergrunddienst in der Admin-Übersicht „Hintergrundjobs".</summary>
/// <param name="Name">Klassenname des Dienstes (z. B. <c>AnalysisJobWorker</c>).</param>
/// <param name="Pausable">Hält er bei „Hintergrundjobs pausieren" an?</param>
/// <param name="LastTick">Ende seines letzten erfolgreichen Takts (UTC), <c>null</c> wenn unbekannt/noch keiner.</param>
public record BackgroundJobServiceDto(string Name, bool Pausable, DateTime? LastTick);

/// <summary>Antwort von <c>GET/PUT /api/admin/background-jobs/pause</c>.</summary>
/// <param name="Paused">Wirksame Pause (Datenbank ODER Konfiguration).</param>
/// <param name="Since">Seit wann per Admin-Schalter pausiert (UTC), sonst <c>null</c>.</param>
/// <param name="By">Wer pausiert hat (Anzeigename), sonst <c>null</c>.</param>
/// <param name="ForcedByConfig"><c>BackgroundJobs:Paused=true</c> in der Konfiguration — der Schalter kann die Pause
/// dann nicht aufheben.</param>
/// <param name="PausedInDb">Stand des Admin-Schalters selbst (ohne Konfiguration).</param>
public record BackgroundJobsPauseDto(bool Paused, DateTime? Since, string? By, bool ForcedByConfig, bool PausedInDb,
    IReadOnlyList<BackgroundJobServiceDto> Services);

/// <summary>Rumpf von <c>PUT /api/admin/background-jobs/pause</c>.</summary>
public record SetBackgroundJobsPauseDto(bool Paused);
