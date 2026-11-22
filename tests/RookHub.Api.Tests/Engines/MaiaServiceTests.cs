using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.Engines;
using Xunit.Abstractions;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Maia-3 auf dem Server (1.003.0, <see cref="MaiaService"/>, <see cref="MaiaEncoding"/>). Die Kodierung läuft immer; die
/// Paritätstests gegen die JS-Fassung des Analysebretts (Fixture <c>Fixtures/maia-parity.json</c>, erzeugt von
/// <c>tools/maia-parity/generate.mjs</c> mit onnxruntime-web) nur, wenn das Modell auf dem Rechner liegt — die CI lädt es nicht
/// (wie die anderen Modell-/Buch-Tests: ohne Datei kehrt der Test still zurück). Lokal: <c>sh src/frontend/app/maia-model/fetch.sh</c>
/// oder <c>sh src/api/RookHub.Api/maia-model/fetch.sh</c>, oder <c>MAIA_MODEL_PATH</c> setzen.
/// </summary>
public class MaiaServiceTests(ITestOutputHelper output)
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml"))) dir = dir.Parent;
        return dir!.FullName;
    }

    /// <summary>Das Modell, falls es auf diesem Rechner liegt, sonst <c>null</c>.</summary>
    internal static string? ModelPath()
    {
        if (Environment.GetEnvironmentVariable("MAIA_MODEL_PATH") is { Length: > 0 } env && File.Exists(env)) return env;
        var root = RepoRoot();
        return new[] { "src/api/RookHub.Api/maia-model/maia3_simplified.onnx", "src/frontend/app/maia-model/maia3_simplified.onnx" }
            .Select(p => Path.Combine(root, p)).FirstOrDefault(File.Exists);
    }

    internal static MaiaService RealService(string path, ILogger<MaiaService>? logger = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Maia:ModelPath"] = path }).Build(),
            logger ?? NullLogger<MaiaService>.Instance);

    // ── Kodierung (immer) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveIndex_wieMaiaEncodingTs()
    {
        Assert.Equal(12 * 64 + 28, MaiaEncoding.MoveIndex("e2e4"));                 // e2 = 12, e4 = 28
        Assert.Equal(4096 + 4 * 32 + 4 * 4 + 0, MaiaEncoding.MoveIndex("e7e8q"));   // Umwandlungsblock
        Assert.Equal(4096 + 4 * 32 + 5 * 4 + 3, MaiaEncoding.MoveIndex("e7f8n"));
        Assert.Equal("e7e5", MaiaEncoding.MirrorUci("e2e4"));
        Assert.Equal("b7b8q", MaiaEncoding.MirrorUci("b2b1q"));
        Assert.Equal("rnbqkbnr/pppp1ppp/8/4p3/8/8/PPPPPPPP/RNBQKBNR",
            MaiaEncoding.MirrorPlacement("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR"));
    }

    [Fact]
    public void Encode_SpiegeltBeiSchwarz_RochadeAlsKoenigszug_UmwandlungVierfach()
    {
        var black = MaiaEncoding.Encode("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1")!;
        Assert.Equal(20, black.Legal.Count);
        // Schwarzer Bauer e7 erscheint gespiegelt als weißer auf e2 (Kanal 0), der weiße e4 als schwarzer auf e5 (Kanal 6).
        Assert.Equal(1, black.Tokens[12 * 12 + 0]);
        Assert.Equal(1, black.Tokens[36 * 12 + 6]);
        Assert.Equal(MaiaEncoding.MoveIndex("e2e4"), black.Indices[black.Legal.ToList().IndexOf("e7e5")]);

        var castle = MaiaEncoding.Encode("r3k2r/ppp2ppp/2n5/3pP3/8/8/PPP2PPP/R3K2R w KQkq d6 0 12")!;
        Assert.Contains("e1g1", castle.Legal);
        Assert.Contains("e1c1", castle.Legal);
        Assert.Contains("e5d6", castle.Legal);   // en passant

        var promo = MaiaEncoding.Encode("8/4P1k1/8/8/8/8/1p4K1/8 w - - 0 60")!;
        Assert.Equal(["e7e8b", "e7e8n", "e7e8q", "e7e8r"], promo.Legal.Where(u => u.StartsWith("e7e8")).Order());
        Assert.Null(MaiaEncoding.Encode("kaputt"));
    }

    [Fact]
    public void Policy_SoftmaxNurUeberLegaleZuege_absteigend()
    {
        var enc = MaiaEncoding.Encode("8/4P1k1/8/8/8/8/1p4K1/8 w - - 0 60")!;
        var logits = new float[MaiaEncoding.MoveCount];
        logits[MaiaEncoding.MoveIndex("e7e8q")] = 5;
        logits[MaiaEncoding.MoveIndex("a1a2")] = 50;   // illegal: zählt nicht
        var policy = MaiaEncoding.Policy(logits, enc);
        Assert.Equal("e7e8q", policy[0].Uci);
        Assert.Equal(1.0, policy.Sum(m => m.P), 6);
        Assert.Equal(enc.Legal.Count, policy.Count);
    }

    // ── Nicht verfügbar → alles wie vorher ─────────────────────────────────────────────────────────

    private sealed class CountingLogger : ILogger<MaiaService>
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Interlocked.Increment(ref Warnings);
        }
    }

    [Fact]
    public void ModellFehlt_nichtVerfuegbar_eineWarnung_keineZuege()
    {
        var log = new CountingLogger();
        var svc = RealService("/nirgends/maia3_simplified.onnx", log);
        Assert.False(svc.Available);
        Assert.Null(svc.Predict("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 1600, 1600));
        Assert.Null(svc.Predict("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 1600, 1600));
        Assert.Equal(1, log.Warnings);
    }

    [Fact]
    public void KaputtesModell_ORTScheitert_nichtVerfuegbar()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maia-kaputt-{Guid.NewGuid():N}.onnx");
        File.WriteAllText(path, "kein onnx");
        try
        {
            var log = new CountingLogger();
            var svc = RealService(path, log);
            Assert.Null(svc.Predict("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 1600, 1600));
            Assert.False(svc.Available);
            Assert.Equal(1, log.Warnings);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Abgeschaltet_perKonfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maia:Enabled"] = "false", ["Maia:ModelPath"] = ModelPath() ?? "/nirgends.onnx",
        }).Build();
        var svc = new MaiaService(config, NullLogger<MaiaService>.Instance);
        Assert.False(svc.Available);
        Assert.Null(svc.Predict("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 1600, 1600));
    }

    // ── Parität mit der JS-Fassung (nur mit Modell) ────────────────────────────────────────────────

    [Fact]
    public void Paritaet_mitDerJsFassung_TopZuegeUndWahrscheinlichkeiten()
    {
        if (ModelPath() is not { } path) return;
        using var svc = RealService(path);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "maia-parity.json")));
        Assert.Equal("405bf76c", doc.RootElement.GetProperty("model").GetString());
        var cases = doc.RootElement.GetProperty("cases").EnumerateArray().ToList();
        Assert.True(cases.Count >= 10);
        foreach (var c in cases)
        {
            var fen = c.GetProperty("fen").GetString()!;
            var policy = svc.Predict(fen, c.GetProperty("eloSelf").GetInt32(), c.GetProperty("eloOppo").GetInt32())!;
            Assert.Equal(c.GetProperty("legal").GetInt32(), policy.Count);
            var top = c.GetProperty("top").EnumerateArray().ToList();
            for (var k = 0; k < top.Count; k++)
            {
                var uci = top[k].GetProperty("uci").GetString()!;
                var p = top[k].GetProperty("p").GetDouble();
                var mine = policy.FirstOrDefault(m => m.Uci == uci);
                Assert.True(mine is not null, $"{fen}: {uci} fehlt");
                // Das Netz rechnet intern in fp16: die Logits beider Laufzeiten liegen auf einem 1/64-Raster und weichen um
                // höchstens ein, zwei Schritte ab (gemessen 0,023) — das sind bis ~2,5 % der Wahrscheinlichkeit.
                Assert.True(Math.Abs(mine!.P - p) <= Math.Max(2e-3, 0.04 * p), $"{fen}: {uci} JS {p:F6}, C# {mine.P:F6}");
                // Gleiche Reihenfolge, sofern die Nachbarn nicht praktisch gleich auf sind.
                var neighbours = top.Where((t, j) => j != k && Math.Abs(t.GetProperty("p").GetDouble() - p) <= 0.05 * p).Any();
                if (p > 0.01 && !neighbours) Assert.Equal(uci, policy[k].Uci);
            }
        }
    }

    [Fact]
    public void Vereinspartie259_nach44g3_h3istNatuerlich_LaufzeitJeAufruf()
    {
        if (ModelPath() is not { } path) return;
        using var svc = RealService(path);
        const string fen = "8/1b6/p5kb/8/2NB2pp/P5P1/1P6/5K2 b - - 0 44";
        var policy = svc.Predict(fen, 1600, 1600)!;
        double P(string uci) => policy.First(m => m.Uci == uci).P;
        output.WriteLine($"1600: h4h3 {P("h4h3"):P1}, h4g3 {P("h4g3"):P1}, b7e4 {P("b7e4"):P2}");
        Assert.True(P("h4h3") > 0.5);
        Assert.True(P("h4g3") > 0.15);
        Assert.True(P("b7e4") < 0.05);

        // Laufzeit je Aufruf (ohne Zwischenspeicher: jedes Mal eine andere Stärke).
        var watch = Stopwatch.StartNew();
        const int n = 20;
        for (var i = 0; i < n; i++) svc.Predict(fen, 1000 + 50 * i, 1600);
        output.WriteLine($"Maia je Aufruf: {watch.Elapsed.TotalMilliseconds / n:F1} ms (1 Thread)");
    }
}
