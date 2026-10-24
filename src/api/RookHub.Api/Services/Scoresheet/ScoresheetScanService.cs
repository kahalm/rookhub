using System.Text.Json;
using System.Text.Json.Serialization;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Scoresheet;
using RookHub.Api.DTOs.League;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// „Partieformular einlesen" (0.529.0): Foto hochladen → Claude liest → der Server macht daraus eine legale
/// Partie (<see cref="ScoresheetResolver"/>) → sie liegt in „Meine Partien", das Foto bleibt daneben liegen.
///
/// <para>Das Lesen dauert (Nachdenken + ggf. Nachfrage) typisch eine halbe bis zwei Minuten und läuft deshalb im
/// Hintergrund (<see cref="ScoresheetScanWorker"/>); der Zustand steht in der Datenbank, ein Neustart verliert
/// nichts. Geht die Lesung ab einer Stelle nicht mehr legal auf, fragt der Dienst beim Modell NACH — mit der
/// Stellung, den legalen Zügen dort und dem Hinweis, dass oft ein früherer Eintrag der falsche ist
/// (<see cref="MaxRounds"/> Durchgänge insgesamt). Was dann noch offen ist, steht am Ende der Partie als
/// Kommentar und auf der Korrekturseite.</para>
///
/// <para>Fassade (Zerlegung Z6): die Arbeit machen <see cref="ScoresheetQuota"/> (Kontingent), <see cref="ScoresheetIntake"/>
/// (Annahme), <see cref="ScoresheetProcessor"/> (Lesen, Prüfen, Partie anlegen), <see cref="ScoresheetCorrection"/>
/// (Korrekturweg), <see cref="LeagueScoresheetScans"/> (LeagueHub) und <see cref="ScoresheetScanRows"/> (Zeilen).</para>
/// </summary>
public class ScoresheetScanService
{
    private readonly ScoresheetBudget _budget;
    private readonly ScoresheetQuota _quota;
    private readonly ScoresheetProcessor _processor;
    private readonly LeagueScoresheetScans _league;
    private readonly ScoresheetCorrection _correction;
    private readonly ScoresheetIntake _intake;

    /// <summary>Größter angenommener Upload (je Foto).</summary>
    public const int MaxUploadBytes = 30 * 1024 * 1024;

    /// <summary>So viele Fotos darf ein Formular haben (0.600.0, Wunsch 2026-09-29: „2. Bild für die 2. Seite, + 3. Seite") —
    /// eine lange Partie geht über mehrere Blätter. Gelesen wird in EINEM Aufruf, es bleibt EINE Einlesung (Tageszahl).</summary>
    public const int MaxPages = 3;

    /// <summary>So viele Fotos DERSELBEN Seite (Hauptfoto + weitere, 0.736.0) — der Leser vergleicht sie bei unklaren
    /// Einträgen.</summary>
    public const int MaxPhotosPerPage = 4;

    /// <summary>Größte Anfrage beim Hochladen (alle Fotos zusammen) — so viel lässt auch der Frontend-nginx für
    /// <c>/api/scoresheets</c> durch (<c>DeploymentConfigTests</c>).</summary>
    public const int MaxUploadRequestBytes = 64 * 1024 * 1024;

    /// <summary>Größer wird ein Foto nicht abgelegt — darüber wird es verkleinert gespeichert (3000 px).</summary>
    public const int MaxStoredBytes = 12 * 1024 * 1024;

    /// <summary>Längste Bildseite, die das Modell bekommt.</summary>
    public const int ModelEdge = 2000;

    /// <summary>Längste Bildseite eines verkleinert abgelegten Fotos.</summary>
    public const int StoredEdge = 3000;

    /// <summary>Lese-Durchgänge insgesamt (1 + Nachfragen).</summary>
    public const int MaxRounds = 3;

