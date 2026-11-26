using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs.Tournaments;

/// <summary><c>PUT /api/live-boards/{id}/link</c>: Lichess-Link der Übertragung (Turnier, Runde oder Partie).</summary>
public sealed class LiveBoardLinkRequest
{
    [MaxLength(500)]
    public string? Url { get; set; }
}

/// <summary>
/// Eine Live-Ansicht (1.029.0): dieselben Teile wie die lange Adresse <c>/tournaments/{id}/live?round=&amp;team=&amp;player=&amp;add=&amp;ext=&amp;eval=</c>.
/// Der Server prüft und deckelt sie (<c>SharedLiveViews.Normalize</c>), bevor sie gespeichert oder gesendet wird.
/// </summary>
public sealed class LiveViewState
{
    /// <summary>chess-results-Runde; <c>null</c> = die gerade laufende.</summary>
    public int? Round { get; set; }
    [MaxLength(200)]
    public string? Team { get; set; }
    [MaxLength(200)]
    public string? Player { get; set; }
    /// <summary>Bretter derselben Runde (<c>Begegnung.Brett</c>, <c>Brett</c> oder Lichess-Kennung), höchstens 24.</summary>
    public List<string> Add { get; set; } = [];
    /// <summary>Bretter fremder Übertragungen (<c>roundId.gameId</c>), höchstens 12 aus 4 Runden.</summary>
    public List<string> Ext { get; set; } = [];
    /// <summary>Engine-Balken.</summary>
    public bool Eval { get; set; }
}

/// <summary><c>POST /api/live-boards/views</c>: Ansicht eines Turniers als kurzen, lebenden Link anlegen.</summary>
public sealed class CreateLiveViewRequest
{
    [MaxLength(50)]
    public string? TournamentId { get; set; }
    public LiveViewState? View { get; set; }
}

/// <summary><c>PUT /api/live-boards/views/{token}</c>: neue Ansicht (Besitzer: Konto oder Header <c>X-Live-View-Key</c>).</summary>
public sealed class UpdateLiveViewRequest
{
    public LiveViewState? View { get; set; }
}

/// <summary>Antwort auf Anlegen/Lesen/Ändern. <c>EditKey</c> nur beim Anlegen (danach nie wieder), <c>CanEdit</c> für diesen Aufrufer.</summary>
public sealed record LiveViewResponse(string Token, string TournamentId, LiveViewState View, bool CanEdit, string? EditKey = null);
