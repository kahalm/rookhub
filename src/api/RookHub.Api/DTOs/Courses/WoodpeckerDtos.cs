using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs.Courses;

/// <summary>Neues Woodpecker-Training: Kurse in der gewünschten Reihenfolge, Start optional rückdatiert.</summary>
public class CreateWoodpeckerPlanDto
{
    [Required, MinLength(1), MaxLength(50)]
    public List<int> BookIds { get; set; } = new();

    /// <summary>Beginn von Zyklus 1 (UTC); leer = jetzt. Darf in der Vergangenheit liegen, nicht in der Zukunft.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Das bisherige nicht archivierte Training in derselben Transaktion archivieren (0.958.0). Ohne das →
    /// 409, solange eins existiert: je Nutzer gibt es höchstens EIN aktives Training.</summary>
    public bool ArchiveCurrent { get; set; }
}

/// <summary>Beginn des laufenden Zyklus verschieben (rückdatieren).</summary>
public class UpdateWoodpeckerStartDto
{
    [Required]
    public DateTime StartedAt { get; set; }
}

public class RecordWoodpeckerAttemptDto
{
    public int BookPuzzleId { get; set; }
    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
    /// <summary>Höchste aufgedeckte Tipp-Stufe (0–3); geklemmt über <c>AttemptRecording</c>, fehlt = 0.</summary>
    public int HintsUsed { get; set; }
}

public class WoodpeckerBookDto
{
    public int BookId { get; set; }
    public string DisplayName { get; set; } = "";
    /// <summary>Lösbare Aufgaben (ohne Erklärlinien).</summary>
    public int PuzzleCount { get; set; }
}