    /// <summary>So viele festgelegte Halbzüge nimmt das Neu-Aufbereiten an (<see cref="ResolveRestAsync"/>) — wie das
    /// Speichern der Partie (<see cref="SavedGameService.UpdateAsync"/>).</summary>
    public const int MaxPrefixPlies = 600;

    /// <summary>So oft setzt der Worker an einer Einlesung an (ein Absturz mittendrin zählt mit).</summary>
    public const int MaxAttempts = 3;

    /// <summary>So viele Einlesungen dürfen je Nutzer gleichzeitig warten oder laufen.</summary>
    public const int MaxOpenPerUser = 3;

    /// <summary>Vorgabe für <c>Scoresheet:DailyLimit</c> — jede Einlesung kostet echtes Geld beim Modell. Seit 0.568.1
    /// EINE je 24 h für normale Nutzer (Wunsch 2026-09-27; vorher 20), Admins zählen nicht. Was mitzählt:
    /// <see cref="CountingSince"/>.</summary>
    public const int DefaultDailyLimit = 1;

    /// <summary>Vorgabe für <c>Scoresheet:LeagueDailyLimit</c>: Einlesungen für die Vereins-Datenbank (LeagueHub) zählen
    /// EIGENS — zehn je Nutzer und 24 h (Wunsch 2026-09-28: „Formularlimit in dem Modus auf 10/User erhöhen"); die
    /// Kostenbremse (<see cref="ScoresheetBudget"/>) gilt für beide Wege gemeinsam.</summary>
    public const int DefaultLeagueDailyLimit = 10;

    /// <summary>Einlesungen OHNE Konto (LeagueHub-Teilen-Link): höchstens so viele je IP und 24 h …</summary>
    public const int AnonPerIpDailyLimit = 10;
    /// <summary>… und so viele von allen zusammen in 24 h (Wunsch 2026-09-28: „10/IP und 100/Tag").</summary>
    public const int AnonDailyLimit = 100;

    public ScoresheetScanService(AppDbContext db, IScoresheetVisionClient vision, SavedGameService games,
        NotificationService notifications, ILogger<ScoresheetScanService> logger, IConfiguration? config = null,
        IScoresheetEngine? engine = null)
    {
        var dailyLimit = int.TryParse(config?["Scoresheet:DailyLimit"], out var l) && l > 0 ? l : DefaultDailyLimit;
        var leagueDailyLimit = int.TryParse(config?["Scoresheet:LeagueDailyLimit"], out var ll) && ll > 0 ? ll : DefaultLeagueDailyLimit;
        var ipSecret = config?["Jwt:Key"] is { Length: > 0 } k ? k : "rookhub-scoresheet";
        // Scoresheet:Thinking — Vorgabe seit 0.533.2: NUR ABSCHREIBEN (Opus 5.5 mit effort low, die Schachlogik macht der
        // Auflöser): am Testsatz 93,1 % für 0,08 $ und 25 s je Formular, und als einzige Variante schlüssig am langen,
        // verbesserten Kufstein-Formular (10 Reparaturen statt 42–60). true = mit Nachdenken und dem vollen Auftrag,
        // Rückfall ohne.
        var startMode = bool.TryParse(config?["Scoresheet:Thinking"], out var thinking) && thinking
            ? ScoresheetReadMode.Full : ScoresheetReadMode.Transcribe;
        _budget = new ScoresheetBudget(config);
        var reader = new ScoresheetReader(vision);
        // Scoresheet:Plausibility — Engine-Prüfung nach dem Lesen (0.646.0, ScoresheetPlausibility), Vorgabe an.
        var checkEngine = bool.TryParse(config?["Scoresheet:Plausibility"], out var plausible) && !plausible ? null : engine;
        External = string.Equals(config?["Scoresheet:Reader"], "external", StringComparison.OrdinalIgnoreCase);

        _quota = new ScoresheetQuota(db, vision, _budget, dailyLimit, leagueDailyLimit, ipSecret, External);
        _processor = new ScoresheetProcessor(db, vision, games, notifications, logger, _budget, reader, checkEngine, startMode, _quota);
        _league = new LeagueScoresheetScans(db, logger);
        _correction = new ScoresheetCorrection(db);
        _intake = new ScoresheetIntake(db, vision, logger, _quota, _processor, _league, External);
    }

