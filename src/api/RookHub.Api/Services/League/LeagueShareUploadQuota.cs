namespace RookHub.Api.Services.League;

/// <summary>
/// Deckel für Partien, die OHNE Konto über einen Teilen-Link in die Vereins-Datenbank kommen (Singleton, Codereview
/// 2026-09-29, A2-009): höchstens <see cref="PerCall"/> je Aufruf und <see cref="PerLinkPerDay"/> je Link und UTC-Tag.
///
/// <para>Der Link wird in Mannschafts-Gruppen geteilt und gilt mindestens sieben Tage — wer ihn weiterreicht, konnte vorher
/// 500 Partien je Aufruf (60 Aufrufe je Minute) auf die FIDE-IDs echter Spieler schreiben, und alle Spielerkarten zeigten
/// sie. Der Upload-Wunsch vom 28.09. bleibt: direkt importiert wird weiter, nur gedeckelt. Die Seite schickt ohnehin
/// Portionen zu 10 Partien.</para>
///
/// <para>Bewusst im ARBEITSSPEICHER und nicht aus der Datenbank gezählt: eine Partie mit „Schwaz" trägt keinen Zeitpunkt
/// (Wunsch des Nutzers, siehe <see cref="Models.LeagueClubGame"/>), ein Zählen über <c>CreatedAt</c> ließe genau diese
/// Partien durch. Ein Neustart setzt den Tag zurück — wie beim Tipp-Deckel (<see cref="HintTaskQueue"/>).</para>
/// </summary>
public sealed class LeagueShareUploadQuota
{
    public const int PerCall = 50;
    public const int PerLinkPerDay = 200;

    private readonly Dictionary<string, int> _usedToday = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private DateOnly _day;

    public LeagueShareUploadQuota(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Bis zu <paramref name="wanted"/> Partien für diesen Link reservieren (<see cref="Lease.Granted"/>).
    /// Reserviert statt erst hinterher gezählt: zwei Aufrufe gleichzeitig kämen sonst beide am Deckel vorbei. Was beim
    /// <c>Dispose</c> nicht als <see cref="Lease.Kept"/> vermerkt ist, geht zurück — ein abgebrochener Aufruf (Handy
    /// verliert das Netz, die Seite wiederholt die Portion) verbraucht so nichts.</summary>
    public Lease Reserve(string linkHash, int wanted)
    {
        var granted = 0;
        if (wanted > 0)
            lock (_gate)
            {
                Roll();
                _usedToday.TryGetValue(linkHash, out var used);
                granted = Math.Clamp(PerLinkPerDay - used, 0, wanted);
                if (granted > 0) _usedToday[linkHash] = used + granted;
            }
        return new Lease(this, linkHash, granted);
    }

    /// <summary>Nicht gebrauchte Plätze zurückgeben (nach einem Tageswechsel ist ohnehin alles frei).</summary>
    private void Return(string linkHash, int unused)
    {
        if (unused <= 0) return;
        lock (_gate)
        {
            Roll();
            if (_usedToday.TryGetValue(linkHash, out var used))
                _usedToday[linkHash] = Math.Max(0, used - unused);
        }
    }

    /// <summary>Reservierte Plätze eines Aufrufs; <see cref="Kept"/> vor dem Speichern setzen — scheitert das Speichern,
    /// bleiben sie verbraucht (lieber einmal zu streng als am Deckel vorbei).</summary>
    public sealed class Lease : IDisposable
    {
        private readonly LeagueShareUploadQuota _quota;
        private readonly string _linkHash;
        private bool _disposed;

        internal Lease(LeagueShareUploadQuota quota, string linkHash, int granted)
        {
            _quota = quota; _linkHash = linkHash; Granted = granted;
        }

        public int Granted { get; }
        public int Kept { get; set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _quota.Return(_linkHash, Granted - Math.Min(Kept, Granted));
        }
    }

    private void Roll()
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        if (today == _day) return;
        _usedToday.Clear();
        _day = today;
    }
}
