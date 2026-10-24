using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.League;

/// <summary>
/// Vereins-Datenbank in LeagueHub (Wunsch 2026-09-28): Mitglieder eines Vereins (zuerst SK Schwaz, seit dem Mandanten-Schritt
/// 2026-10-07 je <see cref="LeagueClub"/> getrennt — jede Methode bekommt den Verein der Anfrage und sieht nur dessen
/// Partien) laden Partien hoch — viele auf einmal als
/// PGN oder einzeln aus einem eingelesenen Partieformular, angemeldet ODER ohne Konto über einen Teilen-Link — und die
/// Spielerkarten der Gegner zeigen sie mit.
///
/// <para>Regeln, alle vom Nutzer vorgegeben:</para>
/// <list type="bullet">
/// <item><b>Beide Namen gegen die Meldelisten</b> (<see cref="LeagueRosterIndex"/>), und wer dort fehlt, gegen das
/// Spielerverzeichnis der Megabase (<see cref="LeagueMegaPlayers"/>, eindeutig über Name oder FIDE-ID — Wunsch
/// 2026-09-28: „standardmäßig auf Megabase matchen, wenn in Tirol kein Treffer"). Ist keine Seite bekannt, wird die
/// Partie abgelehnt (<c>noLeaguePlayer</c>); bleibt nach dem Ersetzen keine bekannte übrig, ebenso (<c>onlyOwnClub</c>).
/// Eine Partie, deren Gegner nur die Megabase kennt, ist übernehmbar, die Übersicht wählt sie aber nicht vor.</item>
/// <item><b>Spieler des Vereins werden durch seinen Namen ersetzt</b> (<see cref="LeagueClub.AnonName"/>, „Schwaz" bzw.
/// „Weilheim"; Vorgabe: jeder, der in seiner jüngsten Saison für den Verein gemeldet ist (<see cref="LeagueClub.OwnsTeam"/>),
/// dazu der Hochladende laut Profil) — ohne Elo und FIDE-ID, die Veranstaltung fällt weg, und es
/// wird WEDER gespeichert, wer dahinter steht, NOCH wer hochgeladen hat oder wann (auch nicht versteckt). Beim PGN-Import
/// zeigt eine Übersicht (<see cref="PreviewAsync"/>) je Partie wer gegen wen, die Vorgaben und ob sie übernommen würde;
/// der Nutzer korrigiert Spieler und Ersetzen, übernommen wird mit seinen Entscheidungen (<see cref="ImportPgnAsync"/>).</item>
/// <item><b>Nur das JAHR</b> des Datums.</item>
/// <item>Gespeichert wird nur die Hauptvariante ohne Kommentare (Kommentare tragen oft Namen und Uhrzeiten), und nur ab
/// der Grundstellung (<c>fromPosition</c>) — die Karten zählen Eröffnungen.</item>
/// <item><b>Zweimal hochgeladen = einmal da</b>: gleiche Züge (<see cref="LeagueClubGame.MovesHash"/>) im gleichen Jahr;
/// bei kurzen Partien (unter <see cref="ShortGamePlies"/> Halbzügen) zusätzlich gleiche Namen — dieselben zwölf
/// Eröffnungszüge spielen viele.</item>
/// <item><b>Über einen Teilen-Link</b> (ohne Konto, <see cref="ImportViaShareAsync"/>, <see cref="AddGameViaShareAsync"/>):
/// gedeckelt je Aufruf und je Link und Tag (<see cref="LeagueShareUploadQuota"/>), und jede Partie trägt den Link als
/// Hash — ein Verwalter entfernt damit alles, was über einen weitergereichten Link kam (<see cref="DeleteByShareAsync"/>,
/// Codereview 2026-09-29, A2-009).</item>
/// </list>
/// <para>Aufbau (Zerlegung Z14): diese Klasse ist die Fassade — Einlesen in <see cref="ClubPgnReader"/> (PGN zerlegen und
/// nachspielen: <see cref="ClubPgnText"/>), wer welche Seite ist in <see cref="ClubSideResolver"/>, Lesewege in
/// <see cref="ClubGameReader"/>, Schreibwege in <see cref="ClubGameWriter"/>.</para>
/// </summary>
public sealed class LeagueClubService
{
    public const int MaxImportGames = ClubPgnText.MaxImportGames;
    public const int MaxImportChars = ClubPgnText.MaxImportChars;
    public const int MaxPlies = ClubPgnText.MaxPlies;
    public const int ShortGamePlies = ClubPgnText.ShortGamePlies;
    public const int PageSize = ClubGameReader.PageSize;

