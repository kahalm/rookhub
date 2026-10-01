using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Keine native Rückfrage in den Oberflächen (Codereview 2026-09-29, W5 F8-005/F4-013) — als Quellscan, weil das
/// Frontend keine ESLint-Regel (<c>no-restricted-globals</c>) hat. <c>window.confirm</c> liegt im Vollbild hinter der
/// Seite, lässt sich im Browser für die Seite abschalten („keine weiteren Dialoge“, danach liefert es still
/// <c>false</c> und Löschen tut nichts) und zeigt die Knöpfe in der Browsersprache statt in der App-Sprache. Jede
/// Rückfrage läuft deshalb über den <c>ConfirmService</c> (<c>src/app/shared/confirm-dialog</c>), in allen
/// Oberflächen (RookHub, Turnierseite, KidHub, LeagueHub, ClubHub).
/// </summary>
public class NativeConfirmGuardTests
{
    /// <summary>
    /// Ein nativer Aufruf: <c>confirm(…)</c> MIT Argument und ohne Objekt davor, oder <c>window.</c>/<c>globalThis.</c>/
    /// <c>self.confirm(…)</c>. <c>confirm()</c> ohne Argument ist eine eigene Methode bzw. Funktion (Partie-Formular,
    /// Turnier-Favoriten) und <c>this.confirm.ask(…)</c> der Dienst — beides bleibt erlaubt.
    /// </summary>
    private static readonly Regex NativeConfirm = new(
        @"(?<![\w.$])confirm\s*\(\s*[^)\s]|\b(?:window|globalThis|self)\s*\.\s*confirm\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Dasselbe für <c>prompt(…)</c> (W5 F5-016): Texteingaben laufen über den <c>PromptService</c>
    /// (<c>src/app/shared/prompt-dialog</c>), der mehrere Felder in EINEM Dialog abfragt. <c>evt.prompt()</c> (das
    /// PWA-Installationsereignis) und <c>this.prompts.ask(…)</c> bleiben erlaubt.
    /// </summary>
    private static readonly Regex NativePrompt = new(
        @"(?<![\w.$])prompt\s*\(\s*[^)\s]|\b(?:window|globalThis|self)\s*\.\s*prompt\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex LineComment = new(@"(?<![:\\])//.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Kommentare weg, Zeilenumbrüche bleiben (damit die gemeldete Zeilennummer stimmt).</summary>
    private static string StripComments(string source) =>
        LineComment.Replace(BlockComment.Replace(source, m => new string('\n', m.Value.Count(c => c == '\n'))), "");

    [Theory]
    [InlineData("if (!confirm(this.translate.instant('x'))) return;")]
    [InlineData("if (!m || !confirm(`Notiz ${n} löschen?`)) return;")]
    [InlineData("const ok = window.confirm(msg);")]
    [InlineData("globalThis.confirm('x')")]
    public void Muster_ErkenntNativeAufrufe(string line) => Assert.Matches(NativeConfirm, line);

    [Theory]
    [InlineData("this.confirm.ask('games.deleteConfirm').subscribe(ok => {")]
    [InlineData("if (!(await firstValueFrom(this.confirm.ask('Dieses Foto löschen?')))) return;")]
    [InlineData("  confirm(): void {")]
    [InlineData("<button (click)=\"confirm()\">")]
    [InlineData("if (err?.status === 409) { confirm(); return; }")]
    [InlineData("this.confirmDialog.ask('games.edit.dropIllegal', { count })")]
    [InlineData("reconfirm('x')")]
    public void Muster_LaesstDienstUndEigeneMethodenDurch(string line) => Assert.DoesNotMatch(NativeConfirm, line);

    [Theory]
    [InlineData("const next = prompt(this.translate.instant('admin.books.renamePrompt'), book.displayName);")]
    [InlineData("const v = window.prompt('Name?');")]
    public void Muster_ErkenntNativePrompts(string line) => Assert.Matches(NativePrompt, line);

    [Theory]
    [InlineData("await evt.prompt();")]
    [InlineData("this.prompts.ask({ fields }).subscribe(res => {")]
    [InlineData("const renamePrompt = 'x';")]
    public void Muster_LaesstPromptDienstDurch(string line) => Assert.DoesNotMatch(NativePrompt, line);

    [Fact]
    public void KeineOberflaeche_RuftDasNativeConfirm() => AssertKeinTreffer(NativeConfirm,
        "Native Rückfrage gefunden — bitte ConfirmService.ask(…) nehmen (shared/confirm-dialog):\n");

    [Fact]
    public void KeineOberflaeche_RuftDasNativePrompt() => AssertKeinTreffer(NativePrompt,
        "Native Texteingabe gefunden — bitte PromptService.ask(…) nehmen (shared/prompt-dialog):\n");

    private static void AssertKeinTreffer(Regex pattern, string message)
    {
        var app = Path.Combine(RepoRoot(), "src", "frontend", "app");
        var files = Directory.GetDirectories(app, "src*")
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".ts", StringComparison.Ordinal) && !f.EndsWith(".spec.ts", StringComparison.Ordinal))
                        || f.EndsWith(".html", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count > 300, $"Nur {files.Count} Quelldateien gefunden — Pfad kaputt?");

        var hits = new List<string>();
        foreach (var file in files)
        {
            var lines = StripComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (pattern.IsMatch(lines[i]))
                    hits.Add($"{Path.GetRelativePath(app, file)}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.True(hits.Count == 0, message + string.Join("\n", hits));
    }
}
