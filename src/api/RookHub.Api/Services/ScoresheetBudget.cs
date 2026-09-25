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
/// <para>Der ANTWORT-DECKEL eines Aufrufs (max_tokens) kommt aus dem verbleibenden Budget
/// (<see cref="Allowance"/>): so viel Ausgabe, wie nach großzügig geschätzter Eingabe noch bezahlbar ist, höchstens
/// <see cref="MaxOutputTokens"/>. Reicht es nicht einmal für <see cref="MinOutputTokens"/>, startet der Aufruf gar
/// nicht. So wird kein Budget überzogen — und eine lange Partie bekommt trotzdem Platz, solange Budget da ist. Ein
/// fester Deckel von 24 000 schnitt am 10er-Testsatz die längste Partie (92 Halbzüge) ab, nachdem 0,63 $ schon
/// bezahlt waren. Die Preise sind die des Modells (Vorgabe: Claude Opus 5, 5 $ / 25 $ je Million Tokens); wer das
/// Modell wechselt, stellt sie mit um.</para>
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
    /// Nachfrage die vorige Lesung). Gemessen: rund 5 000 bei der ersten Lesung, 12 000 bei einer Nachfrage.</summary>
    public const int ReserveInputTokens = 12_000;

    /// <summary>Oberster Antwort-Deckel eines Aufrufs (Nachdenken + JSON). Gemessen am 10er-Testsatz: 5 000 bis über
    /// 24 000 Ausgabe-Tokens, je länger die Partie, desto mehr.</summary>
    public const int MaxOutputTokens = 64_000;

    /// <summary>Darunter lohnt ein Aufruf nicht — die Antwort würde abgeschnitten.</summary>
    public const int MinOutputTokens = 16_000;

    public long UserDailyMicroUsd { get; }
    public long UserMonthlyMicroUsd { get; }
    public long GlobalDailyMicroUsd { get; }
    public decimal InputUsdPerMTok { get; }
    public decimal OutputUsdPerMTok { get; }
    /// <summary>Kosten des kleinsten sinnvollen Aufrufs (Reserve-Eingabe + <see cref="MinOutputTokens"/>).</summary>
    public long ReserveMicroUsd { get; }

    public ScoresheetBudget(IConfiguration? config)
    {
        UserDailyMicroUsd = Micro(Read(config, "Scoresheet:UserDailyUsd", DefaultUserDailyUsd));
        UserMonthlyMicroUsd = Micro(Read(config, "Scoresheet:UserMonthlyUsd", DefaultUserMonthlyUsd));
        GlobalDailyMicroUsd = Micro(Read(config, "Scoresheet:GlobalDailyUsd", DefaultGlobalDailyUsd));
        InputUsdPerMTok = Read(config, "Scoresheet:InputUsdPerMTok", DefaultInputUsdPerMTok);
        OutputUsdPerMTok = Read(config, "Scoresheet:OutputUsdPerMTok", DefaultOutputUsdPerMTok);
        ReserveMicroUsd = CostMicroUsd(ReserveInputTokens, MinOutputTokens);
    }

    /// <summary>Kosten eines Aufrufs in Millionstel Dollar (aufgerundet — lieber zu streng).</summary>
    public long CostMicroUsd(long inputTokens, long outputTokens)
        => (long)Math.Ceiling(inputTokens * InputUsdPerMTok + outputTokens * OutputUsdPerMTok);

    /// <summary>Darf noch ein Aufruf starten? <c>null</c> = ja, sonst der Grund-Code (<c>userDailyBudget</c>,
    /// <c>userMonthlyBudget</c>, <c>globalBudget</c>).</summary>
    public string? Check(long userToday, long userMonth, long globalToday, bool isAdmin)
        => Allowance(userToday, userMonth, globalToday, isAdmin).Blocked;

    /// <summary>
    /// Wie lang darf die Antwort des nächsten Aufrufs werden? Das knappste Budget entscheidet (Admins: nur das
    /// gesamte); davon geht die Eingabe-Reserve ab, der Rest in Ausgabe-Tokens umgerechnet, gedeckelt auf
    /// <see cref="MaxOutputTokens"/>. Unter <see cref="MinOutputTokens"/> ist der Aufruf gesperrt, mit dem Grund des
    /// knappsten Budgets.
    /// </summary>
    public CallAllowance Allowance(long userToday, long userMonth, long globalToday, bool isAdmin)
    {
        var (left, reason) = (GlobalDailyMicroUsd - globalToday, "globalBudget");
        if (!isAdmin)
        {
            if (UserDailyMicroUsd - userToday < left) (left, reason) = (UserDailyMicroUsd - userToday, "userDailyBudget");
            if (UserMonthlyMicroUsd - userMonth < left) (left, reason) = (UserMonthlyMicroUsd - userMonth, "userMonthlyBudget");
        }
        var forOutput = left - (long)Math.Ceiling(ReserveInputTokens * InputUsdPerMTok);
        var tokens = OutputUsdPerMTok <= 0 ? MaxOutputTokens
            : (int)Math.Min(MaxOutputTokens, Math.Max(0, Math.Floor(forOutput / OutputUsdPerMTok)));
        return tokens < MinOutputTokens ? new CallAllowance(0, reason) : new CallAllowance(tokens, null);
    }

    /// <summary>Kosten einer Antwort mit <paramref name="outputTokens"/> Tokens plus Eingabe-Reserve.</summary>
    public long WorstCaseMicroUsd(int outputTokens) => CostMicroUsd(ReserveInputTokens, outputTokens);

    private static long Micro(decimal usd) => (long)Math.Round(usd * 1_000_000m);

    private static decimal Read(IConfiguration? config, string key, decimal fallback)
        => decimal.TryParse(config?[key], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;
}

/// <summary>Was der nächste Modell-Aufruf darf: Antwort-Deckel in Tokens, oder gesperrt mit Grund.</summary>
public sealed record CallAllowance(int MaxTokens, string? Blocked);
