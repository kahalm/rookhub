namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>Aufzeichnungs-Quelle einer Zeit-Scheibe (Index in <see cref="DayBuckets.Source"/>).</summary>
internal enum TrainingSource { RandomPuzzle = 0, CourseBook = 1, Chessable = 2 }

/// <summary>Thema einer Zeit-Scheibe (Index in <see cref="DayBuckets.Theme"/>).</summary>
internal enum TrainingTheme { Opening = 0, Middlegame = 1, Endgame = 2, Tactics = 3, Other = 4 }

/// <summary>Tages-Eimer: Gesamtsekunden + Sekunden je Quelle/Thema + Partien + Manuell-Marker.</summary>
internal sealed class DayBuckets
{
    public int Total;
    public readonly int[] Source = new int[3];
    public readonly int[] Theme = new int[5];
    public int PlayGames;
    public bool HasManual;
}

/// <summary>Deckel gegen aufgeblähte Zeiten — je Quelle, angewandt beim Zusammenführen.</summary>
internal static class TrainingCaps
{
    /// <summary>Obergrenze je Einzel-Puzzle gegen aufgeblähte Zeiten (z.B. Tab stundenlang offen).</summary>
    public const int PerPuzzleCapSeconds = 1800;   // 30 min
    /// <summary>Sanity-Obergrenze je Endlos-Session / manuellem Minuten-Eintrag.</summary>
    public const int PerSessionCapSeconds = 14400; // 4 h
    /// <summary>Obergrenze je Chessable-Zeit-Häppchen (die Extension flusht in kleinen Intervallen).</summary>
    public const int PerChessableFlushCapSeconds = 3600; // 1 h
    /// <summary>Sanity-Obergrenze für manuell eingetragene OTB-Partien je Eintrag.</summary>
    public const int ManualGamesCap = 50;
    /// <summary>Deckel für SELBST eingetragene Minuten-Aktivitäten (10 h). Bewusst höher als
    /// <see cref="PerSessionCapSeconds"/>: der 4-h-Deckel schützt GEMESSENE Sitzungen vor Ausreißern
    /// (vergessener Tab), ein manueller Eintrag ist dagegen getippt und jederzeit korrigierbar — ein
    /// Trainingslager-Tag mit 8 h ist echt. Vorher nahm die Eingabe 600 Minuten an und der Tracker
    /// rechnete still 240 davon an, ohne dass irgendwo etwas von der Kürzung stand.</summary>
    public const int ManualMinutesCap = 600;
    public const int ManualMinutesCapSeconds = ManualMinutesCap * 60;
}

/// <summary>
/// Sammelt die Tages-Eimer je UTC-Kalendertag (<c>DateOnly.FromDateTime(when.Date)</c> — der gespeicherte Wert
/// wird NICHT umgerechnet, egal welche <see cref="DateTimeKind"/> er trägt). Reine Zusammenführung, keine Abfrage.
/// </summary>
internal sealed class TrainingDays
{
    private readonly Dictionary<DateOnly, DayBuckets> _days = new();

    public Dictionary<DateOnly, DayBuckets> Days => _days;

    public DayBuckets Day(DateOnly key)
    {
        if (!_days.TryGetValue(key, out var d)) { d = new DayBuckets(); _days[key] = d; }
        return d;
    }

    public void AddTime(DateTime when, int seconds, int cap, TrainingSource src, TrainingTheme thm)
    {
        var s = Math.Clamp(seconds, 0, cap);
        if (s <= 0) return;
        var d = Day(DateOnly.FromDateTime(when.Date));
        d.Total += s;
        d.Source[(int)src] += s;
        d.Theme[(int)thm] += s;
    }

    /// <summary>Kurs-Versuchszeit (Quelle courseBook): erst einmal gegen Inflation deckeln, dann gleichmäßig
    /// auf die Buch-Themen aufteilen (Rest auf das erste Thema) — so bleibt die Gesamtzeit korrekt
    /// und die Themen-Summe übertreibt nicht.</summary>
    public void AddCourseTime(DateTime when, int seconds, IReadOnlyList<TrainingTheme> themes)
    {
        var capped = Math.Clamp(seconds, 0, TrainingCaps.PerPuzzleCapSeconds);
        if (capped <= 0 || themes.Count == 0) return;
        var each = capped / themes.Count;
        var rem = capped % themes.Count;
        for (var i = 0; i < themes.Count; i++)
        {
            var slice = each + (i == 0 ? rem : 0);
            if (slice > 0) AddTime(when, slice, int.MaxValue, TrainingSource.CourseBook, themes[i]);
        }
    }
}
