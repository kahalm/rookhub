using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Woher eine Engine kommt: über Lichess (Token nötig, Broker <c>engine.lichess.ovh</c>) oder
/// direkt bei RookHub angemeldet (eigener Broker in dieser API).</summary>
public enum EngineSource { Lichess, Local }

/// <summary>Was der eigene Broker über eine <c>rhe_</c>-Engine braucht: den Selector der Warteschlange und
/// das Engine-Objekt, das der Provider mit jedem Auftrag bekommt (Lichess-Form).</summary>
public sealed record LocalEngineTarget(string Selector, ExternalEngineRegistrationDto Engine);

/// <summary>Eine Engine, unabhängig von ihrer Quelle. <see cref="Online"/> nur für <c>rhe_</c> (für Lichess
/// wissen wir es nicht).</summary>
public sealed record EngineRef(
    string Id,
    string Name,
    int MaxThreads,
    int MaxHash,
    EngineSource Source,
    bool? Online = null,
    LichessExternalEngine? Lichess = null,
    LocalEngineTarget? Local = null);

public enum EngineLookupFailure { None, NotFound, NoToken }

public sealed record EngineLookup(EngineRef? Engine, EngineLookupFailure Failure)
{
    public static EngineLookup Found(EngineRef e) => new(e, EngineLookupFailure.None);
    public static readonly EngineLookup NotFound = new(null, EngineLookupFailure.NotFound);
    public static readonly EngineLookup NoToken = new(null, EngineLookupFailure.NoToken);
}

/// <summary>Die gemischte Engine-Liste eines Nutzers. <see cref="LichessUnreachable"/> nur, wenn Lichess
/// nicht antwortete UND es direkt angemeldete Engines gibt — ohne solche bleibt es beim bisherigen 502.</summary>
public sealed record EngineListing(
    bool HasCredentials,
    bool TokenInvalid,
    bool LichessUnreachable,
    List<EngineRef> Engines,
    LichessEngineCredential? Credential);

/// <summary>
/// Löst Engine-Kennungen auf — <c>rhe_…</c> aus der eigenen Registrierung, alles andere (<c>eei_…</c>) über
/// den Lichess-Token des Besitzers — und führt beide Quellen zu EINER Liste zusammen. Der Rest der API
/// (Analysebrett-Proxy, Auftrags-Worker, Profil-Karte) fragt nur noch hier.
/// </summary>
public class EngineRegistry
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly LichessEngineService _lichess;
    private readonly EngineSelectorDirectory _directory;
    private readonly LocalBrokerOptions _options;
    private readonly Func<DateTime> _now;

    public EngineRegistry(AppDbContext db, EncryptionService encryption, LichessEngineService lichess,
        EngineSelectorDirectory directory, LocalBrokerOptions options)
        : this(db, encryption, lichess, directory, options, () => DateTime.UtcNow) { }

    public EngineRegistry(AppDbContext db, EncryptionService encryption, LichessEngineService lichess,
        EngineSelectorDirectory directory, LocalBrokerOptions options, Func<DateTime> now)
    {
        _db = db;
        _encryption = encryption;
        _lichess = lichess;
        _directory = directory;
        _options = options;
        _now = now;
    }

    public static bool IsLocal(string engineId) => ProviderSecrets.IsLocalEngineId(engineId);

    /// <summary>Der entschlüsselte Lichess-Token (null = keiner hinterlegt / unlesbar / leer).</summary>
    public string? TokenOf(LichessEngineCredential? cred) =>
        cred is null ? null : _encryption.TryDecrypt(cred.EncryptedToken);

    /// <summary>Beide Quellen. Wirft wie bisher bei nicht erreichbarem Lichess, sofern es keine
    /// direkt angemeldete Engine gibt, die die Liste trotzdem füllt.</summary>
    public async Task<EngineListing> ListAsync(int userId, CancellationToken ct)
    {
        var cred = await _db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        var token = TokenOf(cred);
        var engines = new List<EngineRef>();

        if (_options.Enabled)
        {
            var username = await UsernameAsync(userId, ct);
            var regs = await _db.ExternalEngineRegistrations.AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .ToListAsync(ct);
            engines.AddRange(regs.Select(r => ToRef(r, username)));
        }

        var tokenInvalid = false;
        var lichessUnreachable = false;
        if (token is not null)
        {
            try
            {
                var result = await _lichess.ListEnginesAsync(userId, token, ct);
                tokenInvalid = result.Unauthorized;
                engines.AddRange(result.Engines.Select(FromLichess));
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException)
                                       && engines.Count > 0 && !ct.IsCancellationRequested)
            {
                // Lichess ist weg, die eigenen Engines nicht: die Auswahl bleibt benutzbar.
                lichessUnreachable = true;
            }
        }

        return new EngineListing(token is not null, tokenInvalid, lichessUnreachable, engines, cred);
    }

    /// <summary>Eine Engine des Besitzers <paramref name="ownerUserId"/> auflösen. Lichess-Fehler
    /// (<see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/>) werden durchgereicht — die
    /// Aufrufer behandeln sie wie bisher (502 bzw. Backoff).</summary>
    public async Task<EngineLookup> ResolveAsync(int ownerUserId, string engineId, CancellationToken ct)
    {
        if (IsLocal(engineId))
        {
            if (!_options.Enabled) return EngineLookup.NotFound;
            var reg = await _db.ExternalEngineRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == engineId && r.UserId == ownerUserId, ct);
            if (reg is null) return EngineLookup.NotFound;
            return EngineLookup.Found(ToRef(reg, await UsernameAsync(ownerUserId, ct)));
        }

        var cred = await _db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == ownerUserId, ct);
        var token = TokenOf(cred);
        if (token is null) return EngineLookup.NoToken;
        var engine = await _lichess.ResolveEngineAsync(ownerUserId, token, engineId, ct);
        return engine is null ? EngineLookup.NotFound : EngineLookup.Found(FromLichess(engine));
    }

    /// <summary>Online = der Provider hat binnen <see cref="LocalBrokerOptions.OnlineWindow"/> abgefragt
    /// (im Speicher sekundengenau; nach einem Neustart hilft der minütlich nachgetragene DB-Stempel).</summary>
    public bool IsOnline(ExternalEngineRegistration reg)
    {
        var seen = _directory.LastSeen(reg.ProviderSelector) ?? reg.LastSeenAt;
        return seen is { } t && _now() - t <= _options.OnlineWindow;
    }

    private EngineRef ToRef(ExternalEngineRegistration r, string username) => new(
        r.Id, r.Name, Math.Max(1, r.MaxThreads), Math.Max(1, r.MaxHash), EngineSource.Local,
        Online: IsOnline(r),
        Local: new LocalEngineTarget(r.ProviderSelector, ExternalEngineRegistrationService.ToDto(r, username, withSecret: true)));

    private static EngineRef FromLichess(LichessExternalEngine e) =>
        new(e.Id, e.Name, e.MaxThreads, e.MaxHash, EngineSource.Lichess, Lichess: e);

    private async Task<string> UsernameAsync(int userId, CancellationToken ct) =>
        await _db.AppUsers.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(ct)
        ?? userId.ToString();
}