public class WoodpeckerCycleDto
{
    public int Number { get; set; }
    public int TargetDays { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime Deadline { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>In diesem Zyklus bearbeitete Aufgaben.</summary>
    public int Attempted { get; set; }
    public int Solved { get; set; }
    /// <summary>Summe der Lösezeiten in diesem Zyklus.</summary>
    public int TimeSeconds { get; set; }
    /// <summary>Versuche dieses Zyklus mit mindestens einem Tipp (gelöst oder nicht).</summary>
    public int Hinted { get; set; }
    /// <summary>Davon gelöst — zählen in <see cref="Solved"/> mit; „sauber gelöst“ = <see cref="Solved"/> − dieser Wert.</summary>
    public int SolvedWithHint { get; set; }
}

public class WoodpeckerPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Archiviert am (0.958.0); <c>null</c> = aktiv. Archiviert → <see cref="Status"/> = <c>archived</c>.</summary>
    public DateTime? ArchivedAt { get; set; }
    public List<WoodpeckerBookDto> Books { get; set; } = new();
    /// <summary>Größe des Satzes (in Zyklus 1 wachsend).</summary>
    public int SetSize { get; set; }
    /// <summary>Lösbare Aufgaben aller Kurse zusammen.</summary>
    public int AvailableCount { get; set; }
    public int CurrentCycle { get; set; }
    public int MaxCycles { get; set; }
    /// <summary><c>solving</c> (Zyklus läuft), <c>break</c> (Pause, nächster Zyklus ab <see cref="NextCycleAt"/>),
    /// <c>ready</c> (nächster Zyklus kann starten), <c>completed</c>; <c>archived</c> (0.958.0) geht allem vor — ein
    /// archiviertes Training läuft nie, auch wenn es mitten im Zyklus abgelegt wurde.</summary>
    public string Status { get; set; } = "solving";
    public DateTime? NextCycleAt { get; set; }
    public List<WoodpeckerCycleDto> Cycles { get; set; } = new();
}

/// <summary>
/// Ausführliche Statistik eines Trainings (<c>GET /api/woodpecker/{id}/stats</c>, 0.957.0). Kern ist der Vergleich auf
/// DENSELBEN Aufgaben: der laufende Zyklus gegen jeden früheren, verbunden über <c>BookPuzzleId</c> — nicht die
/// Zyklus-Summen (Zyklus 1 hat den ganzen Satz, der laufende erst einen Teil). Gerechnet in
/// <c>WoodpeckerStatsCalculator</c>; Formeln in <c>docs/api/woodpecker.md</c>.
/// </summary>
public class WoodpeckerStatsDto
{
    public int PlanId { get; set; }
    public int CurrentCycle { get; set; }
    public int SetSize { get; set; }
    /// <summary>Eine Zeile je Zyklus (aufsteigend, der laufende zuletzt).</summary>
    public List<WoodpeckerStatsCycleDto> Cycles { get; set; } = new();
    /// <summary>Wechsel je Aufgabe gegenüber dem direkten Vorzyklus; <c>null</c> in Zyklus 1 oder ohne gemeinsame Aufgabe.</summary>
    public WoodpeckerChangesDto? Changes { get; set; }
    /// <summary>Hochrechnung aus dem Vergleich mit dem Vorzyklus; <c>null</c> in Zyklus 1 oder mit weniger als
    /// <c>WoodpeckerStatsCalculator.MinForecastSample</c> gemeinsamen Aufgaben.</summary>
    public WoodpeckerForecastDto? Forecast { get; set; }
    /// <summary>Laufender Zyklus je lokalem Kalendertag (Zeitzone über <c>?utcOffsetMinutes=</c>), aufsteigend.</summary>
    public List<WoodpeckerStatsDayDto> Days { get; set; } = new();
    /// <summary>Die langsamsten Aufgaben des laufenden Zyklus (höchstens <c>TroubleLimit</c>), längste zuerst.</summary>
    public List<WoodpeckerTroubleDto> Slowest { get; set; } = new();
    /// <summary>Ab hier wird <c>HintsUsed</c> verlässlich gespeichert (Stichtag, <c>WoodpeckerStatsCalculator.HintsTrackedSince</c>);
    /// ältere Versuche stehen auf 0 und zählen in keiner Tipp-Auswertung.</summary>
    public DateTime HintsTrackedSince { get; set; }
    /// <summary>Tipp-Stufen des laufenden Zyklus (nur Versuche ab dem Stichtag).</summary>
    public WoodpeckerHintBreakdownDto Hints { get; set; } = new();
    /// <summary>Tipps auf denselben Aufgaben gegen den Vorzyklus — nur Aufgaben, deren BEIDE Versuche ab dem Stichtag liegen;
    /// <c>null</c> in Zyklus 1 oder ohne solche Aufgabe.</summary>
    public WoodpeckerHintChangesDto? HintChanges { get; set; }
    /// <summary>Lösezeit je Aufgabe in Stufen (schnell/mittel/lang) im laufenden Zyklus, alle Versuche.</summary>
    public WoodpeckerTimeBucketsDto TimeBuckets { get; set; } = new();
    /// <summary>Aufgaben mit Versuchen in mindestens zwei Zyklen, in keinem gelöst (Anzahl gesamt).</summary>
    public int AlwaysMissedCount { get; set; }
    /// <summary>Davon höchstens <c>TroubleLimit</c> zum Ansehen.</summary>
    public List<WoodpeckerTroubleDto> AlwaysMissed { get; set; } = new();
}

public class WoodpeckerStatsCycleDto
{
    public int Number { get; set; }
    public int TargetDays { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int Attempted { get; set; }
    public int Solved { get; set; }
    /// <summary>Gelöst ohne Tipp (gelöst mit Tipp bleibt gelöst, ist hier nur getrennt).</summary>
    public int SolvedClean { get; set; }
    public int Hinted { get; set; }
    public int TimeSeconds { get; set; }
    /// <summary>Quote in Prozentpunkten (0–100); <c>null</c> ohne Versuch.</summary>
    public double? RatePct { get; set; }
    /// <summary>Ø Lösezeit je Aufgabe (Sekunden); <c>null</c> ohne Versuch.</summary>
    public double? AvgSeconds { get; set; }
    /// <summary>Tage vom Beginn bis zum Ende (laufender Zyklus: bis jetzt).</summary>
    public double DaysUsed { get; set; }
    /// <summary>Versuche ab dem Tipp-Stichtag (nur deren Tipp-Zahl ist belastbar).</summary>
    public int HintsTrackedCount { get; set; }
    /// <summary>Nur frühere Zyklen: dieselben Aufgaben wie im laufenden Zyklus (Schnittmenge). <c>null</c> beim laufenden.</summary>
    public WoodpeckerOverlapDto? SameAsNow { get; set; }
}

/// <summary>Laufender Zyklus N gegen einen früheren Zyklus k auf den Aufgaben, die in BEIDEN einen Versuch haben.</summary>
public class WoodpeckerOverlapDto
{
    public int Cycle { get; set; }
    public int Count { get; set; }
    public int SecondsNow { get; set; }
    public int SecondsThen { get; set; }
    public int SolvedNow { get; set; }
    public int SolvedThen { get; set; }
    public int HintedNow { get; set; }
    public int HintedThen { get; set; }
    /// <summary>Quoten in Prozentpunkten; <c>null</c> ohne gemeinsame Aufgabe.</summary>
    public double? RatePctNow { get; set; }
    public double? RatePctThen { get; set; }
    /// <summary>Anteil der damaligen Zeit: <c>SecondsNow / SecondsThen</c> (0,5 = halbe Zeit); <c>null</c>, wenn damals 0 s.</summary>
    public double? TimeRatio { get; set; }
    /// <summary>Wie viel schneller: <c>SecondsThen / SecondsNow</c> (2,0 = doppelt so schnell); <c>null</c>, wenn jetzt 0 s.</summary>
    public double? SpeedFactor { get; set; }
}

/// <summary>Je gemeinsamer Aufgabe: was sich gegenüber dem Vorzyklus geändert hat.</summary>
public class WoodpeckerChangesDto
{
    public int Cycle { get; set; }
    /// <summary>Damals verfehlt, jetzt gelöst.</summary>
    public int Improved { get; set; }
    /// <summary>Damals gelöst, jetzt verfehlt.</summary>
    public int Worsened { get; set; }
    public int BothSolved { get; set; }
    public int BothMissed { get; set; }
    public int Faster { get; set; }
    public int Slower { get; set; }
    public int SameTime { get; set; }
    /// <summary>Mindestens <c>WoodpeckerStatsCalculator.BigChangeSeconds</c> schneller bzw. langsamer als damals.</summary>
    public int MuchFaster { get; set; }
    public int MuchSlower { get; set; }
    /// <summary>Summe der gesparten Sekunden über die schnelleren Aufgaben bzw. der verlorenen über die langsameren.</summary>
    public int SavedSeconds { get; set; }
    public int LostSeconds { get; set; }
    /// <summary>Zeitstufen auf denselben Aufgaben: jetzt und damals.</summary>
    public WoodpeckerTimeBucketsDto BucketsNow { get; set; } = new();
    public WoodpeckerTimeBucketsDto BucketsThen { get; set; } = new();
}

/// <summary>Lösezeit je Aufgabe: schnell (&lt; <c>FastSeconds</c>), mittel, lang (&gt; <c>SlowSeconds</c>).</summary>
public class WoodpeckerTimeBucketsDto
{
    public int Fast { get; set; }
    public int Medium { get; set; }
    public int Slow { get; set; }
}

/// <summary>Tipp-Stufen des laufenden Zyklus — nur Versuche ab dem Stichtag (<see cref="Tracked"/>).</summary>
public class WoodpeckerHintBreakdownDto
{
    /// <summary>Versuche ab dem Stichtag (Grundlage dieser Zahlen).</summary>
    public int Tracked { get; set; }
    /// <summary>Versuche vor dem Stichtag (Tipp-Zahl unbekannt, nicht mitgezählt).</summary>
    public int Untracked { get; set; }
    public int SolvedNoHint { get; set; }
    public int SolvedHint1 { get; set; }
    public int SolvedHint2 { get; set; }
    public int SolvedHint3 { get; set; }
    public int MissedNoHint { get; set; }
    public int MissedWithHint { get; set; }
}

/// <summary>Tipps je gemeinsamer Aufgabe gegen den Vorzyklus (beide Versuche ab dem Stichtag).</summary>
public class WoodpeckerHintChangesDto
{
    public int Cycle { get; set; }
    public int Count { get; set; }
    public int More { get; set; }
    public int Fewer { get; set; }
    public int Same { get; set; }
    /// <summary>Summe der Tipp-Stufen (0–3 je Aufgabe) jetzt bzw. damals.</summary>
    public int SumNow { get; set; }
    public int SumThen { get; set; }
}

/// <summary>Hochrechnung aus dem Vergleich: r = Zeit jetzt / Zeit damals auf denselben Aufgaben (Vorzyklus).</summary>
public class WoodpeckerForecastDto
{
    public int BasisCycle { get; set; }
    /// <summary>Gemeinsame Aufgaben, auf denen r beruht.</summary>
    public int Sample { get; set; }
    public double TimeRatio { get; set; }
    /// <summary>Gesamtzeit des Vorzyklus über alle seine Aufgaben.</summary>
    public int BasisTotalSeconds { get; set; }
    /// <summary>Bisher im laufenden Zyklus gebraucht.</summary>
    public int SpentSeconds { get; set; }
    /// <summary>Noch offene Aufgaben des Satzes im laufenden Zyklus.</summary>
    public int OpenCount { get; set; }
    /// <summary>Rest ≈ r × Zeit des Vorzyklus auf den offenen Aufgaben (+ Ø jetzt × offene ohne Vorzyklus-Versuch).</summary>
    public int RemainingSeconds { get; set; }
    /// <summary>Voraussichtliche Gesamt-Lösezeit des Zyklus = bisher + Rest.</summary>
    public int ProjectedTotalSeconds { get; set; }
    /// <summary>Buchziel: halbe Zeit (0,5).</summary>
    public double GoalRatio { get; set; }
    public bool GoalReached { get; set; }
}

public class WoodpeckerStatsDayDto
{
    /// <summary>Lokales Datum <c>yyyy-MM-dd</c>.</summary>
    public string Date { get; set; } = "";
    public int Attempted { get; set; }
    public int Solved { get; set; }
    public int TimeSeconds { get; set; }
}

/// <summary>Eine Aufgabe zum Ansehen — nur Bezeichnung und Zahlen, nie Stellung oder Lösung.</summary>
public class WoodpeckerTroubleDto
{
    public int BookPuzzleId { get; set; }
    public int BookId { get; set; }
    /// <summary>Titel der Aufgabe, sonst Kapitel; leer, wenn beides fehlt.</summary>
    public string Label { get; set; } = "";
    /// <summary>Zeit im laufenden Zyklus; <c>null</c>, wenn dort noch nicht bearbeitet.</summary>
    public int? SecondsNow { get; set; }
    /// <summary>Zeit im Vorzyklus; <c>null</c> ohne Versuch dort.</summary>
    public int? SecondsPrev { get; set; }
    public bool? SolvedNow { get; set; }
    /// <summary>In wie vielen Zyklen bearbeitet (Sorgenkinder „immer verfehlt“).</summary>
    public int Cycles { get; set; }
}

public class WoodpeckerNextDto
{
    /// <summary>Nächste Aufgabe; <c>null</c>, wenn der Zyklus erledigt ist oder keine Aufgabe mehr übrig.</summary>
    public BookPuzzleDto? Puzzle { get; set; }
    public WoodpeckerPlanDto Plan { get; set; } = new();
}
