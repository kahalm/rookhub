namespace RookHub.Api.DTOs;

/// <summary>Status des gespeicherten Lichess-Engine-Tokens (maskiert, nie im Klartext zurück).</summary>
public record LichessEngineCredentialResponse(bool HasCredentials, string? MaskedToken);

public class SaveLichessTokenRequest
{
    public string? Token { get; set; }
}

/// <summary>Eine auf dem Lichess-Konto des Users registrierte External Engine — bewusst OHNE
/// <c>clientSecret</c>: das bleibt serverseitig, der Browser analysiert nur über den RookHub-Proxy.</summary>
public record ExternalEngineDto(string Id, string Name, int MaxThreads, int MaxHash);

/// <summary>Antwort der Engine-Liste. <c>TokenInvalid</c> = Lichess hat den gespeicherten Token
/// abgewiesen (401/403) — die UI fordert dann zur Neu-Eingabe auf, statt leer auszusehen.
/// <c>BackgroundEngineIds</c> = die im Profil gewählten Hintergrund-Engines (der Live-Picker blendet
/// sie aus). MEHRERE sind erlaubt: der Worker rechnet je Engine einen Auftrag, also laufen so viele
/// Auftraege nebeneinander, wie Engines hinterlegt sind.</summary>
/// <para><c>ShareAsHouseEngine</c> = diese Hintergrund-Engines stehen auch fremden Partien offen, die
/// jemand auf der Punktepartie-Seite einwirft (nur ein Admin kann das setzen); <c>CanShareHouseEngine</c>
/// sagt der Karte, ob sie das Haekchen ueberhaupt zeigen soll.</para>
public record ExternalEnginesResponse(bool HasCredentials, bool TokenInvalid, List<ExternalEngineDto> Engines,
    IReadOnlyList<string>? BackgroundEngineIds = null, bool ShareAsHouseEngine = false,
    bool CanShareHouseEngine = false);

/// <summary>Haus-Engine-Freigabe schalten (nur Admin).</summary>
public class SetHouseEngineRequest
{
    public bool Share { get; set; }
}

/// <summary>
/// Analyse-Anfrage des Frontends. Wird serverseitig validiert, auf die Engine-Maxima geklemmt und
/// als Lichess-<c>ExternalEngineWork</c> an den Broker weitergereicht. Genau EINS von
/// <see cref="Depth"/>/<see cref="Movetime"/>/<see cref="Nodes"/> (das Work-Schema ist ein oneOf).
/// </summary>
public class EngineAnalyseRequest
{
    /// <summary>Beliebige Sitzungs-ID; Provider leeren zwischen Sessions ggf. die Hash-Tabelle.</summary>
    public string? SessionId { get; set; }
    public string? InitialFen { get; set; }
    /// <summary>Ab <see cref="InitialFen"/> gespielte Züge in UCI-Notation.</summary>
    public List<string>? Moves { get; set; }
    public int MultiPv { get; set; } = 1;
    public int? Depth { get; set; }
    /// <summary>Millisekunden.</summary>
    public int? Movetime { get; set; }
    public long? Nodes { get; set; }
    /// <summary>Gewünschte Threads/Hash — fehlend = Maximum der Engine; wird immer geklemmt.</summary>
    public int? Threads { get; set; }
    public int? Hash { get; set; }
}

/// <summary>
/// Rumpf von <c>POST/PUT /api/external-engine</c> — genau das, was der offizielle Lichess-Provider
/// schickt (<c>{ name, maxThreads, maxHash, variants, providerSecret }</c>), plus das optionale
/// <c>providerData</c> der Lichess-API.
/// </summary>
public class ExternalEngineRegistrationRequest
{
    public string? Name { get; set; }
    public int? MaxThreads { get; set; }
    public int? MaxHash { get; set; }
    public List<string>? Variants { get; set; }
    public string? ProviderSecret { get; set; }
    public string? ProviderData { get; set; }
}

/// <summary>
/// Eine Engine „RookHub direkt" in der Form der Lichess-API (<c>{ id, name, clientSecret, userId,
/// maxThreads, maxHash, variants, providerData }</c>) — der Provider sucht darin per <c>name</c>.
/// <c>clientSecret</c> nur für den Provider (API-Token), nicht für den Browser.
/// </summary>
public class ExternalEngineRegistrationDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientSecret { get; set; }
    /// <summary>Benutzername des Besitzers (bei Lichess die Konto-Kennung).</summary>
    public string UserId { get; set; } = string.Empty;
    public int MaxThreads { get; set; }
    public int MaxHash { get; set; }
    public List<string> Variants { get; set; } = [];
    public string? ProviderData { get; set; }
}

/// <summary>Antwort von <c>POST /api/token/test</c> je Token (Lichess-Form): <c>scopes</c> kommagetrennt,
/// <c>expires</c> in Millisekunden seit der Epoche oder <c>null</c>.</summary>
public record TokenTestInfo(string UserId, string Scopes, long? Expires);
