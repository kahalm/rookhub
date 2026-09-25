namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Einstellungen des eigenen Engine-Brokers (<c>Engine:LocalBroker:*</c>). Alle Werte haben Vorgaben
/// und werden geklemmt — keine Pflichtvariable in den Compose-Dateien.
/// </summary>
public sealed class LocalBrokerOptions
{
    public const string Section = "Engine:LocalBroker";

    /// <summary>Aus = keine Provider-Endpunkte (404), keine <c>rhe_</c>-Engines in der Auswahl.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Long-Poll des Providers: so lange wartet <c>POST /api/external-engine/work</c> auf Arbeit,
    /// dann 204. Der Provider bricht selbst nach 12 s ab — der Wert muss darunter bleiben.</summary>
    public TimeSpan AcquireWait { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>So lange wartet ein Anfragender, bis ein Provider den Auftrag übernommen hat (Upload
    /// begonnen) — sonst 503 (<c>ProviderTimeout</c>), wie bei lila-engine.</summary>
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Höchstzahl wartender Aufträge je Selector; darüber 503 an den Anfragenden.</summary>
    public int MaxQueuedPerEngine { get; init; } = 64;

    /// <summary>Abgeholte Aufträge ohne Upload verfallen nach dieser Frist.</summary>
    public TimeSpan OngoingExpiry { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>„Online" = letzter Abruf des Providers liegt höchstens so weit zurück.</summary>
    public TimeSpan OnlineWindow { get; init; } = TimeSpan.FromSeconds(30);

    public static LocalBrokerOptions FromConfig(IConfiguration config)
    {
        var s = config.GetSection(Section);
        return new LocalBrokerOptions
        {
            Enabled = s.GetValue<bool?>("Enabled") ?? true,
            // Kommazahlen erlaubt (Tests fahren mit Bruchteilen einer Sekunde).
            AcquireWait = Seconds(s.GetValue<double?>("AcquireWaitSeconds") ?? 10, 0.05, 11),
            ProviderTimeout = Seconds(s.GetValue<double?>("ProviderTimeoutSeconds") ?? 15, 0.1, 120),
            MaxQueuedPerEngine = Math.Clamp(s.GetValue<int?>("MaxQueuedPerEngine") ?? 64, 1, 1024),
            OngoingExpiry = Seconds(s.GetValue<double?>("OngoingExpirySeconds") ?? 30, 1, 600),
            OnlineWindow = Seconds(s.GetValue<double?>("OnlineWindowSeconds") ?? 30, 5, 600),
        };
    }

    private static TimeSpan Seconds(double value, double min, double max) =>
        TimeSpan.FromSeconds(Math.Clamp(value, min, max));
}