    /// <summary>
    /// Wer liest (<c>Scoresheet:Reader</c>, 0.687.0, Wunsch 2026-10-06: „der Watcher soll das bisherige Verarbeiten via Key
    /// ersetzen — Key ganz abschalten"): <c>external</c> (gesetzt in <c>appsettings.json</c>) = VON AUSSEN — die Einlesung bleibt <c>pending</c>, der Worker rührt sie nicht an, und der Watcher auf
    /// dem Server (Claude in einer eingeschränkten Sitzung) liest die Fotos über <c>/api/admin/scoresheets</c> und gibt
    /// die Lesung mit <see cref="ProcessReadingAsync"/> zurück. Kein Schlüssel, keine Kosten, keine Kostenbremse. Alles andere
    /// (auch ohne Konfiguration, so laufen die Unit-Tests) = wie bisher das Modell über den Anthropic-Schlüssel.
    /// </summary>
    public bool External { get; }

    /// <summary>Die Kostenbremse (für Tests und die Statusanzeige).</summary>
    public ScoresheetBudget Budget => _budget;

    internal static readonly JsonSerializerOptions Json = ScoresheetScanRows.Json;

    /// <summary>Vermerk im Dateinamen einer übernommenen/verworfenen Liga-Einlesung (das Foto ist dann leer).</summary>
    internal const string DiscardedMark = ScoresheetScanRows.DiscardedMark;

    /// <summary>Kennung des „Lesers", wenn die Lesung nicht vom Modell kommt, sondern von Claude in einer Sitzung
    /// (Skill <c>/formulare</c>, 0.684.0) — steht an der Einlesung und im Archiv.</summary>
    public const string ManualModel = "claude-manual";

    /// <summary>Wie lange <see cref="ScoresheetScanArchive"/> Foto und Erkennung aufbewahrt (Wunsch 2026-10-04: „vorerst 365 Tage").</summary>
    public static readonly TimeSpan ArchiveRetention = TimeSpan.FromDays(365);

    /// <summary>Wem eine Liga-Einlesung gehört: einem Konto ODER (ohne Anmeldung, Teilen-Link) dem Browser, der den
    /// geheimen Schlüssel hat. <see cref="Manager"/> = Verwalter der Vereins-Datenbank: darf JEDE Liga-Einlesung prüfen,
    /// übernehmen oder verwerfen (Wunsch 2026-09-28: was hochgeladen, aber nie geprüft wurde, soll nicht im Limbo hängen).
    /// <see cref="ClubId"/> (Mandanten-Schritt 2026-10-07): nur Einlesungen dieses Vereins — LeagueHub setzt ihn immer
    /// (<c>actor with { ClubId = … }</c>); ohne gilt kein Vereinsfilter (nur RookHub-interne Wege).</summary>
    public readonly record struct ScanActor(int? UserId, string? Key, bool Manager = false, int? ClubId = null)
    {
        public static ScanActor User(int userId) => new(userId, null);
        public static ScanActor Anonymous(string key) => new(null, key);
        public static ScanActor ManagerOf(int userId) => new(userId, null, true);
    }

    // ── Kontingent (ScoresheetQuota) ──────────────────────────────────

    /// <summary>Darf für diesen Nutzer jetzt noch ein Modell-Aufruf starten, und wie lang darf die Antwort werden?</summary>
    internal Task<CallAllowance> AllowanceAsync(int? userId, CancellationToken ct = default) => _quota.AllowanceAsync(userId, ct);

