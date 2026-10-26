using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Scoresheet;
using static RookHub.Api.Services.Games.SavedGamePgn;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Speichert/liest vom User auf chess.com/lichess gespeicherte Partien (RepCheck „Partie speichern").
/// Aus der SAN-Zugliste + Metadaten wird serverseitig ein PGN gebaut. Jede Partie bekommt ein
/// eindeutiges ShareToken für den öffentlichen Teilen-Link.
///
/// <para>Dazu „Partie analysieren" und die Bewertungskurve: die Partie wird ueber denselben Einwurf
/// wie auf der Punktepartie-Seite gerechnet (<see cref="GameAnalysisService.CreateForGuessAsync"/>,
/// Ursprung <see cref="GameAnalysisOrigin.SavedGame"/>), und <see cref="SavedGame.GameAnalysisId"/>
/// merkt sich, welche Analyse zur Partie gehoert.</para>
///
/// <para>Fassade (Zerlegung Z13): Hereinholen liegt in <see cref="SavedGameImport"/>, Korrigieren in
/// <see cref="SavedGameCorrection"/>, Analyse und Vereinspartien in <see cref="SavedGameAnalysisLink"/>, der Teilen-Link in
/// <see cref="SavedGameSharing"/>, der PGN-Bau in <see cref="SavedGamePgn"/>.</para>
/// </summary>
public class SavedGameService
{
    private readonly AppDbContext _db;
    private readonly SavedGameSharing _sharing;
    private readonly SavedGameAnalysisLink _link;
    private readonly SavedGameCorrection _correction;
    private readonly SavedGameImport _import;
    private readonly SavedGameListing _listing;

    public SavedGameService(AppDbContext db, GameAnalysisService analyses, RepertoireAnalyzeService repertoires,
        ILogger<SavedGameService>? logger = null)
    {
        _db = db;
        var log = logger ?? NullLogger<SavedGameService>.Instance;
        _sharing = new SavedGameSharing(db);
        _link = new SavedGameAnalysisLink(db, analyses, repertoires);
        _correction = new SavedGameCorrection(db, this);
        _import = new SavedGameImport(db, this, _link, _correction, log);
        _listing = new SavedGameListing(db);
    }

    // ── Deckel je Konto (A6-007) ─────
    // Ohne Deckel füllte ein Skript über POST /api/games/import (200 Partien je Aufruf, wechselnder Round-Header =
    // nie Dublette, Kommentare und Kopfdaten ungekürzt) die Datenbank um mehrere GB je Stunde — die Liste zeigt
    // ohnehin nur die neuesten 500, gelöscht wird nur von Hand.

    /// <summary>So viele gespeicherte Partien hat ein Konto höchstens (alle Quellen zusammen). Darüber legen der
    /// Import (<c>quota</c>) und „Partie speichern" (<see cref="SavedGameQuotaException"/>) nichts Neues mehr an;
    /// Vorhandenes bleibt, Dubletten und Heilen gehen weiter.</summary>
    public const int MaxGamesPerUser = 5_000;

    /// <summary>So viele Zeichen PGN hat ein Konto höchstens gespeichert (Summe über alle Partien) — der Zähldeckel
    /// allein ließe 5 000 × 5 Mio. Zeichen zu. 100 Mio. = 5 000 Partien à 20 000 Zeichen (stark kommentiert; ohne
    /// Kommentare hat eine Partie rund 1 000). Geprüft beim Import und beim Korrigieren, wenn das PGN wächst;
    /// „Partie speichern" braucht die Summe nicht, dort ist jede Partie klein (höchstens 600 Züge à
    /// <see cref="MaxSanLength"/> Zeichen, gekürzte Kopfdaten).</summary>
    public const long MaxPgnCharsPerUser = 100_000_000;

    /// <summary>Längster angenommener Zug bei „Partie speichern" — ein SAN hat höchstens rund 8 Zeichen
    /// (<c>exd8=Q+!</c>); mehr ginge ungeprüft ins PGN.</summary>
    public const int MaxSanLength = 16;

    /// <summary>Wirksamer Zähldeckel (<see cref="MaxGamesPerUser"/>); nur Tests setzen ihn klein.</summary>
    internal int GamesPerUserCap { get; set; } = MaxGamesPerUser;

    /// <summary>Wirksamer Zeichendeckel (<see cref="MaxPgnCharsPerUser"/>); nur Tests setzen ihn klein.</summary>
    internal long PgnCharsPerUserCap { get; set; } = MaxPgnCharsPerUser;

    /// <summary>Was ein Konto belegt: Zahl der Partien und Summe der PGN-Zeichen, in EINER Abfrage
    /// (<c>COUNT(*)</c>, <c>SUM(CHAR_LENGTH(Pgn))</c>). Ohne Partien keine Zeile.</summary>
    internal static IQueryable<SavedGameUsage> UsageQuery(AppDbContext db, int userId)
        => db.SavedGames.AsNoTracking().Where(g => g.UserId == userId).GroupBy(g => g.UserId)
            .Select(grp => new SavedGameUsage(grp.Count(), grp.Sum(g => (long)g.Pgn.Length)));

    internal async Task<SavedGameUsage> UsageAsync(int userId, CancellationToken ct = default)
        => await UsageQuery(_db, userId).FirstOrDefaultAsync(ct) ?? new SavedGameUsage(0, 0);

    /// <summary>Normalisiert die gemeldete Herkunft auf <c>chess.com</c>/<c>lichess</c> (sonst null).</summary>
    public static string? NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var s = source.Trim().ToLowerInvariant();
        if (s is "chesscom" or "chess.com" or "www.chess.com") return "chess.com";
        if (s is "lichess" or "lichess.org") return "lichess";
        return AllowedSources.Contains(s) ? s : null;
    }

    /// <summary>Hoechstens so viele Partie-IDs beantwortet eine Uebersichts-Abfrage.</summary>
    public const int MaxKnownLookup = 300;

    /// <summary>So viele fertige Analysen ohne abgelegte Genauigkeit rechnet EIN Listenaufruf nach — der
    /// Altbestand von vor 0.515.0 ist klein, und die Liste fragt waehrend einer Rechnung alle zehn Sekunden.</summary>
    public const int AccuracyBackfillPerCall = 10;

    /// <summary>So viele Partien holt EIN Listenaufruf sich vor, um ihre Wertungen aus dem PGN
    /// nachzutragen (0.526.0). Gedeckelt, weil dafuer das PGN geladen wird.</summary>
    public const int HeaderBackfillPerCall = 50;

    /// <summary>
    /// Welche dieser Plattform-Partien liegen schon bei RookHub (<see cref="SavedGameListing.KnownAsync"/>)? Nur Existenz
    /// und Analyse-Stand — keine Zuege, keine PGN.
    /// </summary>
    public Task<List<KnownGameDto>> KnownAsync(int userId, string? source,
        IReadOnlyCollection<string>? externalIds, CancellationToken ct = default) =>
        _listing.KnownAsync(userId, source, externalIds, ct);

    /// <summary>Gespeicherte Partien des Users, neueste zuerst (ohne PGN; <see cref="SavedGameListing.ListAsync"/>).</summary>
    public Task<List<SavedGameDto>> ListAsync(int userId, int take = 200) => _listing.ListAsync(userId, take);

    /// <summary>Detail einer eigenen Partie inkl. PGN; null wenn nicht gefunden / fremd.</summary>
    public async Task<SavedGameDetailDto?> GetAsync(int userId, int id)
    {
        var g = await _db.SavedGames.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (g == null) return null;
        var dto = MapDetail(g);
        // Die eigene Partie-Seite (/games/{id}) startet aus der Sicht des Besitzers — dieselbe Regel wie
        // beim Teilen-Link, damit die beiden Seiten nicht verschieden herum aufgehen.
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        dto.OwnerSide = DetermineOwnerSide(g, profile);
        dto.ScanId = await _db.ScoresheetScans.Where(sc => sc.SavedGameId == g.Id).Select(sc => (int?)sc.Id).FirstOrDefaultAsync();
        dto.ClubGameId = g.LeagueClubGameId;
        (dto.WhiteElo, dto.BlackElo) = await _sharing.WithLeagueEloAsync(g.LeagueClubGameId, dto.WhiteElo, dto.BlackElo);
        if (dto.ScanId == null && g.LeagueClubGameId is { } clubId)
        {
            var now = DateTime.UtcNow;
            dto.ClubSheet = await _db.LeagueClubGames.AnyAsync(c => c.Id == clubId && c.UploadedByUserId == g.UserId)
                && await _db.ScoresheetScanArchives.AnyAsync(a => a.LeagueClubGameId == clubId && a.Page == 1 && a.View == 0 && a.ExpiresAt > now);
        }
        return dto;
    }

    /// <summary>Löscht eine eigene Partie; false wenn nicht gefunden / fremd.</summary>
    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var g = await _db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (g == null) return false;
        // Das Formular-Foto geht mit (alle Seiten); die Einlesung selbst bleibt ohne Foto fürs Kontingent stehen (ohne es
        // zu laden).
        var scanKeys = await ScoresheetScanService.KeysAsync(_db.ScoresheetScans.Where(s => s.SavedGameId == id));
        ScoresheetScanService.DetachWithoutLoading(_db, scanKeys);
        ScoresheetScanService.RemovePagesWithoutLoading(_db,
            await ScoresheetScanService.PageKeysAsync(_db, scanKeys.Select(k => k.Id).ToList()));
        ScoresheetScanService.RemoveViewsWithoutLoading(_db,
            await ScoresheetScanService.ViewKeysAsync(_db, scanKeys.Select(k => k.Id).ToList()));
        _db.SavedGames.Remove(g);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Widerruft den öffentlichen Teilen-Link einer eigenen Partie (Codereview A2-006): die Partie bekommt ein NEUES
    /// Token, der alte <c>/g/</c>-Link (samt Roast-, Trainings- und Analyse-Links daran) liefert ab sofort 404. Das neue
    /// Token ist nirgends veröffentlicht — erst „Link kopieren" gibt es heraus. Vorher blieb nur „Partie löschen".
    /// <c>null</c>, wenn die Partie nicht existiert oder fremd ist.
    /// </summary>
    public async Task<string?> RotateShareTokenAsync(int userId, int id, CancellationToken ct = default)
    {
        var g = await _db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (g == null) return null;
        g.ShareToken = await ShareTokens.NewUniqueAsync(t => _db.SavedGames.AnyAsync(x => x.ShareToken == t, ct));
        await _db.SaveChangesAsync(ct);
        return g.ShareToken;
    }

    // ── Hereinholen (SavedGameImport) ─────────────────────────────────

    // ── Vom Server angelegt + korrigiert (Partieformular, 0.529.0) ─────

    /// <summary>Quelle einer aus einem Formular-Foto eingelesenen Partie.</summary>
    public const string ScoresheetSource = "scoresheet";

    /// <summary>So viele Partien nimmt ein PGN-Upload höchstens auf; der Rest bleibt liegen (<c>Truncated</c>).</summary>
    public const int MaxImportGames = 200;

    /// <summary>Höchstens so viele Zeichen PGN je Upload (≈ 200 kommentierte Partien).</summary>
    public const int MaxImportChars = 5_000_000;

    /// <summary>Quelle der hochgeladenen Partien.</summary>
    public const string ImportSource = "pgn";

    /// <summary>Legt eine gespeicherte Partie an oder gibt die vorhandene zurück (<see cref="SavedGameImport.SaveAsync"/>).
    /// Wirft bei ungültiger Eingabe.</summary>
    public Task<SavedGameDetailDto> SaveAsync(int userId, SaveGameInputDto dto) => _import.SaveAsync(userId, dto);

    /// <summary>Legt eine Partie an, die der SERVER gebaut hat (<see cref="SavedGameImport.CreateGeneratedAsync"/>).</summary>
    public Task<SavedGame> CreateGeneratedAsync(int userId, string source, IReadOnlyList<string> sans,
        IReadOnlyDictionary<int, string>? comments, GameHeaderInput header, string? ownerSide = null) =>
        _import.CreateGeneratedAsync(userId, source, sans, comments, header, ownerSide);

    /// <summary>PGN-Upload: jede Partie des Textes eine eigene Partie (<see cref="SavedGameImport.ImportPgnAsync"/>).</summary>
    public Task<PgnImportResultDto> ImportPgnAsync(int userId, string pgn, string? ownerSide = null, CancellationToken ct = default) =>
        _import.ImportPgnAsync(userId, pgn, ownerSide, ct);

    internal static bool FillsMissing(SavedGame existing, SaveGameInputDto dto) => SavedGameImport.FillsMissing(existing, dto);

    // ── Korrigieren (SavedGameCorrection) ─────────────────────────────

    /// <summary>Korrigiert eine eigene Partie (<see cref="SavedGameCorrection.UpdateAsync"/>); <c>null</c>, wenn sie nicht
    /// existiert oder fremd ist, <see cref="ArgumentException"/> bei einem illegalen Zug.</summary>
    public Task<SavedGameDetailDto?> UpdateAsync(int userId, int id, GameUpdateDto dto) => _correction.UpdateAsync(userId, id, dto);

    // ── Analyse + Bewertungskurve (SavedGameAnalysisLink) ─────────────

    public Task<GameAnalyzeResultDto?> AnalyzeAsync(int userId, int savedGameId, CancellationToken ct = default,
        string? lang = null) => _link.AnalyzeAsync(userId, savedGameId, ct, lang);

    public Task<GameAnalyzeResultDto?> AnalyzeSharedAsync(int userId, string token, CancellationToken ct = default,
        string? lang = null) => _link.AnalyzeSharedAsync(userId, token, ct, lang);

    public Task<GameEvalsDto?> GetEvalsAsync(int userId, int savedGameId, CancellationToken ct = default, bool withBook = true) =>
        _link.GetEvalsAsync(userId, savedGameId, ct, withBook);

    public Task<GameEvalsDto?> GetSharedEvalsAsync(string token, int? callerUserId, CancellationToken ct = default, bool withBook = true) =>
        _link.GetSharedEvalsAsync(token, callerUserId, ct, withBook);

    /// <summary>Eine korrigierte Vereinspartie in alle verbundenen Kopien tragen (<see cref="SavedGameAnalysisLink.ApplyClubMovesAsync"/>).</summary>
    public static Task<int> ApplyClubMovesAsync(AppDbContext db, int clubGameId, IReadOnlyList<string> sans, int? except,
        CancellationToken ct = default) => SavedGameAnalysisLink.ApplyClubMovesAsync(db, clubGameId, sans, except, ct);

    public Task<int> LinkClubCopiesAsync(int take = 500, CancellationToken ct = default) => _link.LinkClubCopiesAsync(take, ct);

    // ── Teilen-Link (SavedGameSharing) ────────────────────────────────

    /// <summary>Öffentliche Sicht über das ShareToken; null wenn unbekannt.</summary>
    public Task<SharedGameDto?> GetSharedAsync(string token, int? callerUserId = null) => _sharing.GetSharedAsync(token, callerUserId);

    internal static string? DetermineOwnerSide(SavedGame g, UserProfile? profile) => SavedGameSharing.DetermineOwnerSide(g, profile);

    // ── PGN (SavedGamePgn) ────────────────────────────────────────────

    public static List<string> LegalSans(IReadOnlyList<string> sans, string? startFen = null) => SavedGamePgn.LegalSans(sans, startFen);

    public static string? SafeSourceUrl(string? raw) => SavedGamePgn.SafeSourceUrl(raw);

    public static string BuildPgn(List<string> moves, SaveGameInputDto dto, string result) => SavedGamePgn.BuildPgn(moves, dto, result);

    public static string? CleanTimeControl(string? raw) => SavedGamePgn.CleanTimeControl(raw);

    internal static List<string> SansOf(string fen, IReadOnlyList<string> uci) => SavedGamePgn.SansOf(fen, uci);

    internal static SavedGameDetailDto MapDetail(SavedGame g) => new()
    {
        Id = g.Id,
        Source = g.Source,
        White = g.White,
        Black = g.Black,
        Result = g.Result,
        PlayedAt = g.PlayedAt,
        SourceUrl = SafeSourceUrl(g.SourceUrl),
        ShareToken = g.ShareToken,
        MoveCount = CountPlies(g.Pgn),
        CreatedAt = g.CreatedAt,
        Pgn = g.Pgn,
        // Aus dem PGN und nicht aus den Spalten: hier LIEGT das PGN, und beim Altbestand steht die
        // Wertung nur dort (der Nachtrag laeuft ueber die Liste).
        WhiteElo = ParseEloHeader(g.Pgn, "WhiteElo"),
        BlackElo = ParseEloHeader(g.Pgn, "BlackElo"),
        TimeControl = g.TimeControl,
        Classifier1 = GameClassifier.Effective(g.Source, g.TimeControl, g.Classifier1, g.Classifier2).First,
        Classifier2 = GameClassifier.Effective(g.Source, g.TimeControl, g.Classifier1, g.Classifier2).Second,
        Classifier1Set = GameClassifier.Clean(g.Classifier1),
        Classifier2Set = GameClassifier.Clean(g.Classifier2),
        Tags = GameTags.Parse(g.Tags),
    };
}
