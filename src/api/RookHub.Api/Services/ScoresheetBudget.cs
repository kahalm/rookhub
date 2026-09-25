namespace RookHub.Api.Services;

/// <summary>
/// Kostenbremse für das Einlesen von Partieformularen: jede Lesung ist ein Aufruf beim großen Modell mit Bild
/// und Nachdenken, und das bezahlt der Betreiber. Gerechnet wird in Geld (Millionstel Dollar), nicht in
/// Einlesungen — eine Einlesung mit zwei Nachfragen kostet das Dreifache.
///
/// <para>Drei Budgets, alle konfigurierbar: je Nutzer pro Tag (<c>Scoresheet:UserDailyUsd</c>), je Nutzer über
/// 30 Tage (<c>Scoresheet:UserMonthlyUsd</c>) und für ALLE zusammen pro Tag (<c>Scoresheet:GlobalDailyUsd</c>) —
/// das letzte schützt das Konto auch dann, wenn sich viele Nutzer zugleich bedienen. Admins sind von den
/// Nutzerbudgets ausgenommen, vom Gesamtbudget nicht.</para>
///
/// <para>Ein Aufruf darf nur starten, wenn der UNGÜNSTIGSTE Fall eines Aufrufs (<see cref="ReserveMicroUsd"/>:
/// volle Antwortlänge + großzügig geschätztes Bild) noch ins Budget passt — so wird kein Budget überzogen, auch
/// nicht von der letzten Einlesung des Tages. Die Preise sind die des Modells (Vorgabe: Claude Opus 5,
/// 5 $ / 25 $ je Million Tokens); wer das Modell wechselt, stellt sie mit um.</para>
/// </summary>
public sealed class ScoresheetBudget
{
    /// <summary>Vorgaben in Dollar.</summary>
    public const decimal DefaultUserDailyUsd = 2m;
    public const decimal DefaultUserMonthlyUsd = 10m;
    public const decimal DefaultGlobalDailyUsd = 15m;
    public const decimal DefaultInputUsdPerMTok = 5m;
    public const decimal DefaultOutputUsdPerMTok = 25m;

    /// <summary>So viele Eingabe-Tokens rechnet die Reserve für einen Aufruf (Bild 2000 px + Auftrag + bei einer
    /// Nachfrage die vorige Lesung).</summary>
    public const int ReserveInputTokens = 12_000;

    public long UserDailyMicroUsd { get; }
    public long UserMonthlyMicroUsd { get; }
    public long GlobalDailyMicroUsd { get; }
    public decimal InputUsdPerMTok { get; }
    public decimal OutputUsdPerMTok { get; }
    /// <summary>Kosten des ungünstigsten einzelnen Aufrufs.</summary>
    public long ReserveMicroUsd { get; }

    public ScoresheetBudget(IConfiguration? config, int maxOutputTokens)
    {
        UserDailyMicroUsd = Micro(Read(config, "Scoresheet:UserDailyUsd", DefaultUserDailyUsd));
        UserMonthlyMicroUsd = Micro(Read(config, "Scoresheet:UserMonthlyUsd", DefaultUserMonthlyUsd));
        GlobalDailyMicroUsd = Micro(Read(config, "Scoresheet:GlobalDailyUsd", DefaultGlobalDailyUsd));
        InputUsdPerMTok = Read(config, "Scoresheet:InputUsdPerMTok", DefaultInputUsdPerMTok);
        OutputUsdPerMTok = Read(config, "Scoresheet:OutputUsdPerMTok", DefaultOutputUsdPerMTok);
        ReserveMicroUsd = CostMicroUsd(ReserveInputTokens, maxOutputTokens);
    }

    /// <summary>Kosten eines Aufrufs in Millionstel Dollar (aufgerundet — lieber zu streng).</summary>
    public long CostMicroUsd(long inputTokens, long outputTokens)
        => (long)Math.Ceiling(inputTokens * InputUsdPerMTok + outputTokens * OutputUsdPerMTok);

    /// <summary>Darf noch ein Aufruf starten? <c>null</c> = ja, sonst der Grund-Code (<c>userDailyBudget</c>,
    /// <c>userMonthlyBudget</c>, <c>globalBudget</c>).</summary>
    public string? Check(long userToday, long userMonth, long globalToday, bool isAdmin)
    {
        if (globalToday + ReserveMicroUsd > GlobalDailyMicroUsd) return "globalBudget";
        if (isAdmin) return null;
        if (userToday + ReserveMicroUsd > UserDailyMicroUsd) return "userDailyBudget";
        if (userMonth + ReserveMicroUsd > UserMonthlyMicroUsd) return "userMonthlyBudget";
        return null;
    }

    private static long Micro(decimal usd) => (long)Math.Round(usd * 1_000_000m);

    private static decimal Read(IConfiguration? config, string key, decimal fallback)
        => decimal.TryParse(config?[key], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;
}
