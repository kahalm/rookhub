using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Maia-3 auf dem SERVER (1.003.0, Nutzer 2026-11-22: „bau maia ein“): sagt voraus, welchen Zug ein Mensch einer bestimmten
/// Stärke spielt — kein Rechner, ein neuronales Netz (CSSLab, GPL-3.0, dasselbe Modell wie im Analysebrett, siehe
/// <c>docs/api/maia-sparring-im-analysebrett.md</c>). Genutzt von den Fehler-Erklärungen (<see cref="MaiaNaturalMoves"/>): die
/// natürlichen Züge an den Entscheidungsstellen, nicht nur Schläge der gezogenen Figur.
///
/// <list type="bullet">
/// <item><b>Modell</b>: <c>Maia:ModelPath</c> (Vorgabe <c>maia/maia3_simplified.onnx</c> neben der Anwendung — das API-Image
/// legt es dort ab, <c>maia-model/fetch.sh</c> mit demselben Pin wie das Frontend).</item>
/// <item><b>Aus</b> mit <c>Maia:Enabled=false</c>. Fehlt das Modell oder scheitert die ONNX-Laufzeit, ist der Dienst
/// „nicht verfügbar“ (<see cref="Available"/> falsch, EINE Warnung im Log) — die Erklärungen laufen dann wie vorher.</item>
/// <item><b>CPU sparsam</b>: <c>Maia:Threads</c> (Vorgabe 1, höchstens 4) für die Laufzeit und nur EIN Aufruf gleichzeitig —
/// der Host teilt sich die CPU mit Jellyfin und den Engine-Providern. Geladen wird erst beim ersten Aufruf (~150 MB).</item>
/// <item><b>Zwischenspeicher</b>: die letzten <see cref="CacheSize"/> Antworten je (Stellung, Stärken) — dieselbe Stellung
/// wird beim Planen und beim Erklären gefragt.</item>
/// </list>
/// Eingabe und Ausgabe genau wie <c>maia-worker.js</c>: <c>tokens</c> [1,64,12], <c>elo_self</c>/<c>elo_oppo</c> [1] als rohe
/// Elo, Ausgabe <c>logits_move</c> (4352); Kodierung in <see cref="MaiaEncoding"/>.
/// </summary>
public class MaiaService : IDisposable
{
    public const string DefaultModelPath = "maia/maia3_simplified.onnx";
    public const int MinElo = 600;
    public const int MaxElo = 2600;
    internal const int CacheSize = 512;

    private readonly ILogger<MaiaService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<MaiaMove>> _cache = new();
    private readonly Queue<string> _order = new();
    private readonly string _path;
    private readonly int _threads;
    private InferenceSession? _session;
    private bool _failed;

    public bool Enabled { get; }
    public string ModelPath => _path;
    /// <summary>Aufschlag auf die Vereins-/FIDE-Elo, bevor sie an Maia geht (<c>Maia:EloOffset</c>, Vorgabe
    /// <see cref="MaiaNaturalMoves.DefaultEloOffset"/> = 200, −400..600): Maia kennt Lichess-Wertungen (1.003.1).</summary>
    public int EloOffset { get; }

    /// <summary>Dauer des letzten Netz-Aufrufs (ohne Zwischenspeicher) — für die Messung in den Tests und im Log.</summary>
    public TimeSpan LastRun { get; private set; }

    public MaiaService(IConfiguration? config, ILogger<MaiaService> logger)
    {
        _logger = logger;
        Enabled = config?.GetValue<bool?>("Maia:Enabled") ?? true;
        var path = config?["Maia:ModelPath"] is { Length: > 0 } p ? p.Trim() : DefaultModelPath;
        _path = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        _threads = Math.Clamp(config?.GetValue<int?>("Maia:Threads") ?? 1, 1, 4);
        EloOffset = Math.Clamp(config?.GetValue<int?>("Maia:EloOffset") ?? MaiaNaturalMoves.DefaultEloOffset, -400, 600);
    }

