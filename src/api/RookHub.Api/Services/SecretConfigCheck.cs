namespace RookHub.Api.Services;

/// <summary>
/// Erkennt die Platzhalter-Geheimnisse aus den öffentlichen Beispiel-Dateien (<c>.env*.example</c>,
/// <c>compose*.example</c>, die <c>.env.example</c> von schach-bot und piratechess_docker) und entscheidet,
/// was ein solcher Wert beim Start bewirkt.
///
/// <para><b>Warum:</b> Alle Repos sind öffentlich. Vorher erkannte nur der <see cref="AdminSeeder"/> einen
/// Platzhalter (exakt „change_me"). Der JWT-Platzhalter „change_me_to_a_secure_key_at_least_32_chars" hat
/// 43 Byte und bestand damit die einzige Startprüfung (≥ 32 Byte) — wer ihn beim Neuaufsetzen stehen ließ,
/// betrieb RookHub mit einem Signaturschlüssel, mit dem sich jeder Leser des Repos ein Admin-JWT bauen
/// konnte. Discord-Link- und Bot-Stats-Geheimnis teilten sich sogar denselben Platzhalter.</para>
///
/// <para><b>Folgen je Art:</b></para>
/// <list type="bullet">
/// <item><see cref="CryptoKeys"/> (Signatur, Verschlüsselung): in Production bricht der Start ab
/// (<see cref="ThrowIfCryptoKeyIsPlaceholder"/>), sonst Error-Log.</item>
/// <item><see cref="InboundSecrets"/> (Geheimnisse, mit denen RookHub EINGEHENDE Aufrufe prüft): das
/// Feature bleibt aus, genau wie bei leerem Wert (<see cref="Usable"/> liefert <c>null</c>), + Error-Log.</item>
/// <item><see cref="OutboundSecrets"/> (Schlüssel, die RookHub nur MITSCHICKT): nur Error-Log. Das Loch
/// sitzt bei der Gegenstelle, die denselben Wert aus derselben <c>.env</c> bekommt und dort geprüft werden
/// muss; hier abzuschalten legte nur das Feature lahm, ohne etwas zu schließen.</item>
/// </list>
/// </summary>
public static class SecretConfigCheck
{
    /// <summary>Anfänge der Beispielwerte, ohne Rücksicht auf Groß-/Kleinschreibung („CHANGE_ME_…" im Crawler).
    /// Ein zufällig erzeugter Schlüssel beginnt nie so.</summary>
    private static readonly string[] PlaceholderPrefixes = ["change_me", "changeme", "your_"];

    /// <summary>Signatur-/Verschlüsselungsschlüssel: Config-Schlüssel → Variable in der <c>.env</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> CryptoKeys = new Dictionary<string, string>
    {
        ["Jwt:Key"] = "JWT_KEY",
        ["Encryption:Key"] = "ENCRYPTION_KEY",
    };

    /// <summary>Eingehend geprüfte Geheimnisse: Config-Schlüssel → Feature, das bei einem Platzhalter AUS bleibt.</summary>
    public static readonly IReadOnlyDictionary<string, string> InboundSecrets = new Dictionary<string, string>
    {
        ["Discord:LinkSecret"] = "Discord-Verknüpfung (POST /api/profile/discord/link) bleibt aus",
        ["SchachBot:StatsSecret"] = "Bot-Statistik (/api/bot/player-progress), der signierte Bot-Heartbeat (/api/bot/heartbeat) und die Bot-Signatur der Ergebnis-GETs bleiben aus",
    };

    /// <summary>Nur mitgeschickte Schlüssel: Config-Schlüssel → Gegenstelle, die den Platzhalter annimmt.</summary>
    public static readonly IReadOnlyDictionary<string, string> OutboundSecrets = new Dictionary<string, string>
    {
        ["Crawler:ApiKey"] = "der Crawler",
        ["Chessable:ServiceKey"] = "piratechess",
        ["SchachBot:WebhookSecret"] = "der Schach-Bot",
    };