    /// <summary>Wie viele Halbzüge vom Anfang zwei Fassungen gemeinsam haben müssen, um als dieselbe Partie zu gelten
    /// (<see cref="ClubSideResolver.SameGamePrefixPlies"/>).</summary>
    public const int SameGamePrefixPlies = ClubSideResolver.SameGamePrefixPlies;

    /// <summary>Grund für Partien über dem Deckel der Teilen-Link-Uploads (<see cref="LeagueShareUploadQuota"/>).</summary>
    public const string ShareLimitReason = ClubPgnReader.ShareLimitReason;

    public const int MaxClaimKeys = ClubGameWriter.MaxClaimKeys;

    /// <summary>So lange gilt ein gecachter Meldelisten-Index höchstens (<see cref="RosterAsync"/>).</summary>
    internal static readonly TimeSpan RosterCacheTime = ClubGameReader.RosterCacheTime;

    public enum DeleteResult { Deleted, NotFound, Forbidden }

    private readonly ClubGameReader _reader;
    private readonly ClubGameWriter _writer;
    private readonly ClubPgnReader _pgn;

    /// <param name="analyses">Die Analysen des Stapels (<see cref="GameAnalysisOrigin.Club"/>) ziehen beim Korrigieren
    /// und Löschen einer Partie nach; ohne (Tests) bleibt es bei der Partie.</param>
    /// <param name="shareQuota">Der Deckel der Teilen-Link-Uploads — in der App ein Singleton (Program.cs), ohne (Tests)
    /// ein eigener je Dienst.</param>
    /// <param name="cache">Der allgemeine Cache der App — hält den Meldelisten-Index (<see cref="RosterAsync"/>); ohne
    /// (Tests) wird er je Aufruf gebaut.</param>
    /// <remarks>Die Teile (Zerlegung Z14) baut die Fassade selbst aus ihren Abhängigkeiten — eine Paarungssuche für alle,
    /// derselbe Logger (Kategorie bleibt <c>LeagueClubService</c>).</remarks>
    public LeagueClubService(AppDbContext db, ILogger<LeagueClubService> log, Func<DateTime>? now = null,
        GameAnalysisService? analyses = null, LeagueShareUploadQuota? shareQuota = null, IMemoryCache? cache = null)
    {
        var clock = now ?? (() => DateTime.UtcNow);
        var pairings = new LeaguePairingFinder(db) { Today = () => DateOnly.FromDateTime(clock()) };
        _reader = new ClubGameReader(db, log, cache, pairings);
        _writer = new ClubGameWriter(db, log, clock, analyses, pairings, _reader);
        _pgn = new ClubPgnReader(db, log, clock, shareQuota ?? new LeagueShareUploadQuota(), pairings, _reader, _writer);
    }

    // ── Reine Bausteine (für andere Dienste und Tests) ──────────────

    /// <inheritdoc cref="ClubPgnText.FullDateOf"/>
    internal static DateOnly? FullDateOf(string? date) => ClubPgnText.FullDateOf(date);

    /// <inheritdoc cref="ClubPgnText.YearOf"/>
    internal static int? YearOf(string? date, DateTime now) => ClubPgnText.YearOf(date, now);

    internal static string HashOf(IReadOnlyList<string> sans) => ClubPgnText.HashOf(sans);

    internal static string PgnOf(LeagueClubGame g, IReadOnlyList<string> sans) => ClubPgnText.PgnOf(g, sans);

    /// <inheritdoc cref="ClubPgnText.UciOf"/>
    internal static string UciOf(string pgn) => ClubPgnText.UciOf(pgn);

    internal static string OpeningOf(string pgn) => ClubPgnText.OpeningOf(pgn);

    internal static string OpeningOfSans(IEnumerable<string> all) => ClubPgnText.OpeningOfSans(all);

    /// <inheritdoc cref="ClubGameWriter.ShareHashOf"/>
    public static string ShareHashOf(string token) => ClubGameWriter.ShareHashOf(token);

