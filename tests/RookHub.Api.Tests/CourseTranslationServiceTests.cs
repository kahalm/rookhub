using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Kurs-Uebersetzung je Linie (<see cref="CourseTranslationService"/>). Das Modell ist ausgetauscht
/// (<see cref="FakeCourseTranslator"/>); geprueft wird, was um den Aufruf herum passiert: welche Texte ans
/// Modell gehen (nur fehlende/veraltete, gleiche nur einmal, Vorhandenes wird kopiert), wohin das Ergebnis
/// wandert (Stellen + Fingerabdruck) und dass ein Fehlschlag nichts schreibt.
/// </summary>
public class CourseTranslationServiceTests : IDisposable
{
    private readonly CourseTranslationTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private static readonly Dictionary<int, string> Moves = new()
    {
        [-1] = "Einleitung zur Linie",
        [0] = "Zug eins, gut",
        [2] = "Idee mit Nf3",
    };

    // ── Eine Linie ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TranslateLine_UebersetztAlleStellen_MitFingerabdruckUndHerkunft()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", title: "Titel A", chapter: "Kapitel A",
            comment: "Erklaerung zur Linie", moveComments: Moves);

        var result = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(LineTranslationStatus.Written, result.Status);
        var texts = await _kit.TextsAsync(line.Id, "de");
        Assert.Equal(new[] { -4, -3, -2, -1, 0, 2 }, texts.Keys.OrderBy(k => k));
        Assert.Equal("DE:Kapitel A", texts[CourseTextSlots.Chapter].Text);
        Assert.Equal("DE:Titel A", texts[CourseTextSlots.Title].Text);
        Assert.Equal("DE:Erklaerung zur Linie", texts[CourseTextSlots.Comment].Text);
        Assert.Equal("DE:Einleitung zur Linie", texts[CourseTextSlots.Intro].Text);
        Assert.Equal(CourseTextHash.Of("Kapitel A"), texts[CourseTextSlots.Chapter].SourceHash);
        Assert.Equal(CourseTextHash.Of("Zug eins, gut"), texts[0].SourceHash);

        var set = await _kit.Db().CommentSets.SingleAsync(s => s.BookPuzzleId == line.Id);
        Assert.Equal(CommentOrigin.Machine, set.Origin);
        Assert.Equal("en", set.TranslatedFrom);
        Assert.Equal("test-modell", set.Model);
        Assert.Null(set.LibraryGameId);
        Assert.Null(set.GameAnalysisId);
        // Die Quelle bleibt die Linie — sie wird nie angefasst.
        var stored = await _kit.Db().BookPuzzles.SingleAsync(bp => bp.Id == line.Id);
        Assert.Equal("Erklaerung zur Linie", stored.Comment);
        Assert.Equal("Kapitel A", stored.Chapter);
    }

    /// <summary>In mehr als der Haelfte der Prod-Linien steht der Kommentar woertlich auch als Einleitung —
    /// ans Modell geht er EINMAL, beide Stellen bekommen dieselbe Uebersetzung.</summary>
    [Fact]
    public async Task TranslateLine_GleicherTextInKommentarUndEinleitung_EinEintragZweiStellen()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", comment: "Derselbe Text",
            moveComments: new Dictionary<int, string> { [-1] = "Derselbe Text", [0] = "Anderes" });

        await _kit.Service().TranslateLineAsync(line.Id, "de");

        var call = Assert.Single(_kit.Llm.Calls);
        Assert.Equal(2, call.Items.Count);
        Assert.Single(call.Items, i => i.Text == "Derselbe Text");
        var texts = await _kit.TextsAsync(line.Id, "de");
        Assert.Equal("DE:Derselbe Text", texts[CourseTextSlots.Comment].Text);
        Assert.Equal("DE:Derselbe Text", texts[CourseTextSlots.Intro].Text);
    }

    [Fact]
    public async Task TranslateLine_ZweiterLaufOhneAenderung_KeinModellaufruf()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", title: "Titel", comment: "Kommentar", moveComments: Moves);
        await _kit.Service().TranslateLineAsync(line.Id, "de");
        var before = _kit.Llm.Calls.Count;

        var again = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(LineTranslationStatus.NothingToDo, again.Status);
        Assert.Equal(before, _kit.Llm.Calls.Count);
    }

    /// <summary>Die Aufbereitung ueberschreibt Kommentare in-place — der naechste Lauf uebersetzt GENAU den
    /// geaenderten Text, der Rest bleibt, wie er ist.</summary>
    [Fact]
    public async Task TranslateLine_GeaenderterKommentar_NurDieserTextNeu()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", title: "Titel", comment: "Alter Kommentar", moveComments: Moves);
        await _kit.Service().TranslateLineAsync(line.Id, "de");
        var db = _kit.Db();
        var stored = await db.BookPuzzles.SingleAsync(bp => bp.Id == line.Id);
        stored.Comment = "Neuer Kommentar";
        await db.SaveChangesAsync();
        var before = _kit.Llm.Calls.Count;

        var result = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(before + 1, _kit.Llm.Calls.Count);
        var call = _kit.Llm.Calls[^1];
        Assert.Equal(new[] { (CourseTextSlots.Comment, "Neuer Kommentar") }, call.Items);
        Assert.Equal(1, result.Translated);
        var texts = await _kit.TextsAsync(line.Id, "de");
        Assert.Equal("DE:Neuer Kommentar", texts[CourseTextSlots.Comment].Text);
        Assert.Equal(CourseTextHash.Of("Neuer Kommentar"), texts[CourseTextSlots.Comment].SourceHash);
        Assert.Equal("DE:Titel", texts[CourseTextSlots.Title].Text);
    }

    [Fact]
    public async Task TranslateLine_VorlageVerschwunden_UebersetzungWirdEntfernt()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", title: "Titel", comment: "Kommentar");
        await _kit.Service().TranslateLineAsync(line.Id, "de");
        var db = _kit.Db();
        var stored = await db.BookPuzzles.SingleAsync(bp => bp.Id == line.Id);
        stored.Comment = null;
        await db.SaveChangesAsync();
        var before = _kit.Llm.Calls.Count;

        var result = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(before, _kit.Llm.Calls.Count);   // nichts zu uebersetzen, nur aufzuraeumen
        Assert.Equal(1, result.Removed);
        var texts = await _kit.TextsAsync(line.Id, "de");
        Assert.False(texts.ContainsKey(CourseTextSlots.Comment));
        Assert.True(texts.ContainsKey(CourseTextSlots.Title));
    }

    /// <summary>Ein halb uebersetzter Satz saehe vollstaendig aus — ein Fehlschlag schreibt fuer die Linie NICHTS.</summary>
    [Fact]
    public async Task TranslateLine_Fehlschlag_SchreibtNichts()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", title: "Titel", comment: "Kommentar", moveComments: Moves);
        _kit.Llm.Fail = true;

        var result = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(LineTranslationStatus.Failed, result.Status);
        Assert.Empty(await _kit.Db().CommentSets.ToListAsync());
    }

    /// <summary>Beantwortet das Modell nicht JEDEN Eintrag, ist es ein Fehlschlag — auch wenn der Rest da ist.</summary>
    [Fact]
    public async Task TranslateLine_ModellLaesstEinenEintragWeg_SchreibtNichts()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", title: "Titel", comment: "Kommentar");
        _kit.Llm.Answer = """{"items":[{"ply":-3,"text":"Nur der Titel"}]}""";

        var result = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(LineTranslationStatus.Failed, result.Status);
        Assert.Empty(await _kit.Db().CommentSets.ToListAsync());
    }

    /// <summary>Derselbe Text in einem ANDEREN Kurs (Doppel-Import, <c>_firstkey</c>-Kopie) ist schon uebersetzt —
    /// dann wird kopiert statt noch einmal bezahlt.</summary>
    [Fact]
    public async Task TranslateLine_TextSchonInAnderemKurs_WirdKopiert_KeinModellaufruf()
    {
        var first = await _kit.SeedBookAsync("en", "Kurs A");
        var original = await _kit.SeedLineAsync(first, "001", title: "Titel", chapter: "Kapitel", comment: "Kommentar",
            moveComments: Moves);
        await _kit.Service().TranslateLineAsync(original.Id, "de");
        var second = await _kit.SeedBookAsync("en", "Kurs A (Kopie)");
        var copy = await _kit.SeedLineAsync(second, "001", title: "Titel", chapter: "Kapitel", comment: "Kommentar",
            moveComments: Moves);
        var before = _kit.Llm.Calls.Count;

        var result = await _kit.Service().TranslateLineAsync(copy.Id, "de");

        Assert.Equal(before, _kit.Llm.Calls.Count);
        Assert.Equal(LineTranslationStatus.Written, result.Status);
        Assert.Equal(0, result.Translated);
        Assert.Equal(6, result.Reused);
        var texts = await _kit.TextsAsync(copy.Id, "de");
        Assert.Equal("DE:Kommentar", texts[CourseTextSlots.Comment].Text);
        Assert.Equal("DE:Idee mit Sf3", texts[2].Text);
    }

    /// <summary>Die Figurenbuchstaben rechnet der Kern um, nicht das Modell (siehe PieceLetters).</summary>
    [Fact]
    public async Task TranslateLine_StelltFigurenbuchstabenAufZielspracheUm()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", moveComments: new Dictionary<int, string> { [2] = "Idee mit Nf3" });

        await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal("DE:Idee mit Sf3", (await _kit.TextsAsync(line.Id, "de"))[2].Text);
        Assert.Contains("K D T L S", _kit.Llm.Calls[0].System);
    }

    /// <summary>Quelle „und" (nicht bestimmbar): der Auftrag behauptet keine Sprache, und die Buchstaben bleiben.</summary>
    [Fact]
    public async Task TranslateLine_UnbestimmteQuelle_AuftragOhneSprachkuerzel_BuchstabenBleiben()
    {
        var book = await _kit.SeedBookAsync("und");
        var line = await _kit.SeedLineAsync(book, "001", moveComments: new Dictionary<int, string> { [2] = "Idee mit Nf3" });

        await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Contains("from the language it is written in", _kit.Llm.Calls[0].System);
        Assert.DoesNotContain("from und", _kit.Llm.Calls[0].System);
        Assert.Equal("DE:Idee mit Nf3", (await _kit.TextsAsync(line.Id, "de"))[2].Text);
    }

    [Fact]
    public async Task TranslateLine_ZielGleichQuelle_TutNichts()
    {
        var book = await _kit.SeedBookAsync("de");
        var line = await _kit.SeedLineAsync(book, "001", comment: "Kommentar");

        var result = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(LineTranslationStatus.SameLanguage, result.Status);
        Assert.Empty(_kit.Llm.Calls);
    }

    [Fact]
    public async Task TranslateLine_OhneModell_NotConfigured_SchreibtNichts()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", comment: "Kommentar");
        _kit.Llm.IsConfigured = false;

        var result = await _kit.Service().TranslateLineAsync(line.Id, "de");

        Assert.Equal(LineTranslationStatus.NotConfigured, result.Status);
        Assert.Empty(await _kit.Db().CommentSets.ToListAsync());
    }

    // ── Quellsprache ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureSourceLanguage_BestimmtUndMerktSie()
    {
        var book = await _kit.SeedBookAsync(commentLanguage: null);
        await _kit.SeedLineAsync(book, "001",
            comment: "The white knight is better than the black bishop, and the position is very good now.",
            moveComments: new Dictionary<int, string> { [0] = "This move is not the best, but it has a clear idea." });

        var lang = await _kit.Service().EnsureSourceLanguageAsync(book.Id);

        Assert.Equal("en", lang);
        Assert.Equal("en", (await _kit.Db().Books.SingleAsync(b => b.Id == book.Id)).CommentLanguage);
    }

    [Fact]
    public async Task EnsureSourceLanguage_NichtBestimmbar_Und()
    {
        var book = await _kit.SeedBookAsync(commentLanguage: null);
        await _kit.SeedLineAsync(book, "001", comment: "!? +- Nf3");

        Assert.Equal("und", await _kit.Service().EnsureSourceLanguageAsync(book.Id));
    }

    [Fact]
    public async Task EnsureSourceLanguage_VorhandeneAngabeBleibt()
    {
        var book = await _kit.SeedBookAsync(commentLanguage: "fr");
        await _kit.SeedLineAsync(book, "001", comment: "The white knight is better than the black bishop and the rook.");

        Assert.Equal("fr", await _kit.Service().EnsureSourceLanguageAsync(book.Id));
    }

    // ── Ein Kurs ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Alle Kapitelnamen gehen in EINER Fuhre ans Modell (Einheitlichkeit) — und nicht noch einmal
    /// mit den Linien; jede Linie traegt danach die Uebersetzung ihres Kapitels.</summary>
    [Fact]
    public async Task TranslateCourse_KapitelnamenInEinerFuhre()
    {
        var book = await _kit.SeedBookAsync("en");
        var a1 = await _kit.SeedLineAsync(book, "001", chapter: "Kapitel A", comment: "Kommentar eins");
        var a2 = await _kit.SeedLineAsync(book, "002", chapter: "Kapitel A", comment: "Kommentar zwei");
        var b1 = await _kit.SeedLineAsync(book, "003", chapter: "Kapitel B", comment: "Kommentar drei");

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de");

        Assert.Equal(CourseTranslationRunStatus.Done, run.Status);
        Assert.Equal(3, run.LinesDone);
        Assert.Equal(2, run.ChaptersTotal);
        Assert.Equal(0, run.ChaptersMissing);
        var chapterCalls = _kit.Llm.Calls.Where(c => c.System.Contains("name of one chapter")).ToList();
        var chapterCall = Assert.Single(chapterCalls);
        Assert.Equal(new[] { "Kapitel A", "Kapitel B" }, chapterCall.Items.Select(i => i.Text).OrderBy(t => t));
        Assert.All(_kit.Llm.Calls.Except(chapterCalls), c => Assert.DoesNotContain(c.Items, i => i.Text.StartsWith("Kapitel")));
        Assert.Equal("DE:Kapitel A", (await _kit.TextsAsync(a1.Id, "de"))[CourseTextSlots.Chapter].Text);
        Assert.Equal("DE:Kapitel A", (await _kit.TextsAsync(a2.Id, "de"))[CourseTextSlots.Chapter].Text);
        Assert.Equal("DE:Kapitel B", (await _kit.TextsAsync(b1.Id, "de"))[CourseTextSlots.Chapter].Text);
    }

    /// <summary>Scheitert die Kapitel-Fuhre, werden die Linien trotzdem uebersetzt — nur ihr Kapitel-Slot bleibt
    /// offen, und der naechste Lauf versucht genau die Kapitel noch einmal (ohne die Linien neu zu uebersetzen).</summary>
    [Fact]
    public async Task TranslateCourse_KapitelFuhreScheitert_LinienTrotzdem_KapitelBleibtOffen()
    {
        var book = await _kit.SeedBookAsync("en");
        var line = await _kit.SeedLineAsync(book, "001", chapter: "Kapitel A", comment: "Kommentar eins");
        _kit.Llm.FailIfSystemContains = "name of one chapter";

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de");

        Assert.Equal(1, run.LinesDone);
        Assert.Equal(1, run.ChaptersMissing);
        var texts = await _kit.TextsAsync(line.Id, "de");
        Assert.True(texts.ContainsKey(CourseTextSlots.Comment));
        Assert.False(texts.ContainsKey(CourseTextSlots.Chapter));

        _kit.Llm.FailIfSystemContains = null;
        var before = _kit.Llm.Calls.Count;
        var again = await _kit.Service().TranslateCourseAsync(book.Id, "de");

        Assert.Equal(1, again.LinesTotal);
        Assert.Equal(0, again.ChaptersMissing);
        Assert.Equal(before + 1, _kit.Llm.Calls.Count);   // nur die Kapitel-Fuhre
        Assert.Equal("DE:Kapitel A", (await _kit.TextsAsync(line.Id, "de"))[CourseTextSlots.Chapter].Text);
    }

    [Fact]
    public async Task TranslateCourse_ZielGleichQuelle_TutNichts()
    {
        var book = await _kit.SeedBookAsync("de");
        await _kit.SeedLineAsync(book, "001", comment: "Kommentar");

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de");

        Assert.Equal(CourseTranslationRunStatus.SameLanguage, run.Status);
        Assert.Empty(_kit.Llm.Calls);
        Assert.Empty(await _kit.Db().CommentSets.ToListAsync());
    }

    [Fact]
    public async Task TranslateCourse_BestimmtQuellspracheBeimErstenLauf()
    {
        var book = await _kit.SeedBookAsync(commentLanguage: null);
        await _kit.SeedLineAsync(book, "001",
            comment: "The white knight is better than the black bishop, and the position is very good now.");

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "fr");

        Assert.Equal("en", run.SourceLanguage);
        Assert.Equal("en", (await _kit.Db().Books.SingleAsync(b => b.Id == book.Id)).CommentLanguage);
    }

    [Fact]
    public async Task TranslateCourse_ZweiterLauf_NichtsZuTun_KeinModellaufruf()
    {
        var book = await _kit.SeedBookAsync("en");
        await _kit.SeedLineAsync(book, "001", chapter: "Kapitel", comment: "Kommentar eins");
        await _kit.SeedLineAsync(book, "002", chapter: "Kapitel", title: "Titel");
        await _kit.Service().TranslateCourseAsync(book.Id, "de");
        var before = _kit.Llm.Calls.Count;

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de");

        Assert.Equal(CourseTranslationRunStatus.NothingToDo, run.Status);
        Assert.Equal(before, _kit.Llm.Calls.Count);
    }

    /// <summary>Abbruch (Sperrzeit, Dienst stoppt): die laufende Linie wird verworfen, das Fertige bleibt — der
    /// naechste Lauf macht nur den Rest.</summary>
    [Fact]
    public async Task TranslateCourse_Abbruch_FertigesBleibt_NaechsterLaufMachtDenRest()
    {
        var book = await _kit.SeedBookAsync("en");
        var l1 = await _kit.SeedLineAsync(book, "001", comment: "Kommentar eins");
        var l2 = await _kit.SeedLineAsync(book, "002", comment: "Kommentar zwei");
        var l3 = await _kit.SeedLineAsync(book, "003", comment: "Kommentar drei");
        using var cts = new CancellationTokenSource();
        _kit.Llm.CancelAt = (2, cts);   // die zweite Linie (keine Kapitel → kein Kapitelaufruf davor)

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _kit.Service().TranslateCourseAsync(book.Id, "de", ct: cts.Token));

        Assert.NotEmpty(await _kit.TextsAsync(l1.Id, "de"));
        Assert.Empty(await _kit.TextsAsync(l2.Id, "de"));
        Assert.Empty(await _kit.TextsAsync(l3.Id, "de"));

        _kit.Llm.CancelAt = null;
        var before = _kit.Llm.Calls.Count;
        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de");

        Assert.Equal(2, run.LinesTotal);
        Assert.Equal(2, run.LinesDone);
        Assert.Equal(before + 2, _kit.Llm.Calls.Count);
        Assert.NotEmpty(await _kit.TextsAsync(l2.Id, "de"));
        Assert.NotEmpty(await _kit.TextsAsync(l3.Id, "de"));
    }

    [Fact]
    public async Task TranslateCourse_MeldetFortschritt_UndZaehltGescheiterte()
    {
        var book = await _kit.SeedBookAsync("en");
        for (var i = 1; i <= 12; i++)
            await _kit.SeedLineAsync(book, i.ToString("000"), comment: $"Kommentar {i}");
        var reports = new List<CourseTranslationProgress>();

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de",
            (p, _) => { lock (reports) reports.Add(p); return Task.CompletedTask; });

        Assert.Equal(12, run.LinesTotal);
        Assert.Equal(12, run.LinesDone);
        Assert.Equal(0, run.LinesFailed);
        Assert.Contains(reports, r => r.LinesDone + r.LinesFailed == CourseTranslationService.ProgressEvery);
        Assert.Equal(new CourseTranslationProgress(12, 12, 0), reports[^1]);

        // Scheitert das Modell, zaehlt der Lauf die Linien als gescheitert und schreibt nichts.
        var other = await _kit.SeedBookAsync("en", "Zweiter Kurs");
        await _kit.SeedLineAsync(other, "001", comment: "Etwas anderes");
        _kit.Llm.Fail = true;
        var failed = await _kit.Service().TranslateCourseAsync(other.Id, "de");
        Assert.Equal(1, failed.LinesFailed);
        Assert.Equal(0, failed.LinesDone);
    }

    /// <summary>Linien mit gemeinsamem Zuganfang tragen dieselben Kommentare und laufen gleichzeitig — trotzdem geht
    /// jeder verschiedene Text je Lauf genau EINMAL ans Modell (Phase 1: er gehoert der ersten Linie), die Nachbarlinie
    /// bekommt ihn in Phase 2 per Wiederverwendung.</summary>
    [Fact]
    public async Task TranslateCourse_GleicherTextInZweiLinien_Parallel_GehtGenauEinmalAnsModell()
    {
        var book = await _kit.SeedBookAsync("en");
        var shared = new Dictionary<int, string> { [0] = "Gemeinsamer Zugkommentar" };
        var first = await _kit.SeedLineAsync(book, "001", title: "Titel Eins", comment: "Gemeinsamer Kommentar",
            moveComments: shared);
        var second = await _kit.SeedLineAsync(book, "002", title: "Titel Zwei", comment: "Gemeinsamer Kommentar",
            moveComments: shared);
        _kit.Llm.Delay = TimeSpan.FromMilliseconds(150);   // beide Linien sind gleichzeitig beim Modell

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de", parallel: 2);

        Assert.Equal(2, run.LinesDone);
        var items = _kit.Llm.Calls.SelectMany(c => c.Items).ToList();
        Assert.Single(items, i => i.Text == "Gemeinsamer Kommentar");
        Assert.Single(items, i => i.Text == "Gemeinsamer Zugkommentar");
        foreach (var line in new[] { first, second })
        {
            var texts = await _kit.TextsAsync(line.Id, "de");
            Assert.Equal("DE:Gemeinsamer Kommentar", texts[CourseTextSlots.Comment].Text);
            Assert.Equal("DE:Gemeinsamer Zugkommentar", texts[0].Text);
        }
        Assert.Equal("DE:Titel Zwei", (await _kit.TextsAsync(second.Id, "de"))[CourseTextSlots.Title].Text);
    }

    /// <summary>Scheitert die Besitzer-Linie eines gemeinsamen Textes in Phase 1, uebersetzt Phase 2 ihn fuer die
    /// Nachbarlinie — die bleibt nicht auf ewig halb.</summary>
    [Fact]
    public async Task TranslateCourse_BesitzerLinieScheitert_Phase2UebersetztDenText()
    {
        var book = await _kit.SeedBookAsync("en");
        var owner = await _kit.SeedLineAsync(book, "001", title: "Titel Eins", comment: "Gemeinsamer Kommentar");
        var neighbour = await _kit.SeedLineAsync(book, "002", title: "Titel Zwei", comment: "Gemeinsamer Kommentar");
        _kit.Llm.FailIfPromptContains = "Titel Eins";   // jede Anfrage der Besitzer-Linie scheitert

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de", parallel: 2);

        Assert.Equal(2, run.LinesTotal);
        Assert.Equal(1, run.LinesDone);
        Assert.Equal(1, run.LinesFailed);
        Assert.Empty(await _kit.TextsAsync(owner.Id, "de"));
        var texts = await _kit.TextsAsync(neighbour.Id, "de");
        Assert.Equal("DE:Gemeinsamer Kommentar", texts[CourseTextSlots.Comment].Text);
        Assert.Equal("DE:Titel Zwei", texts[CourseTextSlots.Title].Text);
    }

    /// <summary>Mehrere Linien gleichzeitig, jede mit eigenem Scope/DbContext — alle landen, keine doppelt.</summary>
    [Fact]
    public async Task TranslateCourse_Parallel_JedeLinieGenauEinmal()
    {
        var book = await _kit.SeedBookAsync("en");
        var ids = new List<int>();
        for (var i = 1; i <= 20; i++)
            ids.Add((await _kit.SeedLineAsync(book, i.ToString("000"), chapter: i % 2 == 0 ? "Gerade" : "Ungerade",
                comment: $"Kommentar {i}")).Id);

        var run = await _kit.Service().TranslateCourseAsync(book.Id, "de", parallel: 6);

        Assert.Equal(20, run.LinesDone);
        var sets = await _kit.Db().CommentSets.Where(s => s.BookPuzzleId != null).ToListAsync();
        Assert.Equal(20, sets.Count);
        Assert.Equal(ids.OrderBy(i => i), sets.Select(s => s.BookPuzzleId!.Value).OrderBy(i => i));
    }

    [Fact]
    public async Task TranslateCourse_UngueltigeSprache_OderOhneModell()
    {
        var book = await _kit.SeedBookAsync("en");
        await _kit.SeedLineAsync(book, "001", comment: "Kommentar");

        Assert.Equal(CourseTranslationRunStatus.InvalidLanguage,
            (await _kit.Service().TranslateCourseAsync(book.Id, "de; drop")).Status);
        _kit.Llm.IsConfigured = false;
        Assert.Equal(CourseTranslationRunStatus.NotConfigured,
            (await _kit.Service().TranslateCourseAsync(book.Id, "de")).Status);
        Assert.Empty(_kit.Llm.Calls);
    }

    // ── Quelle je Stelle ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceSlots_BelegtDieStellenNachKonvention_UndLaesstLeeresWeg()
    {
        var slots = CourseTranslationService.SourceSlots("Titel", "  ", "Kommentar\r\nzweite Zeile ",
            """{"-1":"Einleitung","0":"","3":"Nach dem vierten Halbzug"}""");

        Assert.Equal(new[] { -3, -2, -1, 3 }, slots.Keys.OrderBy(k => k));
        Assert.Equal("Kommentar\nzweite Zeile", slots[CourseTextSlots.Comment].Text);
        Assert.Equal(CourseTextHash.Of("Kommentar\r\nzweite Zeile"), slots[CourseTextSlots.Comment].Hash);
    }

    [Fact]
    public void CourseTextHash_IstStabil_UndNormalisiert()
    {
        Assert.Equal(16, CourseTextHash.Of("x").Length);
        Assert.Equal(CourseTextHash.Of("Text"), CourseTextHash.Of("  Text\n"));
        Assert.Equal(CourseTextHash.Of("a\nb"), CourseTextHash.Of("a\r\nb"));
        Assert.NotEqual(CourseTextHash.Of("Text"), CourseTextHash.Of("Text."));
        // Festgenagelt: ein geaenderter Algorithmus machte jede gespeicherte Uebersetzung „veraltet".
        Assert.Equal("7284227d681d1b17", CourseTextHash.Of("Kommentar"));
    }
}
