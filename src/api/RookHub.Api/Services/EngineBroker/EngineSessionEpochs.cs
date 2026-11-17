using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Was eine erkannte fremde Ausgabe für die Engine bedeutet (<see cref="EngineSessionEpochs.ReportForeign"/>).</summary>
/// <param name="InARow">Erkennungen in Folge auf dieser Engine, ohne sauberen Lauf dazwischen (diese mitgezählt).</param>
/// <param name="RepeatOffender">Ab <see cref="EngineSessionEpochs.RepeatOffenderStrikes"/> in Folge: die Neusynchronisierung
/// hilft offenbar nicht, der Provider-Prozess muss neu gestartet werden.</param>
/// <param name="LogError">Den Fehler jetzt protokollieren (höchstens einmal je Engine und <see cref="EngineSessionEpochs.ErrorLogInterval"/>).</param>
/// <param name="BenchedUntil">Bis dann bekommt die Engine keine Hintergrund-Aufträge (nur bei <paramref name="RepeatOffender"/>).</param>
public readonly record struct ForeignStrike(int InARow, bool RepeatOffender, bool LogError, DateTime? BenchedUntil);

/// <summary>
/// Sitzungs-Epoche je Engine (17.11.2026, „Engine-Provider-Versatz"): der lichess-Provider gerät nach einem abgerissenen
/// Upload dauerhaft eine Suche hinter die Aufträge — die Ausgabe der vorigen Suche bleibt in der Pipe, jeder folgende
/// Auftrag bekommt die Zeilen der VORIGEN Stellung samt deren <c>bestmove</c>. Der Provider schickt
/// <c>ucinewgame</c> + <c>isready</c> genau dann, wenn sich die <c>sessionId</c> der Arbeit ändert, und sein
/// <c>isready()</c> verwirft alles bis <c>readyok</c> — auch die liegengebliebene Ausgabe. Eine neue Sitzungskennung
/// synchronisiert ihn also wieder.
///
/// <para>Deshalb hängt <see cref="EngineBrokerRouter"/> an JEDE <c>sessionId</c> (Hintergrund UND Live, beide Broker)
/// die Epoche der Engine: ein fester Anteil je API-Prozess (nach jedem Neustart — dem Hauptauslöser — beginnt der erste
/// Auftrag jeder Engine in einer neuen Sitzung) plus ein Zähler je Engine, den <see cref="ReportForeign"/> hochzählt.
/// Sonst bleibt die Kennung gleich: die Hashtabelle bleibt warm wie bisher, und Aufträge mit eigener Sitzung
/// (flache Tiefen, 0.974.0) behalten ihre eigene.</para>
///
/// <para>Nebenbei der Zähler „in Folge": hilft das Neusynchronisieren dreimal hintereinander nicht, wird die Engine für
/// <see cref="BenchDuration"/> aus dem Hintergrund genommen (<see cref="IsBenched"/>), und der Worker meldet einen Fehler.</para>
/// </summary>
public sealed class EngineSessionEpochs
{
    /// <summary>So viele Erkennungen in Folge (ohne sauberen Lauf dazwischen) machen eine Engine zum Wiederholungstäter.</summary>
    public const int RepeatOffenderStrikes = 3;

    /// <summary>So lange bekommt ein Wiederholungstäter keine Hintergrund-Aufträge.</summary>
    public static readonly TimeSpan BenchDuration = TimeSpan.FromMinutes(5);

    /// <summary>Höchstens ein Fehler-Log je Engine in diesem Abstand.</summary>
    public static readonly TimeSpan ErrorLogInterval = TimeSpan.FromHours(1);

    /// <summary>Längste Kennung, die weitergegeben wird — dieselbe Grenze wie beim Live-Weg
    /// (<see cref="EngineStreamProxy.ValidateWork"/>), damit kein Broker an der Länge scheitert.</summary>
    public const int MaxSessionIdLength = 64;

    private sealed class State
    {
        public int Counter;
        public int InARow;
        public DateTime? LastErrorLog;
        public DateTime? BenchedUntil;
    }

    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);

    /// <summary>Der feste Anteil dieses Prozesses (5 Zeichen a–z0–9, zufällig beim Start).</summary>
    public string ProcessPart { get; }

    public EngineSessionEpochs() : this(NewProcessPart()) { }

    /// <param name="processPart">Für Tests fest vorgegeben; nur a–z/0–9.</param>
    internal EngineSessionEpochs(string processPart) => ProcessPart = processPart;

    private static string NewProcessPart()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> part = stackalloc char[5];
        for (var i = 0; i < part.Length; i++) part[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(part);
    }

    private State StateOf(string engineId) => _states.GetOrAdd(engineId, _ => new State());

    /// <summary>Die Epoche der Engine (<c>e</c> + Prozessanteil + Zähler).</summary>
    public string EpochOf(string engineId)
    {
        var s = StateOf(engineId);
        lock (s) return $"e{ProcessPart}{s.Counter}";
    }

    /// <summary>
    /// Die Sitzungskennung, die zum Provider geht: <c>{sessionId}-{Epoche}</c>. Ist das länger als
    /// <see cref="MaxSessionIdLength"/>, wird die ursprüngliche Kennung vorne gekürzt — die Epoche bleibt immer ganz.
    /// </summary>
    public string SessionFor(string engineId, string sessionId)
    {
        var suffix = "-" + EpochOf(engineId);
        var room = MaxSessionIdLength - suffix.Length;
        var head = sessionId.Length > room ? sessionId[..room] : sessionId;
        return head + suffix;
    }

    /// <summary>Fremde Ausgabe auf der Engine erkannt: Epoche weiter (der nächste Auftrag beginnt mit neuer Sitzung)
    /// und den Zähler „in Folge" erhöhen.</summary>
    public ForeignStrike ReportForeign(string engineId, DateTime nowUtc)
    {
        var s = StateOf(engineId);
        lock (s)
        {
            s.Counter++;
            s.InARow++;
            var repeat = s.InARow >= RepeatOffenderStrikes;
            var log = false;
            if (repeat)
            {
                s.BenchedUntil = nowUtc + BenchDuration;
                if (s.LastErrorLog is not { } last || nowUtc - last >= ErrorLogInterval)
                {
                    s.LastErrorLog = nowUtc;
                    log = true;
                }
            }
            return new ForeignStrike(s.InARow, repeat, log, repeat ? s.BenchedUntil : null);
        }
    }

    /// <summary>Ein sauberer Lauf (Daten, keine fremde Ausgabe): die Folge ist unterbrochen.</summary>
    public void ReportClean(string engineId)
    {
        if (!_states.TryGetValue(engineId, out var s)) return;
        lock (s) s.InARow = 0;
    }

    /// <summary>Darf die Engine gerade keine Hintergrund-Aufträge bekommen (Wiederholungstäter, <see cref="BenchDuration"/>)?</summary>
    public bool IsBenched(string engineId, DateTime nowUtc)
    {
        if (!_states.TryGetValue(engineId, out var s)) return false;
        lock (s) return s.BenchedUntil is { } until && nowUtc < until;
    }
}
