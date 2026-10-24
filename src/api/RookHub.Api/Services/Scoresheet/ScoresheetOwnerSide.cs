using System.Text.Json;
using System.Text.Json.Serialization;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Scoresheet;
using RookHub.Api.DTOs.League;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>Welche Seite spielte der Nutzer (Zerlegung Z6 aus <see cref="ScoresheetScanService"/>)?</summary>
internal static class ScoresheetOwnerSide
{
    /// <summary>„Ich spielte: automatisch" — welcher gelesene Spielername passt zum Profil?</summary>
    internal static async Task<string?> GuessOwnerSideAsync(AppDbContext db, int userId, ScoresheetTranscription t, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var username = await db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(ct);
        return GuessOwnerSide(t.White, t.Black,
            new[] { profile?.LastName, profile?.DisplayName, profile?.FirstName, username });
    }

    /// <summary>
    /// Welche Seite gehört dem Nutzer? Seine Namen (Nachname, Anzeigename, Vorname, Benutzername — in dieser Reihenfolge)
    /// werden in den vom Formular gelesenen Spielernamen gesucht, als ganzes Wort und ohne Groß/klein und Akzente. Nur
    /// wenn GENAU EINE Seite passt, gilt sie; sonst <c>null</c> (lieber keine Drehung als eine falsche).
    /// </summary>
    public static string? GuessOwnerSide(string? white, string? black, IEnumerable<string?> myNames)
    {
        static string Norm(string? s) => new string((s ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray());
        var w = Norm(white).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var b = Norm(black).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        foreach (var name in myNames)
        {
            var words = Norm(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 3).ToList();
            if (words.Count == 0) continue;
            var inWhite = words.Any(w.Contains);
            var inBlack = words.Any(b.Contains);
            if (inWhite && !inBlack) return "white";
            if (inBlack && !inWhite) return "black";
        }
        return null;
    }
}
