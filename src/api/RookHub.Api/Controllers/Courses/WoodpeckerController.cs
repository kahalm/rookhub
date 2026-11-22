using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Controllers.Courses;

/// <summary>
/// Woodpecker-Training über einen oder mehrere Kurse (feste Regeln aus dem Buch, <see cref="WoodpeckerRules"/>).
/// Pläne gehören dem Nutzer; ein fremder Plan ist 404. Fehlerfälle werfen die Dienste als Domänen-Ausnahme.
/// <para>Jeder Endpunkt, der einen Plan zurückgibt, nimmt <c>?utcOffsetMinutes=</c> (Minuten östlich von UTC, wie bei
/// <c>stats</c>): damit zählt <see cref="WoodpeckerPlanDto.TodayAttempted"/> den lokalen Kalendertag des Betrachters
/// (0.993.0); ohne Angabe den UTC-Tag.</para>
/// </summary>
[ApiController]
[Route("api/woodpecker")]
[Authorize]
public class WoodpeckerController : BaseApiController
{
    private readonly WoodpeckerService _service;
    /// <summary>Kurs-Übersetzung der Aufgaben (<c>?lang=</c>), wie im Kurs-Löser. Optional wie dort — ohne ihn bleibt
    /// <c>lang</c> wirkungslos (Original).</summary>
    private readonly CourseCommentLocalizer? _localizer;

    public WoodpeckerController(WoodpeckerService service, CourseCommentLocalizer? localizer = null)
    {
        _service = service;
        _localizer = localizer;
    }

    /// <summary>Eigene Woodpecker-Trainings, neueste zuerst.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.ListAsync(GetUserId(), ct, utcOffsetMinutes));

    /// <summary>Neues Training über die gewählten Kurse; Start optional rückdatiert.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWoodpeckerPlanDto dto, [FromQuery] int utcOffsetMinutes = 0,
        CancellationToken ct = default) =>
        Ok(await _service.CreateAsync(GetUserId(), IsAdmin, dto, ct, utcOffsetMinutes));

    [HttpGet("{planId:int}")]
    public async Task<IActionResult> Get(int planId, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.GetAsync(GetUserId(), planId, ct, utcOffsetMinutes));

    [HttpDelete("{planId:int}")]
    public async Task<IActionResult> Delete(int planId, CancellationToken ct)
    {
        await _service.DeleteAsync(GetUserId(), planId, ct);
        return NoContent();
    }

    /// <summary>Training archivieren (jederzeit; bleibt lesbar, ein neues wird möglich).</summary>
    [HttpPost("{planId:int}/archive")]
    public async Task<IActionResult> Archive(int planId, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.ArchiveAsync(GetUserId(), planId, ct, utcOffsetMinutes));

    /// <summary>Archiviertes Training wiederherstellen (nur ohne anderes aktives Training, sonst 409).</summary>
    [HttpPost("{planId:int}/unarchive")]
    public async Task<IActionResult> Unarchive(int planId, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.UnarchiveAsync(GetUserId(), planId, ct, utcOffsetMinutes));

    /// <summary>Beginn des laufenden Zyklus verschieben (rückdatieren).</summary>
    [HttpPut("{planId:int}/start")]
    public async Task<IActionResult> UpdateStart(int planId, [FromBody] UpdateWoodpeckerStartDto dto,
        [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.UpdateStartAsync(GetUserId(), planId, dto.StartedAt, ct, utcOffsetMinutes));

    /// <summary>Zyklus 1 vor der Frist beenden (der Satz steht dann fest).</summary>
    [HttpPost("{planId:int}/finish-set")]
    public async Task<IActionResult> FinishSet(int planId, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.FinishSetAsync(GetUserId(), planId, ct, utcOffsetMinutes));

    /// <summary>Laufenden Zyklus verlängern (1.007.0; vor allem nach Ablauf der Frist, wenn nicht alles bearbeitet ist).</summary>
    [HttpPost("{planId:int}/extend")]
    public async Task<IActionResult> Extend(int planId, [FromBody] ExtendWoodpeckerCycleDto dto,
        [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.ExtendCycleAsync(GetUserId(), planId, dto.Days, ct, utcOffsetMinutes));

    /// <summary>Laufenden Zyklus abschließen (1.007.0): ab Zyklus 2 erst nach der Frist, Offenes bleibt unbearbeitet.</summary>
    [HttpPost("{planId:int}/finish-cycle")]
    public async Task<IActionResult> FinishCycle(int planId, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.FinishCycleAsync(GetUserId(), planId, ct, utcOffsetMinutes));

    /// <summary>Nächsten Zyklus beginnen (frühestens einen Tag nach dem Ende des vorigen).</summary>
    [HttpPost("{planId:int}/next-cycle")]
    public async Task<IActionResult> StartNextCycle(int planId, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.StartNextCycleAsync(GetUserId(), planId, ct, utcOffsetMinutes));

    /// <summary>Ausführliche Statistik: Vergleich auf denselben Aufgaben mit jedem früheren Zyklus, Wechsel je Aufgabe,
    /// Hochrechnung, Verlauf, Tipps, Sorgenkinder (0.957.0). <c>?utcOffsetMinutes=</c> = Zeitzone der Tageszeilen.</summary>
    [HttpGet("{planId:int}/stats")]
    public async Task<IActionResult> Stats(int planId, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.GetStatsAsync(GetUserId(), planId, utcOffsetMinutes, ct));

    /// <summary>Nächste Aufgabe: Zyklus 1 in Kursreihenfolge, danach zufällig aus dem Satz. <c>?lang=</c> liefert die
    /// Kommentare übersetzt, wo der Kurs aktuelle Übersetzungen hat (seit 0.882.0 läuft Woodpecker im Kurs-Löser).</summary>
    [HttpGet("{planId:int}/next")]
    public async Task<IActionResult> Next(int planId, [FromQuery] string? lang = null, [FromQuery] int utcOffsetMinutes = 0,
        CancellationToken ct = default)
    {
        var next = await _service.GetNextAsync(GetUserId(), planId, ct, IsAdmin, utcOffsetMinutes);
        if (_localizer is not null) await _localizer.ApplyAsync(next.Puzzle, lang, ct);
        return Ok(next);
    }

    /// <summary>Versuch an einer Aufgabe im laufenden Zyklus (je Zyklus zählt der erste).</summary>
    [HttpPost("{planId:int}/attempts")]
    public async Task<IActionResult> RecordAttempt(int planId, [FromBody] RecordWoodpeckerAttemptDto dto,
        [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default) =>
        Ok(await _service.RecordAttemptAsync(GetUserId(), planId, dto, ct, IsAdmin, utcOffsetMinutes));
}
