namespace RookHub.Api.Services.Engines;

/// <summary>
/// Die Einstellungen des <see cref="AnalysisJobWorker"/> aus <c>AnalysisJobs:*</c>, geklemmt wie bisher im Konstruktor
/// (zweite Zerlegungsrunde W7b). Einmal beim Start gelesen.
/// </summary>
internal sealed record AnalysisJobWorkerSettings
{
    /// <summary>Takt der Schleife (<c>AnalysisJobs:TickSeconds</c>, 1..60, Vorgabe 5).</summary>
    public required TimeSpan Tick { get; init; }

    /// <summary>Ruhe nach einem Live-Stream, bevor eine Engine wieder Hintergrund rechnet
    /// (<c>AnalysisJobs:IdleGraceSeconds</c>, 0..600, Vorgabe 20).</summary>
    public required TimeSpan IdleGrace { get; init; }

    /// <summary>Abstand der Zwischensicherungen (<c>AnalysisJobs:PersistIntervalSeconds</c>, 1..60, Vorgabe 5).</summary>
    public required TimeSpan PersistInterval { get; init; }

    /// <summary>Ohne erste Datenzeile binnen dieser Frist gilt der Provider als nicht rechnend — sonst hinge der
    /// Auftrag unbegrenzt in „läuft" (der HttpClient hat bewusst kein Timeout) und blockierte den Slot des Users.
    /// <para>Konfiguration: <c>AnalysisJobs:FirstLineTimeoutSeconds</c> (30..3600, Vorgabe 300).</para></summary>
    public required TimeSpan FirstLineTimeout { get; init; }

    /// <summary>Frist fuer jede WEITERE Datenzeile, nachdem die erste da war. Der Wert muss ueber der
    /// laengsten ehrlichen Iteration liegen (Tiefe 40+ mit fuenf Linien dauert Minuten), deshalb sehr
    /// grosszuegig — er faengt nicht die langsame, sondern die HAENGENDE Engine.
    /// <para>Am 2026-09-12 auf Prod gebraucht: ein Auftrag stand vier Stunden auf „laeuft" bei Tiefe 11,
    /// waehrend der Provider unveraendert dieselbe <c>info</c>-Zeile wiederholte (time 27 ms, nodes 24006,
    /// Tempo auf die Stelle genau eingefroren). Der Waechter der ersten Zeile war da laengst entschaerft,
    /// einen zweiten gab es nicht — und weil die Pumpe je Nutzer nur EINE Partie fuettert, standen darueber
    /// 434 wartende Partien und elf freie Engines still.</para>
    /// <para>Konfiguration: <c>AnalysisJobs:StallTimeoutSeconds</c> (300..86400, Vorgabe 1800).</para></summary>
    public required TimeSpan StallTimeout { get; init; }

    /// <summary>Wartezeit, nachdem ein Auftrag wegen eines 503/504 auf eine ANDERE Engine
    /// umgehaengt wurde. Kurz, weil die neue Engine sofort laufen kann — und trotzdem
    /// unbedenklich: der Worker rechnet je Engine nur einen Auftrag, es sind also hoechstens so
    /// viele Versuche gleichzeitig unterwegs wie Engines hinterlegt sind, nicht so viele wie
    /// Auftraege offen sind. Faellt der Broker ganz aus, pendeln die Auftraege damit im Takt
    /// dieser Frist statt im Zwei-Minuten-Takt — bei sechzehn Engines rund ein Abruf je Sekunde.
    /// <para>Konfiguration: <c>AnalysisJobs:EngineSwitchBackoffSeconds</c> (5..600, Vorgabe 15).</para></summary>
    public required TimeSpan EngineSwitchBackoff { get; init; }

    /// <summary>Ab dieser Laufzeit gilt „kein Tiefenfortschritt" NICHT mehr als Fehlversuch: eine echte Sackgasse
    /// (Matt/Patt, abgelehnte Arbeit) endet in Sekunden, ein langer Lauf ohne neue Zeile ist eine gekappte
    /// Verbindung — dafür darf der Auftrag nicht als gescheitert gelten.
    /// <para>Konfiguration: <c>AnalysisJobs:FruitlessMinRuntimeSeconds</c> (5..3600, Vorgabe 60).</para></summary>
    public required TimeSpan FruitlessMinRuntime { get; init; }

    /// <summary>Engines, die nur auf ausdrückliche Anforderung rechnen (<see cref="ExplicitOnlyEngines"/>).</summary>
    public required IReadOnlySet<string> ExplicitOnly { get; init; }

    /// <summary>Schrittweite der Zwischenstände einer Knotenanalyse (<c>AnalysisJobs:SnapshotStepNodes</c>, Vorgabe 10 000, 0 = aus).</summary>
    public required long SnapshotStep { get; init; }

    internal static AnalysisJobWorkerSettings From(IConfiguration config)
    {
        var step = config.GetValue<long?>("AnalysisJobs:SnapshotStepNodes") ?? 10_000;
        return new AnalysisJobWorkerSettings
        {
            Tick = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:TickSeconds") ?? 5, 1, 60)),
            IdleGrace = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:IdleGraceSeconds") ?? 20, 0, 600)),
            PersistInterval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:PersistIntervalSeconds") ?? 5, 1, 60)),
            FirstLineTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:FirstLineTimeoutSeconds") ?? 300, 30, 3600)),
            StallTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:StallTimeoutSeconds") ?? 1800, 300, 86400)),
            EngineSwitchBackoff = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:EngineSwitchBackoffSeconds") ?? 15, 5, 600)),
            FruitlessMinRuntime = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:FruitlessMinRuntimeSeconds") ?? 60, 5, 3600)),
            ExplicitOnly = ExplicitOnlyEngines.From(config),
            SnapshotStep = step <= 0 ? 0 : Math.Clamp(step, 1_000, 1_000_000),
        };
    }
}
