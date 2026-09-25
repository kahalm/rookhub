using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Ergebnis einer Registrierung: entweder die Engine oder Status + Meldung.</summary>
public sealed record EngineRegistrationResult(ExternalEngineRegistration? Engine, int Status, string? Error)
{
    public static EngineRegistrationResult Ok(ExternalEngineRegistration e) => new(e, 200, null);
    public static EngineRegistrationResult Fail(int status, string error) => new(null, status, error);
}

/// <summary>
/// Registrierung „RookHub direkt": dieselben Regeln, die der Provider von Lichess kennt
/// (<c>GET/POST/PUT /api/external-engine</c>) — der Provider bleibt unverändert, auf dem Rechner des
/// Nutzers ändert sich nur die Adresse. Die Regeln: Name je Nutzer eindeutig (≤ 200 Zeichen),
/// höchstens <see cref="MaxEnginesPerUser"/> Engines, <c>maxThreads</c> 1..1024, <c>maxHash</c>
/// 1..1 048 576 MiB, <c>variants</c> ⊇ <c>chess</c>; gespeichert wird der Selector des
/// <c>providerSecret</c>, nicht das Secret.
/// </summary>
public class ExternalEngineRegistrationService
{
    public const int MaxEnginesPerUser = 32;
    public const int MaxNameLength = 200;
    public const int MaxThreadsLimit = 1024;
    public const int MaxHashLimit = 1_048_576;
    public const int MinProviderSecretLength = 16;
    public const int MaxProviderSecretLength = 1024;
    public const int MaxProviderDataLength = 500;

    /// <summary>Die Varianten, die der Provider überhaupt meldet (seine eigene Liste). Angeboten wird
    /// nur <c>chess</c>; die anderen werden gespeichert, damit ein PUT sie nicht als Fehler abweist.</summary>
    public static readonly IReadOnlySet<string> KnownVariants = new HashSet<string>(StringComparer.Ordinal)
    {
        "chess", "antichess", "atomic", "crazyhouse", "horde", "kingofthehill", "racingkings", "3check",
    };

    private readonly AppDbContext _db;
    private readonly EngineSelectorDirectory _directory;
    private readonly ILogger<ExternalEngineRegistrationService> _logger;

    public ExternalEngineRegistrationService(AppDbContext db, EngineSelectorDirectory directory,
        ILogger<ExternalEngineRegistrationService> logger)
    {
        _db = db;
        _directory = directory;
        _logger = logger;
    }

