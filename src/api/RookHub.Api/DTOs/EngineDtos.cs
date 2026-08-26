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
/// <c>BackgroundEngineId</c> = im Profil gewählte Hintergrund-Engine (der Live-Picker blendet sie aus).</summary>
public record ExternalEnginesResponse(bool HasCredentials, bool TokenInvalid, List<ExternalEngineDto> Engines,
    string? BackgroundEngineId = null);

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
