namespace RookHub.Api.Services.Games;

/// <summary>
/// Wer eine Stellung markiert (0.847.0): ein angemeldeter Nutzer (<see cref="UserId"/>) oder ein Besucher ohne Anmeldung mit
/// der anonymen Kennung der App (<see cref="AnonId"/>, Header <c>X-Visitor-Id</c>). Ein <c>int</c> wird stillschweigend zum
/// angemeldeten Aufrufer — die Aufrufe mit Nutzer-Id bleiben unverändert.
/// </summary>
public readonly record struct MarkCaller(int? UserId, string? AnonId)
{
    public static implicit operator MarkCaller(int userId) => new(userId, null);

    /// <summary>Besucher ohne Anmeldung; eine Kennung, die nicht der strengen Form genügt, zählt als keine.</summary>
    public static MarkCaller Anonymous(string? anonId) =>
        new(null, Ebooks.EbookCaller.IsValidAnonId(anonId) ? anonId : null);

    /// <summary>Weder Konto noch gültige Kennung — darf nichts markieren und sieht nichts.</summary>
    public bool IsNobody => UserId == null && AnonId == null;

    /// <summary>Für Protokoll und Anzeige: „user 5" bzw. „anon 1a2b…".</summary>
    public override string ToString() => UserId is int u ? $"user {u}" : AnonId is { } a ? $"anon {a}" : "nobody";
}
