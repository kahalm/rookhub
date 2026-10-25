using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Accounts;

public static class AdminSeeder
{
    /// <summary>
    /// Legt beim Start einen Bootstrap-Admin an — aber NUR, wenn es noch KEINEN aktiven Admin gibt
    /// (Codereview I1-008) und kein User mit diesem Usernamen existiert. Vorher genügte der fehlende
    /// Name: löschte der Betreiber das Seed-Konto (Löschung benennt es in <c>deleted_{id}</c> um), legte
    /// der nächste Neustart ein frisches Admin-Konto mit dem ADMIN_PASSWORD aus der .env an — still,
    /// mit einem Passwort, das meist seit der Ersteinrichtung niemand geändert hat. Ein vorhandener Account wird NICHT verändert
    /// (kein Passwort-Reset, kein Re-Promote bei jedem Boot). Sonst könnte über die
    /// Env-Konfiguration ein bestehendes Konto übernommen oder zurückgesetzt werden,
    /// und ein vom User selbst geändertes Admin-Passwort würde bei jedem Neustart
    /// wieder überschrieben.
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        await SeedEveryoneGroupAsync(db);

        var username = config["ADMIN_USERNAME"];
        var password = config["ADMIN_PASSWORD"];

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return;

        // Wohlbekannte Platzhalter aus den Beispiel-Dateien („change_me…", „your_…") nicht seeden —
        // verhindert einen versehentlichen Admin mit Default-Passwort. Dieselbe Erkennung wie für
        // die übrigen Geheimnisse (SecretConfigCheck).
        if (SecretConfigCheck.IsPlaceholder(password))
        {
            Console.Error.WriteLine(
                "[AdminSeeder] ADMIN_PASSWORD ist ein Platzhalter aus den Beispiel-Dateien — Admin wird NICHT angelegt. Bitte ein echtes Passwort setzen.");
            return;
        }

        // Es gibt schon einen aktiven Admin → das ist keine Ersteinrichtung mehr, nichts anlegen.
        if (await db.AppUsers.AnyAsync(u => u.IsAdmin && u.DeletedAt == null))
            return;

        // Vorhandenen Account niemals anfassen (kein Reset, kein Re-Promote).
        if (await db.AppUsers.AnyAsync(u => u.Username == username))
            return;

        Console.Error.WriteLine(
            $"[AdminSeeder] Kein aktiver Admin vorhanden — Bootstrap-Admin „{username}“ aus ADMIN_USERNAME/ADMIN_PASSWORD angelegt. ADMIN_PASSWORD nach der Ersteinrichtung aus der .env entfernen.");

        db.AppUsers.Add(new AppUser
        {
            Username = username,
            Email = $"{username}@rookhub.local",
            PasswordHash = PasswordHashing.Hash(password),
            IsAdmin = true,
            Profile = new UserProfile()
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Stellt sicher, dass genau EINE System-Gruppe „Everyone" existiert (<see cref="Group.IsEveryone"/>),
    /// in der jeder Nutzer implizit Mitglied ist. Idempotent: existiert bereits eine solche Gruppe,
    /// passiert nichts; existiert nur eine gleichnamige normale Gruppe, wird diese zur Everyone-Gruppe
    /// erhoben (verhindert Kollision mit dem Unique-Namensindex).
    /// </summary>
    private static async Task SeedEveryoneGroupAsync(AppDbContext db)
    {
        if (await db.Groups.AnyAsync(g => g.IsEveryone))
            return;

        var byName = await db.Groups.FirstOrDefaultAsync(g => g.Name == "Everyone");
        if (byName != null)
        {
            byName.IsEveryone = true;
        }
        else
        {
            db.Groups.Add(new Group
            {
                Name = "Everyone",
                Description = "Alle Nutzer sind automatisch Mitglied. Bücher/Kurse, die dieser Gruppe freigegeben werden, sind für jeden sichtbar.",
                IsEveryone = true,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }
}