    /// <summary>
    /// Ist <paramref name="value"/> ein Platzhalter aus den Beispielen? Leer ist KEIN Platzhalter (das
    /// entscheidet jede Stelle selbst, meist „Feature aus"). Erkannt werden die Präfixe
    /// <c>change_me</c>/<c>changeme</c>/<c>your_</c> und die Spitzklammer-Form
    /// <c>&lt;min-32-char-secret-for-jwt-signing&gt;</c> aus den Kommentaren der Beispiel-Composes.
    /// </summary>
    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (v.Length > 1 && v[0] == '<' && v[^1] == '>') return true;
        return PlaceholderPrefixes.Any(p => v.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Der Wert, wenn er als Geheimnis taugt; <c>null</c> bei leer (auch nur Leerzeichen) oder Platzhalter.</summary>
    public static string? Usable(string? value)
        => string.IsNullOrWhiteSpace(value) || IsPlaceholder(value) ? null : value;

    /// <summary>
    /// Startprüfung vor dem Bau der App: in Production ist ein Platzhalter als Signatur- oder
    /// Verschlüsselungsschlüssel ein Startabbruch. Außerhalb von Production nur ein Error-Log
    /// (<see cref="LogStartupFindings"/>) — lokale Entwicklung und Tests sollen nicht daran scheitern.
    /// </summary>
    public static void ThrowIfCryptoKeyIsPlaceholder(IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsProduction()) return;
        foreach (var (key, envName) in CryptoKeys)
        {
            if (IsPlaceholder(config[key]))
                throw new InvalidOperationException(
                    $"{key} ({envName}) ist ein Platzhalter aus den öffentlichen Beispiel-Dateien — damit startet " +
                    "RookHub in Production nicht. Einen echten Zufallswert setzen (z. B. openssl rand -base64 48).");
        }
    }

    /// <summary>Jeden Platzhalter-Fund einmal beim Start auf Error melden, samt Folge; dazu eine Warnung bei
    /// leerem <c>SchachBot:StatsSecret</c>.</summary>
    public static void LogStartupFindings(IConfiguration config, ILogger logger)
    {
        foreach (var (key, envName) in CryptoKeys)
            if (IsPlaceholder(config[key]))
                logger.LogError(
                    "Startprüfung: {ConfigKey} ist ein Platzhalter aus den Beispiel-Dateien — {Effect}",
                    key, $"in Production bricht der Start hier ab; {envName} durch einen echten Zufallswert ersetzen");

        foreach (var (key, effect) in InboundSecrets)
            if (IsPlaceholder(config[key]))
                logger.LogError(
                    "Startprüfung: {ConfigKey} ist ein Platzhalter aus den Beispiel-Dateien — {Effect}",
                    key, effect + ", bis ein echtes Geheimnis gesetzt ist");

        foreach (var (key, peer) in OutboundSecrets)
            if (IsPlaceholder(config[key]))
                logger.LogError(
                    "Startprüfung: {ConfigKey} ist ein Platzhalter aus den Beispiel-Dateien — {Effect}",
                    key, $"{peer} nimmt ihn als gültigen Schlüssel an; in beiden Stacks einen echten Wert setzen");

        // Leeres Bot-Stats-Geheimnis: /api/bot/player-progress antwortet 503 not-configured, und der Bot
        // pausiert die Motivations-DMs. Das ist legitim (Installation ohne Bot), aber im Betrieb meist ein
        // Konfigurationsfehler beim Neuaufsetzen (compose-Default ${SCHACH_BOT_STATS_SECRET:-}) — der
        // Endpoint selbst loggt je Aufruf nichts, also EINMAL hier.
        if (string.IsNullOrWhiteSpace(config["SchachBot:StatsSecret"]))
            logger.LogWarning(
                "Startprüfung: {ConfigKey} ist leer — {Effect}",
                "SchachBot:StatsSecret",
                "/api/bot/player-progress und /api/bot/heartbeat antworten 503 not-configured, die Motivations-DMs des Schach-Bots pausieren und der log-watcher vermisst seinen Heartbeat");
    }
}
