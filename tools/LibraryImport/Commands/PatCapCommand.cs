using RookHub.Api.Services.Accounts;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>pat-cap</c> — API-Tokens (PATs) ohne Ablauf auf die Hoechstdauer befristen (P-14).</summary>
internal static class PatCapCommand
{
    //   pat-cap [--dry-run] [--days 365]
    //
    // Seit P-14 (entschieden 25.10.2026) laeuft kein neuer API-Token mehr nie ab (ApiTokenService, Auth:PatMaxDays,
    // Vorgabe 365). Der Altbestand (am 25.10.: 30 von 35 Prod-Tokens ohne Ablauf) bekommt KEINE Migration, sondern
    // diesen Lauf: ExpiresAt = heute + 365 Tage fuer jede Zeile mit ExpiresAt NULL. Wiederholbar (zweiter Lauf: 0).
    // Vorher zaehlen:  SELECT COUNT(*) FROM UserApiTokens WHERE ExpiresAt IS NULL;
    // Auf Prod NUR auf ausdrueckliche Anweisung, nach einem Dump, und zuerst --dry-run.
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var dryRun = ctx.Options.Has("--dry-run");
        var days = ctx.Options.Int("--days") ?? ApiTokenService.DefaultMaxDays;
        if (days <= 0)
        {
            Console.Error.WriteLine("--days muss positiv sein.");
            return 2;
        }
        await using var db = ctx.Db.New();
        var r = await ApiTokenService.CapUnlimitedAsync(db, DateTime.UtcNow, days, dryRun);
        foreach (var (userId, n) in r.PerUser.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
            Console.WriteLine($"  Konto #{userId,-7} {n} Token(s) ohne Ablauf");
        Console.WriteLine((r.DryRun ? "PROBELAUF, nichts geschrieben. " : "")
            + $"{r.Tokens} Token(s) ohne Ablauf in {r.PerUser.Count} Konto/Konten"
            + (r.Tokens > 0 ? $" → ExpiresAt {r.ExpiresAt:yyyy-MM-dd HH:mm} UTC" : "") + ".");
        return 0;
    }
}