    /// <inheritdoc cref="ClubGameWriter.NewClaimKey"/>
    public static string NewClaimKey() => ClubGameWriter.NewClaimKey();

    public static string ClaimHashOf(string key) => ClubGameWriter.ClaimHashOf(key);

    internal static LeagueClubSideMatchDto MatchDto(LeagueRosterIndex.Hit h, LeagueMegaPlayers.Hit? mega = null, bool alias = false) =>
        ClubSideResolver.MatchDto(h, mega, alias);

    internal static LeagueRosterPersonDto PersonDto(LeagueRosterIndex.Person p) => ClubSideResolver.PersonDto(p);

    internal static LeagueClubGameDto ToDto(LeagueClubGame g, int userId, bool canManage) => ClubGameReader.ToDto(g, userId, canManage);

    // ── Einlesen (ClubPgnReader) ────────────────────────────────────

    /// <inheritdoc cref="ClubPgnReader.PreviewAsync"/>
    public Task<LeagueClubPreviewDto> PreviewAsync(LeagueClub club, int? userId, string pgn, CancellationToken ct = default) =>
        _pgn.PreviewAsync(club, userId, pgn, ct);

    /// <inheritdoc cref="ClubPgnReader.ImportPgnAsync"/>
    public Task<LeagueClubImportResultDto> ImportPgnAsync(LeagueClub club, int? userId, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct = default) =>
        _pgn.ImportPgnAsync(club, userId, pgn, decisions, ct);

    /// <inheritdoc cref="ClubPgnReader.ImportViaShareAsync"/>
    public Task<LeagueClubImportResultDto> ImportViaShareAsync(LeagueClub club, string shareToken, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct = default) =>
        _pgn.ImportViaShareAsync(club, shareToken, pgn, decisions, ct);

    /// <inheritdoc cref="ClubPgnReader.AddGameAsync"/>
    public Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameAsync(LeagueClub club, int? userId, LeagueClubGameRequest req,
        CancellationToken ct = default) => _pgn.AddGameAsync(club, userId, req, ct);

    /// <inheritdoc cref="ClubPgnReader.AddGameViaShareAsync"/>
    public Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameViaShareAsync(LeagueClub club, string shareToken,
        LeagueClubGameRequest req, CancellationToken ct = default, string? claimKey = null) =>
        _pgn.AddGameViaShareAsync(club, shareToken, req, ct, claimKey);

    // ── Lesen (ClubGameReader) ──────────────────────────────────────

    /// <inheritdoc cref="ClubGameReader.RosterAsync"/>
    public Task<LeagueRosterIndex> RosterAsync(LeagueClub club, CancellationToken ct) => _reader.RosterAsync(club, ct);

    /// <inheritdoc cref="ClubGameReader.SuggestPairingsAsync"/>
    public Task<List<LeagueClubPairingDto>> SuggestPairingsAsync(LeagueClub club, LeagueClubPairingQuery q, CancellationToken ct) =>
        _reader.SuggestPairingsAsync(club, q, ct);

