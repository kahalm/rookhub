using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Uebersetzen. Der API-Aufruf ist ausgetauscht — geprueft wird, was um ihn herum passiert, und das
/// ist das Heikle: die QUELLE darf nie ueberschrieben werden, eine Uebersetzung muss sich als
/// solche zu erkennen geben, und ein halber Durchgang darf keinen halben Satz hinterlassen.
/// </summary>
public class CommentTranslationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeClaude _claude = new();
    private readonly CommentTranslationService _svc;

    public CommentTranslationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CommentTranslationService(_db, _claude, NullLogger<CommentTranslationService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Ein Claude, der die Vorlage durchreicht und jede Zeile markiert — so laesst sich
    /// pruefen, WAS er bekommen hat und wohin das Ergebnis wandert.</summary>
    private sealed class FakeClaude : IClaudeJsonClient
    {
        public bool IsConfigured { get; set; } = true;
        public string? Answer { get; set; }
        public List<string> Prompts { get; } = [];
        public string? LastSystem { get; private set; }

        public Task<string?> GenerateHintsJsonAsync(string system, string prompt, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> TranslateCommentsJsonAsync(string system, string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            LastSystem = system;
            if (Answer is not null) return Task.FromResult<string?>(Answer);

            using var doc = JsonDocument.Parse(prompt);
            var items = doc.RootElement.GetProperty("items").EnumerateArray()
                .Select(i => new { ply = i.GetProperty("ply").GetInt32(), text = "DE:" + i.GetProperty("text").GetString() });
            return Task.FromResult<string?>(JsonSerializer.Serialize(new { items }));
        }
    }

    private async Task<int> GameAsync(params string[] comments)
    {
        var analysis = new GameAnalysis { UserId = 1, Pgn = "x", StartFen = "s", TargetDepth = 20, MultiPv = 5 };
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();

        var set = new CommentSet { GameAnalysisId = analysis.Id, Language = "en", Origin = CommentOrigin.Source };
        for (var i = 0; i < comments.Length; i++)
            set.Texts.Add(new CommentText { Ply = i, Text = comments[i] });
        _db.CommentSets.Add(set);
        await _db.SaveChangesAsync();
        return analysis.Id;
    }

    [Fact]
    public async Task Translate_legtEinenEigenenSatzAn()
    {
        var id = await GameAsync("The rook belongs here.", "A quiet move.");

        var written = await _svc.TranslateAsync(id, "de");

        Assert.Equal(2, written);
        var set = await _db.CommentSets.Include(s => s.Texts).SingleAsync(s => s.Language == "de");
        Assert.Equal(CommentOrigin.Machine, set.Origin);
        Assert.Equal("en", set.TranslatedFrom);
        Assert.Equal(CommentTranslationService.ModelName, set.Model);
        Assert.Equal("DE:The rook belongs here.", set.Texts.Single(t => t.Ply == 0).Text);

        // Die Quelle steht unveraendert daneben.
        var source = await _db.CommentSets.Include(s => s.Texts).SingleAsync(s => s.Language == "en");
        Assert.Equal("The rook belongs here.", source.Texts.Single(t => t.Ply == 0).Text);
    }

    /// <summary>Die Figurenbuchstaben sind sprachabhaengig — steht das nicht im Auftrag, werden aus
    /// den Zuegen Buchstabensalat.</summary>
    [Fact]
    public async Task Translate_nenntDieFigurenbuchstabenDerZielsprache()
    {
        var id = await GameAsync("Anything.");

        await _svc.TranslateAsync(id, "de");

        Assert.Contains("K D T L S", _claude.LastSystem);
    }

    /// <summary>Zweimal uebersetzen legt nicht zwei Saetze an — und ueberschreibt auch nichts,
    /// solange niemand ausdruecklich darum bittet.</summary>
    [Fact]
    public async Task Translate_vorhandeneSprache_bleibtStehen()
    {
        var id = await GameAsync("The rook belongs here.");
        await _svc.TranslateAsync(id, "de");

        Assert.Equal(0, await _svc.TranslateAsync(id, "de"));
        Assert.Single(await _db.CommentSets.Where(s => s.Language == "de").ToListAsync());
    }

    [Fact]
    public async Task Translate_mitForce_ersetztDieMaschinenfassung()
    {
        var id = await GameAsync("The rook belongs here.");
        await _svc.TranslateAsync(id, "de");
        _claude.Answer = """{"items":[{"ply":0,"text":"Besser."}]}""";

        var written = await _svc.TranslateAsync(id, "de", force: true);

        Assert.Equal(1, written);
        var set = await _db.CommentSets.Include(s => s.Texts).SingleAsync(s => s.Language == "de");
        Assert.Equal("Besser.", set.Texts.Single().Text);
    }

    /// <summary>Eine QUELLE wird auch mit force nicht ersetzt: sie liesse sich nicht
    /// wiederherstellen.</summary>
    [Fact]
    public async Task Translate_ersetztNiemalsDieQuelle()
    {
        var id = await GameAsync("The rook belongs here.");

        Assert.Equal(0, await _svc.TranslateAsync(id, "en", force: true));
        var set = await _db.CommentSets.Include(s => s.Texts).SingleAsync(s => s.Language == "en");
        Assert.Equal(CommentOrigin.Source, set.Origin);
        Assert.Equal("The rook belongs here.", set.Texts.Single().Text);
    }

    /// <summary>Bricht eine Fuhre ab, entsteht GAR kein Satz — ein halb uebersetzter waere der
    /// schlechtere Zustand: er sieht vollstaendig aus.</summary>
    [Fact]
    public async Task Translate_beiFehlschlag_schreibtNichts()
    {
        var id = await GameAsync("The rook belongs here.");
        _claude.Answer = "kein JSON";

        Assert.Equal(0, await _svc.TranslateAsync(id, "de"));
        Assert.Empty(await _db.CommentSets.Where(s => s.Language == "de").ToListAsync());
    }

    [Fact]
    public async Task Translate_ohneSchluessel_tutNichts()
    {
        var id = await GameAsync("The rook belongs here.");
        _claude.IsConfigured = false;

        Assert.Equal(0, await _svc.TranslateAsync(id, "de"));
        Assert.Empty(_claude.Prompts);
    }

    /// <summary>Eine lange Partie wird in Fuhren geteilt — aber so wenige wie moeglich, weil die
    /// Einheitlichkeit der Begriffe an der gemeinsamen Fuhre haengt.</summary>
    [Fact]
    public async Task Translate_teiltLangePartienAuf()
    {
        var long1 = new string('a', CommentTranslationService.ChunkChars - 10);
        var id = await GameAsync(long1, long1, "kurz");

        await _svc.TranslateAsync(id, "de");

        Assert.Equal(2, _claude.Prompts.Count);
    }
}
