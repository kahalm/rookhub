using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Absage einer Engine-Einstellung — der Controller antwortet damit 400 (bzw. 404 bei
/// <paramref name="NotFound"/>) mit <c>{ message }</c>, mit <paramref name="Code"/> als <c>{ message, code }</c>.</summary>
internal sealed record EngineSettingsRejection(string Message, bool NotFound = false, string? Code = null);

/// <summary>Ergebnis von <see cref="EngineSettingsService.SetBackgroundMembershipAsync"/>: <paramref name="Changed"/> =
/// die Liste wurde geschrieben (sonst stand die Engine schon drin bzw. schon nicht drin); <paramref name="Ids"/> = die
/// Liste danach.</summary>
internal sealed record BackgroundMembershipResult(bool Changed, IReadOnlyList<string> Ids);

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
        e.Source == EngineSource.Local ? ExternalEngineDto.SourceRookHub : ExternalEngineDto.SourceLichess, e.Online, e.LastSeenAt);

    // ===== Hintergrund-Engines ===============================================

    /// <summary>Höchstzahl hinterlegbarer Hintergrund-Engines — steht am Modell
    /// (<see cref="LichessEngineCredential.MaxBackgroundEngines"/>, Verlauf und Begründung dort), damit jeder Weg, der die
    /// Liste vergrößert, denselben Deckel prüft.</summary>
    internal const int MaxBackgroundEngines = LichessEngineCredential.MaxBackgroundEngines;

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

        var ids = NormalizeBackgroundIds(requested);
        if (CheckBackgroundList(ids, cred?.BackgroundEngines.Count ?? 0) is { } invalid) return (null, invalid);
        if (CheckLichessToken(cred, ids) is { } noToken) return (null, noToken);
        return (new BackgroundEnginePlan(cred, ids, NothingToStore: cred is null && ids.Count == 0), null);
    }

    /// <summary>Längste zulässige Engine-Kennung (beide Quellen sind 16 Zeichen lang; Spielraum für fremde).</summary>
    internal const int MaxEngineIdLength = 64;

    /// <summary>Bereinigt eine angefragte Liste: getrimmt, Leerwerte und Doppelte raus, Reihenfolge bleibt.</summary>
    internal static List<string> NormalizeBackgroundIds(IEnumerable<string?>? requested) => (requested ?? [])
        .Select(i => i?.Trim() ?? string.Empty)
        .Where(i => i.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>Länge je Kennung, dann Deckel und Spaltenlänge über die gemeinsame Regel
    /// <see cref="LichessEngineCredential.CheckBackgroundList"/> (Code <c>background_list_full</c>). Abgelehnt wird nur, was
    /// die Liste über den Deckel VERGRÖSSERT — eine schon zu lange Liste lässt sich weiter verkleinern.</summary>
    private static EngineSettingsRejection? CheckBackgroundList(IReadOnlyCollection<string> ids, int currentCount)
    {
        if (ids.Any(i => i.Length > MaxEngineIdLength))
            return new EngineSettingsRejection("Invalid engine id");
        return LichessEngineCredential.CheckBackgroundList(ids, currentCount) switch
        {
            LichessEngineCredential.BackgroundListProblem.TooMany => new EngineSettingsRejection(
                $"At most {MaxBackgroundEngines} background engines", Code: Exceptions.ApiErrorCodes.BackgroundListFull),
            LichessEngineCredential.BackgroundListProblem.TooLong => new EngineSettingsRejection(
                "Background engine list too long", Code: Exceptions.ApiErrorCodes.BackgroundListFull),
            _ => null,
        };
    }

    /// <summary>Lichess-Engines brauchen den Token (Meldungen wie bisher); „RookHub direkt" nicht — ein Nutzer ohne
    /// Lichess-Konto bekommt die Zeile dann eben ohne Token.</summary>
    private EngineSettingsRejection? CheckLichessToken(LichessEngineCredential? cred, IEnumerable<string> ids)
    {
        if (!ids.Any(i => !EngineRegistry.IsLocal(i))) return null;
        // Zeile ohne Token (seit dem eigenen Broker möglich) heißt ebenfalls „kein Token“, nicht „unlesbar“.
        if (!HasToken(cred)) return new EngineSettingsRejection("No Lichess token stored");
        if (_encryption.TryDecrypt(cred!.EncryptedToken) is null) return new EngineSettingsRejection("Stored token unreadable");
        return null;
    }

    /// <summary>
    /// EINE Engine in die Hintergrund-Liste des Kontos <paramref name="userId"/> aufnehmen bzw. daraus entfernen (Admin-Reiter
    /// „Engines", 0.951.0) — dieselben Regeln wie das Setzen der ganzen Liste im Profil (<see cref="PlanBackgroundAsync"/>
    /// + <see cref="AllRegisteredAsync"/> + <see cref="SaveBackgroundAsync"/>), nur auf eine Kennung bezogen:
    /// <list type="bullet">
    /// <item><b>Idempotent</b>: steht sie schon drin (bzw. schon nicht drin), wird nichts geschrieben.</item>
    /// <item><b>Aufnehmen</b>: hinten angehängt (Reihenfolge der übrigen bleibt), Deckel über die NEUE Liste; die Engine
    /// muss diesem Konto gehören (<see cref="AllRegisteredAsync"/>: <c>rhe_</c> registriert auf dieses Konto, <c>eei_</c>
    /// über dessen Lichess-Token — Netzfehler gehen an den Aufrufer); eine <c>eei_</c> braucht den Token.</item>
    /// <item><b>Entfernen</b>: immer erlaubt, auch wenn die Liste über dem Deckel steht oder der Token fehlt — die Liste
    /// wird nur kürzer. Auch für fremde und nicht (mehr) registrierte Kennungen.</item>
    /// <item><b><see cref="LichessEngineCredential.PrunedBackgroundNames"/></b>: der Name der Engine (falls auf dieses
    /// Konto registriert) fällt in BEIDEN Richtungen aus der Merkliste — die Wartung trägt eine von Hand entfernte Engine
    /// bei Neuanmeldung nicht wieder ein, und eine von Hand aufgenommene nicht ein zweites Mal.</item>
    /// </list>
    /// Wartende Aufträge bleiben, wo sie sind (wie beim Bearbeiten im Profil).
    /// </summary>
    internal async Task<(BackgroundMembershipResult? Result, EngineSettingsRejection? Rejection)> SetBackgroundMembershipAsync(
        int userId, string? engineId, bool enabled, CancellationToken ct)
    {
        var id = engineId?.Trim() ?? string.Empty;
        if (id.Length == 0 || id.Length > MaxEngineIdLength)
            return (null, new EngineSettingsRejection("Invalid engine id"));

        var cred = await CredentialAsync(userId, ct);
        var current = cred?.BackgroundEngines ?? [];
        if (current.Contains(id) == enabled)
            return (new BackgroundMembershipResult(false, current), null);

        var name = await _db.ExternalEngineRegistrations.AsNoTracking()
            .Where(r => r.Id == id && r.UserId == userId).Select(r => r.Name).FirstOrDefaultAsync(ct);

        if (!enabled)
        {
            // cred ist hier gesetzt: ohne Zeile stünde die Engine in keiner Liste.
            cred!.SetBackgroundEngines(current.Where(e => e != id));
            ForgetPruned(cred, name);
            cred.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return (new BackgroundMembershipResult(true, cred.BackgroundEngines), null);
        }

        var next = NormalizeBackgroundIds(current.Append(id));
        if (CheckBackgroundList(next, current.Count) is { } invalid) return (null, invalid);
        if (CheckLichessToken(cred, [id]) is { } noToken) return (null, noToken);
        if (!await AllRegisteredAsync(userId, [id], ct))
            return (null, new EngineSettingsRejection("Engine not found", NotFound: true, Code: Exceptions.ApiErrorCodes.EngineNotFound));

        if (cred is not null) ForgetPruned(cred, name);
        var saved = await SaveBackgroundAsync(userId, new BackgroundEnginePlan(cred, next, NothingToStore: false), ct);
        return (new BackgroundMembershipResult(true, saved), null);
    }

    private static void ForgetPruned(LichessEngineCredential cred, string? name)
    {
        if (name is not null && cred.PrunedBackground.Contains(name))
            cred.SetPrunedBackground(cred.PrunedBackground.Where(n => n != name));
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