    /// <inheritdoc cref="ClubGameReader.PairingsForGameAsync"/>
    public Task<List<LeagueClubPairingDto>?> PairingsForGameAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct) =>
        _reader.PairingsForGameAsync(club, userId, canManage, id, ct);

    /// <inheritdoc cref="ClubGameReader.ClubOfGameAsync"/>
    public Task<int?> ClubOfGameAsync(int id, CancellationToken ct = default) => _reader.ClubOfGameAsync(id, ct);

    /// <inheritdoc cref="ClubGameReader.ListAsync"/>
    public Task<LeagueClubListDto> ListAsync(LeagueClub club, int userId, bool canManage, string? fide, string? q, int page, CancellationToken ct,
        bool mine = false) => _reader.ListAsync(club, userId, canManage, fide, q, page, ct, mine);

    /// <inheritdoc cref="ClubGameReader.GetAsync"/>
    public Task<LeagueClubGameDto?> GetAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default) =>
        _reader.GetAsync(club, userId, canManage, id, ct);

    /// <inheritdoc cref="ClubGameReader.EvalsAsync"/>
    public Task<GameEvalsDto?> EvalsAsync(LeagueClub club, int id, CancellationToken ct = default) => _reader.EvalsAsync(club, id, ct);

    /// <inheritdoc cref="ClubGameReader.CanCorrectAsync"/>
    public Task<bool> CanCorrectAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default) =>
        _reader.CanCorrectAsync(club, userId, canManage, id, ct);

    public Task<string> ExportAsync(LeagueClub club, string? fide, string? q, CancellationToken ct) => _reader.ExportAsync(club, fide, q, ct);

    /// <inheritdoc cref="ClubGameReader.SuggestAsync"/>
    public Task<List<LeagueRosterPersonDto>> SuggestAsync(LeagueClub club, string q, bool all, CancellationToken ct) =>
        _reader.SuggestAsync(club, q, all, ct);

    public Task<LeagueClubMatchDto> MatchAsync(LeagueClub club, string? white, string? black, CancellationToken ct) =>
        _reader.MatchAsync(club, white, black, ct);

    // ── Schreiben (ClubGameWriter) ──────────────────────────────────

    /// <inheritdoc cref="ClubGameWriter.AssignPairingIfUnlinkedAsync"/>
    public Task<(bool Found, int? Id)> AssignPairingIfUnlinkedAsync(int clubGameId, CancellationToken ct = default, int? clubId = null) =>
        _writer.AssignPairingIfUnlinkedAsync(clubGameId, ct, clubId);

    /// <inheritdoc cref="ClubGameWriter.ConfirmPairingAsync"/>
    public Task<(bool? Changed, string? Reason)> ConfirmPairingAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default) =>
        _writer.ConfirmPairingAsync(club, userId, canManage, id, ct);

    /// <inheritdoc cref="ClubGameWriter.ArchiveOlderVersionsAsync"/>
    internal Task<int> ArchiveOlderVersionsAsync(LeagueClubGame game, IReadOnlyList<string> sans, DateTime now, CancellationToken ct) =>
        _writer.ArchiveOlderVersionsAsync(game, sans, now, ct);

    /// <inheritdoc cref="ClubGameWriter.ArchiveOlderAsync"/>
    public Task<int?> ArchiveOlderAsync(LeagueClub club, int id, CancellationToken ct = default) => _writer.ArchiveOlderAsync(club, id, ct);

    /// <inheritdoc cref="ClubGameWriter.UpdateAsync"/>
    public Task<(LeagueClubGame? Game, string? Reason)> UpdateAsync(LeagueClub club, int userId, bool canManage, int id,
        LeagueClubGameUpdateRequest req, CancellationToken ct = default) => _writer.UpdateAsync(club, userId, canManage, id, req, ct);

    /// <inheritdoc cref="ClubGameWriter.CorrectMovesAsync"/>
    public Task<(LeagueClubGame? Game, IReadOnlyList<string>? Sans, string? Reason)> CorrectMovesAsync(LeagueClub club, int userId,
        bool canManage, int id, IReadOnlyList<string> moves, CancellationToken ct = default) =>
        _writer.CorrectMovesAsync(club, userId, canManage, id, moves, ct);

    /// <inheritdoc cref="ClubGameWriter.ClaimPreviewAsync"/>
    public Task<(int Games, int Anonymized)> ClaimPreviewAsync(IEnumerable<string>? keys, CancellationToken ct = default) =>
        _writer.ClaimPreviewAsync(keys, ct);

    /// <inheritdoc cref="ClubGameWriter.ClaimAsync"/>
    public Task<int> ClaimAsync(int userId, IEnumerable<string>? keys, CancellationToken ct = default) => _writer.ClaimAsync(userId, keys, ct);

    /// <inheritdoc cref="ClubGameWriter.ForgetClaimsAsync"/>
    public Task<int> ForgetClaimsAsync(IEnumerable<string>? keys, CancellationToken ct = default) => _writer.ForgetClaimsAsync(keys, ct);

    public Task<DeleteResult> DeleteAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default) =>
        _writer.DeleteAsync(club, userId, canManage, id, ct);

    /// <inheritdoc cref="ClubGameWriter.DeleteByShareAsync"/>
    public Task<int> DeleteByShareAsync(LeagueClub club, string shareToken, bool dryRun, CancellationToken ct = default) =>
        _writer.DeleteByShareAsync(club, shareToken, dryRun, ct);
}
