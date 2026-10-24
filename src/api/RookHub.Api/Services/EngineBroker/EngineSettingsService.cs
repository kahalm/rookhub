using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Absage einer Engine-Einstellung — der Controller antwortet damit 400 (bzw. 404 bei
/// <paramref name="NotFound"/>) mit <c>{ message }</c>.</summary>
internal sealed record EngineSettingsRejection(string Message, bool NotFound = false);

/// <summary>Geprüfte Hintergrund-Liste, bereit zum Speichern: die bereinigten Kennungen und die vorhandene Zeile
/// (<c>null</c> = noch keine). <paramref name="NothingToStore"/>: keine Zeile UND leere Liste — es gibt nichts zu tun.</summary>
internal sealed record BackgroundEnginePlan(LichessEngineCredential? Credential, List<string> Ids, bool NothingToStore);

/// <summary>
/// Die Engine-Einstellungen eines Kontos (Zerlegung Z12, vorher direkt im <c>EngineController</c>): Lichess-Token,
/// Liste der Hintergrund-Engines und die Freigabe als Haus-Engine — alles auf der Zeile
/// <see cref="LichessEngineCredential"/>, die es seit dem eigenen Broker auch OHNE Token gibt. Antworten mit Statuscode
/// baut der Controller; Aufrufe an Lichess (Auflösen der Engines) schirmt er ab.
/// </summary>
internal sealed class EngineSettingsService
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly EngineRegistry _registry;

    internal EngineSettingsService(AppDbContext db, EncryptionService encryption, EngineRegistry registry)
    {
        _db = db;
        _encryption = encryption;
        _registry = registry;
    }

    /// <summary>Ein Lichess-Token ist hinterlegt (die Zeile gibt es seit dem eigenen Broker auch OHNE Token:
    /// sie trägt die Hintergrund-Liste und die Haus-Engine-Freigabe).</summary>
    internal static bool HasToken(LichessEngineCredential? cred) => !string.IsNullOrEmpty(cred?.EncryptedToken);

    private Task<LichessEngineCredential?> CredentialAsync(int userId, CancellationToken ct = default)
        => _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);

    // ===== Lichess-Token =====================================================

    internal async Task<LichessEngineCredentialResponse> GetCredentialsAsync(int userId)
    {
        var cred = await CredentialAsync(userId);
        if (!HasToken(cred))
            return new LichessEngineCredentialResponse(false, null);

        // Robust gegen Key-Rotation/korrupte Daten: kein 500, nur keine Maske (Re-Eingabe nötig).
        var plain = _encryption.TryDecrypt(cred!.EncryptedToken);
        return new LichessEngineCredentialResponse(true, plain is null ? null : Mask(plain));
    }

    /// <summary>Token speichern (verschlüsselt). Absage bei leerem oder zu langem Token.</summary>
    internal async Task<(LichessEngineCredentialResponse? Saved, EngineSettingsRejection? Rejection)> SaveTokenAsync(
        int userId, string? rawToken)
    {
        var token = rawToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return (null, new EngineSettingsRejection("Token is required"));
        if (token.Length > 200)
            return (null, new EngineSettingsRejection("Token too long"));

        var cred = await CredentialAsync(userId);
        var now = DateTime.UtcNow;
        if (cred is null)
        {
            cred = new LichessEngineCredential { UserId = userId, CreatedAt = now };
            _db.LichessEngineCredentials.Add(cred);
        }
        cred.EncryptedToken = _encryption.Encrypt(token);
        cred.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return (new LichessEngineCredentialResponse(true, Mask(token)), null);
    }

    /// <summary>Lichess-Token löschen. Die Zeile verschwindet ganz, wenn danach nichts mehr an ihr hängt;
    /// stehen „RookHub direkt"-Engines in der Hintergrund-Liste, bleibt sie (ohne Token) mit genau diesen —
    /// die Lichess-Engines fallen heraus, ohne Token rechnet dort niemand.</summary>
    internal async Task DeleteTokenAsync(int userId)
    {
        var cred = await CredentialAsync(userId);
        if (cred is null) return;
        var local = cred.BackgroundEngines.Where(EngineRegistry.IsLocal).ToList();
        if (local.Count == 0)
        {
            _db.LichessEngineCredentials.Remove(cred);
        }
        else
        {
            cred.EncryptedToken = string.Empty;
            cred.SetBackgroundEngines(local);
            cred.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>Die Engine-Liste beider Quellen als Antwort (ohne clientSecret).</summary>
    internal static ExternalEnginesResponse ToResponse(EngineListing listing, bool isAdmin)
    {
        var cred = listing.Credential;
        var engines = listing.Engines.Select(ToDto).ToList();
        return new ExternalEnginesResponse(HasToken(cred), listing.TokenInvalid, engines, cred?.BackgroundEngines ?? [],
            cred?.ShareAsHouseEngine ?? false, isAdmin, listing.LichessUnreachable);
    }

    private static ExternalEngineDto ToDto(EngineRef e) => new(e.Id, e.Name, e.MaxThreads, e.MaxHash,
        e.Source == EngineSource.Local ? ExternalEngineDto.SourceRookHub : ExternalEngineDto.SourceLichess, e.Online);

    // ===== Hintergrund-Engines ===============================================

    /// <summary>Hoechstzahl hinterlegbarer Hintergrund-Engines. Nicht die Welt begrenzen, aber auch
    /// nicht unbegrenzt: die Liste steht als CSV in einer Spalte, und jede Engine kostet den Worker
    /// einen eigenen Broker-Stream.
    ///
    /// <para>Seit 2026-09-11 sechzehn statt acht: ein Provider auf einer 40-Kern-Maschine meldet
    /// eine Live-Engine und zwoelf Hintergrund-Engines an, und mit acht blieb die Haelfte davon
    /// unbenutzbar, sobald die vier einer zweiten Maschine noch in der Liste standen. Sechzehn
    /// passen in die Spalte: <see cref="LichessEngineCredential.BackgroundEngineIds"/> fasst 600
    /// Zeichen, eine Kennung ist rund 17 lang (<c>eei_</c> + 12 + Komma) — 16 belegen also etwa
    /// 272. Der Worker rechnet je Engine EINEN Auftrag; der Deckel von vier gleichzeitigen Stroemen
    /// (<c>EngineStreamProxy.MaxConcurrentStreamsPerUser</c>) gilt nur dem Live-Proxy, nicht ihm.</para>
    ///
    /// <para>Seit 2026-10-05 zweiunddreissig: ein zweiter Rechner („RookHub PC") meldet sechzehn
    /// Hintergrund-Engines an, neben den zwoelf der grossen Maschine und den vier von Lichess — mit
    /// sechzehn blieb er ganz aussen vor. 32 Kennungen belegen rund 32 × 17 − 1 = 543 der 600 Zeichen,
    /// mehr passt ohne breitere Spalte nicht.</para></summary>
    internal const int MaxBackgroundEngines = 32;

    /// <summary>
    /// Hintergrund-Engines fuer Analyseauftraege festlegen, Schritt 1: Liste bereinigen und prüfen (leere Liste =
    /// keine). Danach prüft der Aufrufer mit <see cref="AllRegisteredAsync"/>, dass JEDE registriert ist (sonst liefe der
    /// Worker gegen eine Wand), und speichert mit <see cref="SaveBackgroundAsync"/>.
    ///
    /// <para>MEHRERE sind der Sinn: der Worker rechnet je Engine genau einen Auftrag, also laufen so
    /// viele Auftraege nebeneinander, wie hier stehen. Mit einer einzigen ist die Warteschlange
    /// strikt seriell, und ein einzelner zaeher Auftrag legt alles still.</para>
    /// </summary>
    internal async Task<(BackgroundEnginePlan? Plan, EngineSettingsRejection? Rejection)> PlanBackgroundAsync(
        int userId, IEnumerable<string?>? requested, CancellationToken ct)
    {
        var cred = await CredentialAsync(userId, ct);

        var ids = (requested ?? [])
            .Select(i => i?.Trim() ?? string.Empty)
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count > MaxBackgroundEngines)
            return (null, new EngineSettingsRejection($"At most {MaxBackgroundEngines} background engines"));
        if (ids.Any(i => i.Length > 64))
            return (null, new EngineSettingsRejection("Invalid engine id"));

        // Lichess-Engines brauchen den Token (Meldungen wie bisher); „RookHub direkt" nicht — ein Nutzer ohne
        // Lichess-Konto bekommt die Zeile dann eben ohne Token.
        var needsLichess = ids.Any(i => !EngineRegistry.IsLocal(i));
        if (needsLichess && cred is null)
            return (null, new EngineSettingsRejection("No Lichess token stored"));
        if (needsLichess && _encryption.TryDecrypt(cred!.EncryptedToken) is null)
            return (null, new EngineSettingsRejection("Stored token unreadable"));
        return (new BackgroundEnginePlan(cred, ids, NothingToStore: cred is null && ids.Count == 0), null);
    }

    /// <summary>Ist JEDE Engine der Liste registriert? Bricht bei der ersten fehlenden ab. Eine nicht registrierte
    /// Engine in der Liste hiesse, dass ein Teil der Auftraege still in einer Warteschlange landet, die niemand
    /// abarbeitet. Fragt bei Lichess-Engines Lichess — Netzfehler gehen an den Aufrufer.</summary>
    internal async Task<bool> AllRegisteredAsync(int userId, IReadOnlyList<string> ids, CancellationToken ct)
    {
        foreach (var id in ids)
            if ((await _registry.ResolveAsync(userId, id, ct)).Engine is null) return false;
        return true;
    }

    /// <summary>Die geprüfte Liste speichern (legt die Zeile ohne Token an, wenn es keine gibt). → die gespeicherte Liste.</summary>
    internal async Task<IReadOnlyList<string>> SaveBackgroundAsync(int userId, BackgroundEnginePlan plan, CancellationToken ct)
    {
        var cred = plan.Credential;
        if (cred is null)
        {
            cred = new LichessEngineCredential { UserId = userId, EncryptedToken = string.Empty, CreatedAt = DateTime.UtcNow };
            _db.LichessEngineCredentials.Add(cred);
        }
        cred.SetBackgroundEngines(plan.Ids);
        cred.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return cred.BackgroundEngines;
    }

    // ===== Haus-Engine =======================================================

    /// <summary>
    /// Die eigenen Hintergrund-Engines auch fremden Partien oeffnen, die jemand auf der Punktepartie-Seite einwirft
    /// („Haus-Engine"). Wer das darf (nur Admins), prüft der Controller.
    ///
    /// <para>Freigegeben sind immer ALLE hinterlegten Hintergrund-Engines. Eine Auswahl davon waere
    /// eine zweite Liste neben der ersten — und zwei Listen, die dasselbe meinen, laufen
    /// auseinander. Wer weniger teilen will, hinterlegt weniger.</para>
    /// </summary>
    /// <returns>Der neue Stand der Freigabe, oder eine Absage.</returns>
    internal async Task<(bool Shared, EngineSettingsRejection? Rejection)> SetHouseEngineAsync(int userId, bool share,
        CancellationToken ct)
    {
        var cred = await CredentialAsync(userId, ct);
        // Ohne Zeile gibt es keine Hintergrund-Engine (weder Token noch „RookHub direkt"): wie bisher abweisen.
        if (cred is null)
            return (false, new EngineSettingsRejection(share ? "No background engine configured" : "No Lichess token stored"));

        // Ohne hinterlegte Hintergrund-Engine gaebe die Freigabe nichts her: die Partien der anderen
        // landeten in einer Warteschlange, die niemand abarbeitet.
        if (share && cred.BackgroundEngines.Count == 0)
            return (false, new EngineSettingsRejection("No background engine configured"));

        if (cred.ShareAsHouseEngine != share)
        {
            cred.ShareAsHouseEngine = share;
            cred.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return (cred.ShareAsHouseEngine, null);
    }

    internal static string Mask(string value)
    {
        // Nur die letzten 4 Zeichen zur Wiedererkennung zeigen (wie beim Chessable-Bearer).
        if (value.Length <= 4) return new string('*', value.Length);
        return new string('*', Math.Min(20, value.Length - 4)) + value[^4..];
    }
}