    public Task<List<ExternalEngineRegistration>> ListAsync(int userId, CancellationToken ct = default) =>
        _db.ExternalEngineRegistrations.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);

    /// <summary>Prüft den Rumpf. <c>null</c> = in Ordnung.</summary>
    public static string? Validate(ExternalEngineRegistrationRequest? r)
    {
        if (r is null) return "Request body is required";
        if (string.IsNullOrWhiteSpace(r.Name)) return "name is required";
        if (r.Name.Length > MaxNameLength) return $"name must be at most {MaxNameLength} characters";
        if (r.MaxThreads is not { } threads || threads is < 1 or > MaxThreadsLimit)
            return $"maxThreads must be 1..{MaxThreadsLimit}";
        if (r.MaxHash is not { } hash || hash is < 1 or > MaxHashLimit)
            return $"maxHash must be 1..{MaxHashLimit}";
        var variants = r.Variants ?? [];
        if (!variants.Contains("chess")) return "variants must include chess";
        if (variants.FirstOrDefault(v => !KnownVariants.Contains(v)) is { } unknown)
            return $"unsupported variant: {unknown}";
        if (string.IsNullOrEmpty(r.ProviderSecret)
            || r.ProviderSecret.Length is < MinProviderSecretLength or > MaxProviderSecretLength)
            return $"providerSecret must be {MinProviderSecretLength}..{MaxProviderSecretLength} characters";
        if (r.ProviderData is { Length: > MaxProviderDataLength })
            return $"providerData must be at most {MaxProviderDataLength} characters";
        return null;
    }

    /// <summary>
    /// <c>POST</c> (id = null) oder <c>PUT</c> (id gesetzt). Ein POST mit einem schon vorhandenen Namen
    /// AKTUALISIERT diesen Eintrag, statt einen zweiten anzulegen: der Name ist die Identität, und ein
    /// Provider, dessen GET-Liste gerade scheiterte, soll sich beim nächsten Start nicht verdoppeln.
    /// </summary>
    public async Task<EngineRegistrationResult> SaveAsync(int userId, string? id,
        ExternalEngineRegistrationRequest request, CancellationToken ct = default)
    {
        if (Validate(request) is { } error) return EngineRegistrationResult.Fail(400, error);

        var mine = await _db.ExternalEngineRegistrations.Where(r => r.UserId == userId).ToListAsync(ct);
        ExternalEngineRegistration? target;
        if (id is not null)
        {
            target = mine.FirstOrDefault(r => r.Id == id);
            if (target is null) return EngineRegistrationResult.Fail(404, "Engine not found");
        }
        else
        {
            target = mine.FirstOrDefault(r => r.Name == request.Name);
        }

        // Gleicher Name bis auf Groß-/Kleinschreibung bei einem ANDEREN Eintrag: die Datenbank
        // (utf8mb4_unicode_ci) hält ihn für denselben, der eindeutige Index würde beim Speichern werfen.
        if (mine.Any(r => r != target && string.Equals(r.Name.TrimEnd(), request.Name!.TrimEnd(), StringComparison.OrdinalIgnoreCase)))
            return EngineRegistrationResult.Fail(409, "An engine with this name already exists");

        var now = DateTime.UtcNow;
        var isNew = target is null;
        if (target is null)
        {
            if (mine.Count >= MaxEnginesPerUser)
                return EngineRegistrationResult.Fail(400, $"At most {MaxEnginesPerUser} engines per account");
            target = new ExternalEngineRegistration
            {
                Id = await NewUniqueIdAsync(ct),
                UserId = userId,
                ClientSecret = ProviderSecrets.NewClientSecret(),
                CreatedAt = now,
            };
            _db.ExternalEngineRegistrations.Add(target);
        }

        target.Name = request.Name!;
        target.MaxThreads = request.MaxThreads!.Value;
        target.MaxHash = request.MaxHash!.Value;
        target.Variants = string.Join(',', (request.Variants ?? []).Distinct(StringComparer.Ordinal));
        target.ProviderData = request.ProviderData;
        target.ProviderSelector = ProviderSecrets.Selector(request.ProviderSecret!);
        target.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Zwei Provider desselben Namens im selben Augenblick, oder eine Schreibweise, die die
            // Sortierung der Datenbank für gleich hält (Akzente) — kein 500, sondern ein klares Nein.
            _logger.LogWarning(ex, "EngineBroker: Registrierung abgewiesen (Name-Konflikt) user={UserId}", userId);
            return EngineRegistrationResult.Fail(409, "An engine with this name already exists");
        }

        _directory.Register(target.ProviderSelector);
        _logger.LogInformation("EngineBroker: Engine {Action} user={UserId} engine={EngineId} threads={Threads} hash={Hash}",
            isNew ? "registriert" : "aktualisiert", userId, target.Id, target.MaxThreads, target.MaxHash);
        return EngineRegistrationResult.Ok(target);
    }

    /// <summary>Löscht eine eigene Registrierung (und nimmt sie aus der Hintergrund-Liste).
    /// <c>false</c> = gibt es nicht (oder gehört jemand anderem).</summary>
    public async Task<bool> DeleteAsync(int userId, string id, CancellationToken ct = default)
    {
        var reg = await _db.ExternalEngineRegistrations.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (reg is null) return false;
        _db.ExternalEngineRegistrations.Remove(reg);

        // Eine gelöschte Engine in der Hintergrund-Liste hieße: ein Teil der Aufträge landet in einer
        // Warteschlange, die niemand abarbeitet.
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cred is not null && cred.BackgroundEngines.Contains(id))
        {
            cred.SetBackgroundEngines(cred.BackgroundEngines.Where(e => e != id));
            cred.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        _directory.Invalidate();
        _logger.LogInformation("EngineBroker: Engine geloescht user={UserId} engine={EngineId}", userId, id);
        return true;
    }

    public static ExternalEngineRegistrationDto ToDto(ExternalEngineRegistration r, string username, bool withSecret) => new()
    {
        Id = r.Id,
        Name = r.Name,
        ClientSecret = withSecret ? r.ClientSecret : null,
        UserId = username,
        MaxThreads = r.MaxThreads,
        MaxHash = r.MaxHash,
        Variants = [.. r.VariantList],
        ProviderData = r.ProviderData,
    };

    private async Task<string> NewUniqueIdAsync(CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var id = ProviderSecrets.NewEngineId();
            if (!await _db.ExternalEngineRegistrations.AnyAsync(r => r.Id == id, ct)) return id;
        }
        throw new InvalidOperationException("Could not allocate an engine id");
    }
}
