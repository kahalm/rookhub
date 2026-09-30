using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RookHub.Api.Data;

/// <summary>
/// Zeitstempel in der Datenbank sind UTC. MariaDB speichert <c>DATETIME</c> ohne Zone, der Treiber liefert jeden
/// gelesenen Wert mit <see cref="DateTimeKind.Unspecified"/> — System.Text.Json schreibt ihn dann OHNE „Z", und der
/// Browser liest „2026-09-29T16:00:00" als ORTSzeit. Folge waren Einzelflicken (SpecifyKind im Backend,
/// <c>serverTime()</c> im Formular) und Fehler, wo einer fehlte: der Dialog der Kalk-Serie zog den Termin bei jedem
/// Speichern um den UTC-Versatz vor, der Turnier-Monitor schaltete sich nach dem Neuladen sofort ab.
///
/// <para>Lesen: jeder Wert wird als UTC gekennzeichnet (am Wert selbst ändert sich nichts). Schreiben: eine Ortszeit
/// wird nach UTC umgerechnet, alles andere bleibt, wie es ist (ohne Angabe gilt UTC, wie bisher).</para>
///
/// <para>Gilt für jede <see cref="DateTime"/>- und <see cref="Nullable{DateTime}"/>-Spalte des Modells
/// (<see cref="AppDbContext"/>.ConfigureConventions). Der Spaltentyp bleibt gleich — keine Migration.</para>
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
