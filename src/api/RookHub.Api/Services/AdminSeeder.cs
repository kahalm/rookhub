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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = true,
            Profile = new UserProfile()
        });
        await db.SaveChangesAsync();
    }
}
