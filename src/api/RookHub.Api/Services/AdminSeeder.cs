using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public static class AdminSeeder
{
    /// <summary>
    /// Legt beim Start einen Admin-Account an — aber NUR, wenn noch kein User mit
    /// diesem Usernamen existiert. Ein vorhandener Account wird NICHT verändert
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

        // Wohlbekannten Platzhalter aus den compose-Beispielen nicht seeden —
        // verhindert einen versehentlichen Admin mit Default-Passwort.
        if (password == "change_me")
        {
            Console.Error.WriteLine(
                "[AdminSeeder] ADMIN_PASSWORD ist der Platzhalter 'change_me' — Admin wird NICHT angelegt. Bitte ein echtes Passwort setzen.");
            return;
        }

        // Vorhandenen Account niemals anfassen (kein Reset, kein Re-Promote).
        if (await db.AppUsers.AnyAsync(u => u.Username == username))
            return;

        db.AppUsers.Add(new AppUser
        {
            Username = username,
            Email = $"{username}@rookhub.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
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