    /// <summary>An und (soweit bekannt) brauchbar: eingeschaltet, Modell da, die Laufzeit ist nicht gescheitert.</summary>
    public virtual bool Available => Enabled && !_failed && (_session != null || File.Exists(_path));

    /// <summary>
    /// Die legalen Züge der Stellung mit Wahrscheinlichkeit (Summe 1, absteigend) für einen Spieler der Stärke
    /// <paramref name="eloSelf"/> (am Zug) gegen <paramref name="eloOppo"/>; Elo auf <see cref="MinElo"/>..<see cref="MaxElo"/>
    /// begrenzt. <c>null</c> = nicht verfügbar oder unlesbare Stellung; leer = kein legaler Zug.
    /// </summary>
    public virtual IReadOnlyList<MaiaMove>? Predict(string fen, int eloSelf, int eloOppo)
    {
        if (!Enabled || _failed) return null;
        eloSelf = Math.Clamp(eloSelf, MinElo, MaxElo);
        eloOppo = Math.Clamp(eloOppo, MinElo, MaxElo);
        var key = $"{string.Join(' ', fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4))}|{eloSelf}|{eloOppo}";
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit)) return hit;
            if (MaiaEncoding.Encode(fen) is not { } enc) return null;
            if (enc.Legal.Count == 0) return [];
            if (Session() is not { } session) return null;
            try
            {
                var watch = Stopwatch.StartNew();
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("tokens", new DenseTensor<float>(enc.Tokens, [1, 64, 12])),
                    NamedOnnxValue.CreateFromTensor("elo_self", new DenseTensor<float>(new float[] { eloSelf }, [1])),
                    NamedOnnxValue.CreateFromTensor("elo_oppo", new DenseTensor<float>(new float[] { eloOppo }, [1])),
                };
                using var results = session.Run(inputs, ["logits_move"]);
                var logits = results.First().AsTensor<float>().ToArray();
                LastRun = watch.Elapsed;
                var policy = MaiaEncoding.Policy(logits, enc);
                _cache[key] = policy;
                _order.Enqueue(key);
                while (_order.Count > CacheSize) _cache.Remove(_order.Dequeue());
                return policy;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or InvalidOperationException or ArgumentException)
            {
                _logger.LogWarning("Maia: Aufruf gescheitert ({Message}) — natürliche Züge bleiben aus", ex.Message);
                return null;
            }
        }
    }

    /// <summary>Die Sitzung, beim ersten Mal geladen (unter <see cref="_gate"/>). Fehlt das Modell oder scheitert die Laufzeit:
    /// einmal warnen, dann bis zum Neustart aus.</summary>
    private InferenceSession? Session()
    {
        if (_session != null) return _session;
        if (_failed) return null;
        if (!File.Exists(_path))
        {
            _failed = true;
            _logger.LogWarning("Maia: Modell fehlt unter {Path} — Fehler-Erklärungen ohne natürliche Züge", _path);
            return null;
        }
        try
        {
            var watch = Stopwatch.StartNew();
            // NICHT ORT_ENABLE_ALL (die Vorgabe): mit ORT 1.31.0 stürzt das Laden dieses Modells dann mit einem Segfault ab
            // (22.11.2026 nachgestellt, 0/1/2 laufen) — ein Absturz der ganzen API, den kein catch fängt.
            var options = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                IntraOpNumThreads = _threads, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
            };
            _session = new InferenceSession(_path, options);
            _logger.LogInformation("Maia: Modell geladen ({Path}, {Ms} ms, {Threads} Thread(s))", _path, watch.ElapsedMilliseconds, _threads);
            return _session;
        }
        catch (Exception ex)
        {
            _failed = true;
            _logger.LogWarning("Maia: ONNX-Laufzeit scheitert ({Message}) — Fehler-Erklärungen ohne natürliche Züge", ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }
        GC.SuppressFinalize(this);
    }
}
