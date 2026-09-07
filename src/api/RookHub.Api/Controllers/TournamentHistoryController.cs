using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Der Turnierverlauf: welche Turniere ein Spieler gespielt hat, welche noch kommen, und in den
/// gespielten Punkte, Platz und Performance-Rating. Umschaltbar auf Freunde.
///
/// <para><b>Sichtbarkeit.</b> Der eigene Verlauf immer; ein fremder nur zwischen ANGENOMMENEN
/// Freunden (sonst 403) — dieselbe Regel wie bei <c>/api/friends/{userId}/stats</c>. Die Daten
/// selbst sind auf chess-results oeffentlich, die VERKNUEPFUNG von Konto und Spielerkennung ist es
/// nicht: <c>PublicProfileDto</c> gibt die ChessResultsId bewusst nicht heraus, und dabei bleibt
/// es.</para>
/// </summary>
[ApiController]
[Route("api/tournament-history")]
[Authorize]
public class TournamentHistoryController : BaseApiController
{
    /// <summary>
    /// Wie viele Konten ein Aufruf umfassen darf. „Alle Freunde" ist der eigentliche Zweck, aber
    /// jedes Konto kostet im schlechtesten Fall einen Seitenabruf fuer seine Trefferliste.
    /// </summary>
    private const int MaxUsers = 20;

    private readonly TournamentHistoryService _history;
    private readonly FriendService _friends;
    private readonly AppDbContext _db;

    public TournamentHistoryController(
        TournamentHistoryService history, FriendService friends, AppDbContext db)
    {
        _history = history;
        _friends = friends;
        _db = db;
    }

    /// <summary>
    /// Der Verlauf. Ohne <c>userIds</c> der eigene; mit, die genannten Konten — der eigene ist
    /// immer erlaubt, fremde muessen angenommene Freunde sein.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PlayerHistoryDto>>> Get(
        [FromQuery] string? userIds = null, CancellationToken ct = default)
    {
        var me = GetUserId();
        if (!TryParseIds(userIds, out var requested))
            return BadRequest(new { message = "userIds must be a comma-separated list of numbers." });

        if (requested.Count == 0) requested = [me];
        if (requested.Count > MaxUsers)
            return BadRequest(new { message = $"At most {MaxUsers} users per request." });

        var others = requested.Where(id => id != me).ToList();
        if (others.Count > 0)
        {
            var allowed = await _friends.GetAcceptedFriendIdsAsync(me, others);
            // Ein einzelnes fremdes Konto abzuweisen waere verwirrend („warum fehlt der?"), und
            // still zu ueberspringen waere eine Luecke ohne Erklaerung. Also 403 mit Grund.
            if (others.Any(id => !allowed.Contains(id)))
                return Forbid();
        }

        var histories = await _history.GetAsync(requested, ct);
        return Ok(histories.Select(PlayerHistoryDto.From).ToList());
    }

    /// <summary>
    /// Die angenommenen Freunde fuer die Umschaltung — ALLE, auch die ohne Namen im Profil.
    ///
    /// <para><b>Warum nicht mehr gefiltert.</b> Bis hierher kamen nur Freunde MIT Namen heraus, mit
    /// der Begruendung, ein Freund ohne Verlauf sei in der Liste nur eine Enttaeuschung. In der
    /// Praxis war das Ergebnis schlimmer: wer Freunde hat, die ihren Namen nicht eingetragen haben,
    /// bekam eine LEERE Auswahl und keinen Grund dafuer — gemeldet als „ich habe Freunde, kann aber
    /// keine auswaehlen". Jetzt stehen sie da und tragen mit, ob etwas zu holen ist
    /// (<c>hasName</c>); die Ansicht zeigt sie ausgegraut mit dem Grund.</para>
    /// </summary>
    [HttpGet("friends")]
    public async Task<ActionResult<List<HistoryFriendDto>>> Friends(CancellationToken ct = default)
    {
        var me = GetUserId();
        var friendIds = (await _friends.GetAcceptedFriendIdsAsync(
            me, await _db.Friendships
                .Where(f => f.RequesterId == me || f.AddresseeId == me)
                .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId)
                .ToListAsync(ct))).ToList();
        if (friendIds.Count == 0) return Ok(new List<HistoryFriendDto>());

        var rows = await _db.UserProfiles.AsNoTracking()
            .Where(p => friendIds.Contains(p.UserId))
            .Select(p => new
            {
                p.UserId, p.LastName, p.FirstName, p.FideId, p.ChessResultsId, p.DisplayName,
                Username = p.User.Username,
            })
            .ToListAsync(ct);

        return Ok(rows
            .Select(r => new
            {
                Row = r,
                Identity = TournamentHistoryService.IdentityOf(r.LastName, r.FirstName, r.FideId, r.ChessResultsId),
            })
            .Select(x => new HistoryFriendDto
            {
                UserId = x.Row.UserId,
                DisplayName = x.Row.DisplayName ?? x.Row.Username,
                // Ohne Nachnamen im Profil gibt es keine Spielersuche und damit keinen Verlauf.
                HasName = x.Identity is not null,
                // Ohne Kennung sucht die Historie ueber den NAMEN und findet damit auch
                // Namensgleiche. Das gehoert gesagt, nicht verschwiegen.
                Exact = x.Identity is not null
                    && (x.Identity.FideId is not null || x.Identity.IdentNumber is not null),
            })
            // Die brauchbaren zuerst — sonst steht die Auswahl voll mit Konten ohne Verlauf.
            .OrderByDescending(f => f.HasName)
            .ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static bool TryParseIds(string? csv, out List<int> ids)
    {
        ids = [];
        if (string.IsNullOrWhiteSpace(csv)) return true;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var id) || id <= 0) return false;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return true;
    }
}