    public Task<ScoresheetStatusDto> StatusAsync(int userId, string? purpose = null) => _quota.StatusAsync(userId, purpose);

    /// <summary>HMAC der IP-Adresse — die Adresse selbst wird nie gespeichert (<see cref="ScoresheetQuota.AnonIpHash"/>).</summary>
    public string AnonIpHash(System.Net.IPAddress? ip) => _quota.AnonIpHash(ip);

    internal static string IpKey(System.Net.IPAddress? ip) => ScoresheetQuota.IpKey(ip);

    public Task<ScoresheetStatusDto> AnonStatusAsync(string ipHash) => _quota.AnonStatusAsync(ipHash);

    // ── Annahme (ScoresheetIntake) ────────────────────────────────────

    /// <summary>Nimmt ein Foto an und reiht es ein (<see cref="ScoresheetIntake.CreateAsync(int, byte[], string?, string?, string?, string?, string?)"/>).</summary>
    public Task<(ScoresheetScanDto? Scan, string? Reason)> CreateAsync(int userId, byte[] data, string? contentType,
        string? fileName, string? language, string? ownerSide = null, string? purpose = null) =>
        _intake.CreateAsync(userId, data, contentType, fileName, language, ownerSide, purpose);

    /// <summary>Ein Formular über mehrere Fotos — EINE Einlesung.</summary>
    public Task<(ScoresheetScanDto? Scan, string? Reason)> CreateAsync(int userId, IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide = null, string? purpose = null, int? clubId = null) =>
        _intake.CreateAsync(userId, pages, language, ownerSide, purpose, clubId);

    internal static List<List<ScoresheetUpload>>? SheetGroups(IReadOnlyList<ScoresheetUpload> uploads) =>
        ScoresheetIntake.SheetGroups(uploads);

    /// <summary>Liga-Einlesung aus einer FERTIGEN Lesung, ohne Modell-Aufruf (<see cref="ScoresheetIntake.CreateManualAsync"/>).</summary>
    public Task<(ScoresheetScanDto? Scan, string? Reason)> CreateManualAsync(int ownerUserId, IReadOnlyList<ScoresheetUpload> pages,
        string transcriptionJson, int? clubGameId, CancellationToken ct = default, int? clubId = null) =>
        _intake.CreateManualAsync(ownerUserId, pages, transcriptionJson, clubGameId, ct, clubId);

    /// <summary>Foto OHNE Konto hochladen (<see cref="ScoresheetIntake.CreateAnonymousAsync(byte[], string?, string?, string?, string?, string)"/>).</summary>
    public Task<(ScoresheetScanDto? Scan, string? Key, string? Reason)> CreateAnonymousAsync(byte[] data, string? contentType,
        string? fileName, string? language, string? ownerSide, string ipHash) =>
        _intake.CreateAnonymousAsync(data, contentType, fileName, language, ownerSide, ipHash);

    public Task<(ScoresheetScanDto? Scan, string? Key, string? Reason)> CreateAnonymousAsync(IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide, string ipHash, int? clubId = null) =>
        _intake.CreateAnonymousAsync(pages, language, ownerSide, ipHash, clubId);

    public Task<ScoresheetScanDto?> GetAsync(int userId, int id) => _intake.GetAsync(userId, id);

    public Task<List<ScoresheetScanDto>> ListAsync(int userId, int take = 20) => _intake.ListAsync(userId, take);

    // ── Lesen (ScoresheetProcessor) ───────────────────────────────────

    public Task<List<ExternalPendingScanDto>> PendingForExternalAsync(CancellationToken ct = default) =>
        _processor.PendingForExternalAsync(ct);

    public Task<(byte[] Data, string ContentType)?> PhotoForExternalAsync(int scanId, int page, CancellationToken ct = default,
        int view = 0) => _processor.PhotoForExternalAsync(scanId, page, ct, view);

