namespace RookHub.Api.Services;

/// <summary>
/// EINE Stelle fuer den Schalter des RookHub-EIGENEN Chessable-Wegs (<c>Chessable:Enabled</c>, Vorgabe AN) —
/// Codereview 2026-09-29, A10-017. Vorher las jede der sieben Stellen (Registrierung in Program.cs, Controller,
/// Watchdog, Re-Fetch, Kurs-/Repertoire-Dienst, Startpruefung des Schluessels) den Schluessel selbst, je mit eigener
/// Literal-Vorgabe <c>true</c>; eine geaenderte Vorgabe an nur einer Stelle liesse Registrierung und Laufzeitverhalten
/// still auseinanderlaufen. Wer Schluessel oder Vorgabe aendert, aendert sie HIER (<c>ChessableSwitchTests</c>
/// haelt fest, dass sonst niemand den Schluessel liest).
/// </summary>
public static class ChessableSwitch
{
    /// <summary>Konfigurationsschluessel (ENV <c>Chessable__Enabled</c>).</summary>
    public const string Key = "Chessable:Enabled";

    /// <summary>Vorgabe AN, damit eine Installation ohne die Variable sich wie bisher verhaelt.</summary>
    public const bool Default = true;

    /// <summary>Laeuft der eigene Chessable-Weg? Ohne Konfiguration (Tests) gilt die <see cref="Default"/>.</summary>
    public static bool IsEnabled(IConfiguration? configuration) =>
        configuration?.GetValue(Key, Default) ?? Default;
}
