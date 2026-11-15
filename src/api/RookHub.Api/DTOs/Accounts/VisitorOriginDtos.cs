namespace RookHub.Api.DTOs.Accounts;

/// <summary>Eindeutige Benutzer eines Landes (ISO-3166, zwei Großbuchstaben); <see cref="Hits"/> nur für Admins.</summary>
public record VisitorCountryDto(string Country, int Users, long? Hits);

/// <summary>
/// Besucher-Herkunft nach Ländern. Für normale Konten immer „alle Oberflächen, alles Erfasste“ und ohne Aufrufe
/// (<see cref="Admin"/> = false, <see cref="Days"/>/<see cref="Surface"/>/<see cref="Hits"/> leer); Admins bekommen
/// ihren Filter zurück. <see cref="Since"/> = erster erfasster Tag überhaupt (nur für Admins; leer, solange nichts gezählt ist).
/// </summary>
public record VisitorOriginsDto(
    bool Admin,
    int? Days,
    string? Surface,
    int UniqueUsers,
    int Countries,
    int UnknownUsers,
    long? Hits,
    DateOnly? Since,
    IReadOnlyList<VisitorCountryDto> ByCountry);