    /// <summary>Die Lesung einer WARTENDEN Einlesung von außen übernehmen (<see cref="ScoresheetProcessor.ProcessReadingAsync"/>).</summary>
    public Task<(ScoresheetScanDto? Scan, string? Reason)> ProcessReadingAsync(int scanId, string transcriptionJson,
        CancellationToken ct = default) => _processor.ProcessReadingAsync(scanId, transcriptionJson, ct);

    public Task<string?> StartExternalAsync(int scanId, CancellationToken ct = default) => _processor.StartExternalAsync(scanId, ct);

    public Task<string?> FailExternalAsync(int scanId, string? reason, CancellationToken ct = default) =>
        _processor.FailExternalAsync(scanId, reason, ct);

    public Task<int> RequeueInterruptedAsync(CancellationToken ct) => _processor.RequeueInterruptedAsync(ct);

    public Task<int?> ClaimNextAsync(CancellationToken ct) => _processor.ClaimNextAsync(ct);

    /// <summary>Liest eine übernommene Einlesung und legt die Partie an (<see cref="ScoresheetProcessor.ProcessAsync"/>).</summary>
    public Task ProcessAsync(int scanId, CancellationToken ct, CancellationToken shutdown = default) =>
        _processor.ProcessAsync(scanId, ct, shutdown);

    internal static Dictionary<int, string> CommentsFor(ScoresheetResolution r) => ScoresheetProcessor.CommentsFor(r);

    /// <summary>Welche Seite gehört dem Nutzer (<see cref="ScoresheetOwnerSide.GuessOwnerSide"/>)?</summary>
    public static string? GuessOwnerSide(string? white, string? black, IEnumerable<string?> myNames) =>
        ScoresheetOwnerSide.GuessOwnerSide(white, black, myNames);

    // ── Korrekturseite (ScoresheetCorrection) ─────────────────────────

    public Task<(byte[] Data, string ContentType, string FileName, int PageCount)?> PhotoForGameAsync(int userId, int gameId,
        int page = 1) => _correction.PhotoForGameAsync(userId, gameId, page);

    public Task<ScoresheetEditStateDto?> EditStateAsync(int userId, int gameId) => _correction.EditStateAsync(userId, gameId);

    /// <summary>Den Rest ab einer festgelegten Stelle neu aufbereiten (<see cref="ScoresheetCorrection.ResolveRestAsync"/>).</summary>
    public Task<ScoresheetResolveResultDto?> ResolveRestAsync(int userId, int gameId, IReadOnlyList<string> prefix,
        int writtenFrom) => _correction.ResolveRestAsync(userId, gameId, prefix, writtenFrom);

    public Task SaveEditStateAsync(int userId, int gameId, List<ScoresheetPly> plies) =>
        _correction.SaveEditStateAsync(userId, gameId, plies);

    public Task<bool> HasClubSheetAsync(int clubGameId, CancellationToken ct = default) =>
        _correction.HasClubSheetAsync(clubGameId, ct);

    public Task<ScoresheetEditStateDto?> ClubEditStateAsync(int clubGameId, CancellationToken ct = default) =>
        _correction.ClubEditStateAsync(clubGameId, ct);

    public Task<(byte[] Data, string ContentType, int PageCount)?> ClubPhotoAsync(int clubGameId, int page, CancellationToken ct = default,
        int view = 0) => _correction.ClubPhotoAsync(clubGameId, page, ct, view);

    public Task<ScoresheetResolveResultDto?> ResolveClubRestAsync(int clubGameId, IReadOnlyList<string> prefix, int writtenFrom,
        CancellationToken ct = default) => _correction.ResolveClubRestAsync(clubGameId, prefix, writtenFrom, ct);

    public Task SaveClubEditStateAsync(int clubGameId, List<ScoresheetPly>? plies, string finalPgn, CancellationToken ct = default) =>
        _correction.SaveClubEditStateAsync(clubGameId, plies, finalPgn, ct);

