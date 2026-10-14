namespace RookHub.Api.Services.Courses;

/// <summary>
/// Die festen Regeln der Woodpecker-Methode (Smith/Tikkanen, „The Woodpecker Method", Instructions S. 26–29) —
/// bewusst NICHT einstellbar:
/// <list type="number">
/// <item>Zyklus 1: in vier Wochen so viele Aufgaben lösen wie möglich — sie bilden den Satz.</item>
/// <item>Danach mindestens einen ganzen Tag Pause.</item>
/// <item>Jeder weitere Zyklus löst denselben Satz in der halben Zeit (aufgerundet): 28 → 14 → 7 → 4 → 2 → 1 → 1 Tage.</item>
/// <item>Fertig, sobald ein Zyklus an einem Tag geschafft ist — sonst nach dem 7. Zyklus.</item>
/// </list>
/// </summary>
public static class WoodpeckerRules
{
    public const int MaxCycles = 7;

    /// <summary>Tage des ersten Zyklus.</summary>
    public const int FirstCycleDays = 28;

    /// <summary>Pause zwischen zwei Zyklen (mindestens).</summary>
    public static readonly TimeSpan MinBreak = TimeSpan.FromDays(1);

    /// <summary>„An einem Tag geschafft" = Zyklus innerhalb von 24 Stunden erledigt.</summary>
    public static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    /// <summary>Zieldauer des Zyklus <paramref name="number"/> (1-basiert) in Tagen: halbiert, aufgerundet.</summary>
    public static int TargetDays(int number)
    {
        if (number < 1 || number > MaxCycles) throw new ArgumentOutOfRangeException(nameof(number));
        var days = FirstCycleDays;
        for (var i = 1; i < number; i++) days = (days + 1) / 2;
        return days;
    }

    public static DateTime Deadline(int number, DateTime startedAt) => startedAt.AddDays(TargetDays(number));

    /// <summary>Ist die Methode mit diesem beendeten Zyklus abgeschlossen?</summary>
    public static bool CompletesMethod(int number, DateTime startedAt, DateTime finishedAt) =>
        number >= MaxCycles || (number >= 2 && finishedAt - startedAt <= OneDay);
}
