using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public static class AdminSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        var username = config["ADMIN_USERNAME"];
        var password = config["ADMIN_PASSWORD"];

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return;

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);

        if (user == null)
        {
            user = new AppUser
            {
                Username = username,
                Email = $"{username}@rookhub.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsAdmin = true,
                Profile = new UserProfile()
            };
            db.AppUsers.Add(user);
        }
        else
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.IsAdmin = true;
        }

        await db.SaveChangesAsync();
    }
}