    // ── Vereins-Datenbank (LeagueScoresheetScans) ─────────────────────

    public Task<List<ScoresheetScanDto>> LeagueScansAsync(int userId, int? clubId = null) => _league.LeagueScansAsync(userId, clubId);

    public Task<List<LeagueOpenScanDto>> LeagueOpenScansAsync(int viewerId, int? clubId = null, CancellationToken ct = default) =>
        _league.LeagueOpenScansAsync(viewerId, clubId, ct);

    public Task<List<(string Key, ScoresheetScanDto Scan)>> LeagueScansByKeysAsync(IEnumerable<string> keys, int? clubId = null) =>
        _league.LeagueScansByKeysAsync(keys, clubId);

    public Task<LeagueScanStateDto?> LeagueScanStateAsync(ScanActor actor, int? scanId, CancellationToken ct = default) =>
        _league.LeagueScanStateAsync(actor, scanId, ct);

    public Task<(byte[] Data, string ContentType)?> LeagueScanPhotoAsync(ScanActor actor, int? scanId, int page = 1, int view = 0) =>
        _league.LeagueScanPhotoAsync(actor, scanId, page, view);

    public Task<ScoresheetResolveResultDto?> ResolveLeagueRestAsync(ScanActor actor, int? scanId, IReadOnlyList<string> prefix,
        int writtenFrom) => _league.ResolveLeagueRestAsync(actor, scanId, prefix, writtenFrom);

    /// <summary>Liga-Einlesung abschließen, übernommen ODER verworfen (<see cref="LeagueScoresheetScans.CloseLeagueScanAsync"/>).</summary>
    public Task<bool> CloseLeagueScanAsync(ScanActor actor, int? scanId, string? finalPgn = null, int? clubGameId = null) =>
        _league.CloseLeagueScanAsync(actor, scanId, finalPgn, clubGameId);

    /// <summary>Zwei Liga-Einlesungen DESSELBEN Formulars zusammenführen (<see cref="LeagueScoresheetScans.MergeLeagueScansAsync"/>).</summary>
    public Task<string?> MergeLeagueScansAsync(ScanActor actor, int targetId, int otherId, CancellationToken ct = default) =>
        _league.MergeLeagueScansAsync(actor, targetId, otherId, ct);

    // ── Zeilen ohne Foto löschen/leeren (ScoresheetScanRows) ──────────

    public static void RemoveWithoutLoading(AppDbContext db, IEnumerable<(int Id, int? UserId, int? SavedGameId)> scans) =>
        ScoresheetScanRows.RemoveWithoutLoading(db, scans);

    public static void DetachWithoutLoading(AppDbContext db, IEnumerable<(int Id, int? UserId, int? SavedGameId)> scans) =>
        ScoresheetScanRows.DetachWithoutLoading(db, scans);

    public static Task<List<(int Id, int ScanId)>> PageKeysAsync(AppDbContext db, IReadOnlyCollection<int> scanIds) =>
        ScoresheetScanRows.PageKeysAsync(db, scanIds);

    public static void RemovePagesWithoutLoading(AppDbContext db, IEnumerable<(int Id, int ScanId)> pages) =>
        ScoresheetScanRows.RemovePagesWithoutLoading(db, pages);

    public static Task<List<(int Id, int ScanId)>> ViewKeysAsync(AppDbContext db, IReadOnlyCollection<int> scanIds) =>
        ScoresheetScanRows.ViewKeysAsync(db, scanIds);

    public static void RemoveViewsWithoutLoading(AppDbContext db, IEnumerable<(int Id, int ScanId)> views) =>
        ScoresheetScanRows.RemoveViewsWithoutLoading(db, views);

    public static Task<List<(int Id, int? UserId, int? SavedGameId)>> KeysAsync(IQueryable<ScoresheetScan> query) =>
        ScoresheetScanRows.KeysAsync(query);
}
