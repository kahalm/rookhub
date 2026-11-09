using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Teilen-Link für EINE Ligapartie (Wunsch 2026-11-09: „ich will auch eine ligapartie teilen können (als angemeldeter user)").
/// Die Marke trägt alles selbst (Verein, Turnier, Runde, Begegnung, Brett, Vereinspartie, Ablauf) und ist mit dem
/// DataProtection-Schlüsselbund der API versiegelt — KEINE Tabelle (wie <see cref="Puzzles.DailyStartTokens"/>) und bewusst
/// NICHT <see cref="LeagueShare"/>: jede Zeile dort öffnet die ganze Begegnung samt Meldeliste und Upload. Der Preis: einzeln
/// widerrufen lässt sich so ein Link nicht, er läuft nur ab.
/// <para>Teilbar ist genau, was <see cref="LeagueFixtureGames"/> an dem Brett mit PGN ausgibt (Vereinspartie des Vereins oder
/// Partie aus einer Spielerkarte). Beim Lesen wird das Brett neu aufgelöst und muss noch DIESELBE Vereinspartie tragen — eine
/// umgehängte oder archivierte Partie macht den Link ungültig, statt still eine andere zu zeigen.</para>
/// </summary>
public sealed class LeagueGameShares(AppDbContext db, LeagueFixtureGames games, IDataProtectionProvider protection, TimeProvider? clock = null)
{
    private readonly IDataProtector _protector = protection.CreateProtector("RookHub.League.GameShare.v1");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Was die Seite des Links zeigt. Bei einer anonymisierten Vereinspartie die Namen der Partie und weder Brett noch
    /// Mannschaften (Brett + Mannschaft + Runde ergäben über die öffentlichen Paarungen den Spieler).</summary>
    public sealed record SharedGame(string? League, string? Season, int Round, int? Board, string? Home, string? Away,
        string? White, int? WhiteElo, string? Black, int? BlackElo, string Result, string Pgn, string Expires);

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    /// <summary>Link für das Brett anlegen; <c>null</c> = an diesem Brett gibt es keine Partie.</summary>
    public async Task<(string Token, DateOnly Expires)?> CreateAsync(LeagueClub club, int tnr, int round, string team, int board, CancellationToken ct)
    {
        var g = await db.LeagueGames.AsNoTracking().FirstOrDefaultAsync(x => x.Tnr == tnr && x.Round == round && x.Board == board
            && (x.HomeTeam == team || x.AwayTeam == team), ct);
        if (g is null || await PairingAsync(club, g, ct) is not { } p) return null;
        var date = await db.LeagueRounds.AsNoTracking().Where(r => r.Tnr == tnr && r.Round == round).Select(r => r.Date).FirstOrDefaultAsync(ct);
        var expires = LeagueService.ExpiresForDate(date, Today);
        return (Seal(club.Id, tnr, round, g.MatchNo, board, p.ClubGameId ?? 0, expires), expires);
    }

    /// <summary>Die geteilte Partie — <c>null</c> = Marke kaputt/fremd, abgelaufen oder die Partie hängt nicht mehr an dem Brett.</summary>
    public async Task<SharedGame?> ReadAsync(string token, CancellationToken ct)
    {
        if (Open(token) is not { } t || t.Expires < Today) return null;
        var club = await db.LeagueClubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == t.ClubId, ct);
        var g = await db.LeagueGames.AsNoTracking().FirstOrDefaultAsync(x => x.Tnr == t.Tnr && x.Round == t.Round && x.MatchNo == t.MatchNo && x.Board == t.Board, ct);
        if (club is null || g is null || await PairingAsync(club, g, ct) is not { } p || (p.ClubGameId ?? 0) != t.ClubGameId) return null;
        var league = await db.LeagueTournaments.AsNoTracking().Where(l => l.Tnr == t.Tnr).Select(l => new { l.Name, l.Season }).FirstOrDefaultAsync(ct);
        var expires = t.Expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var anon = t.ClubGameId == 0 ? null : await db.LeagueClubGames.AsNoTracking()
            .Where(c => c.Id == t.ClubGameId && c.ClubId == club.Id && c.Anonymized).Select(c => new { c.White, c.Black }).FirstOrDefaultAsync(ct);
        return anon is null
            ? new SharedGame(league?.Name, league?.Season, t.Round, p.Board, g.HomeTeam, g.AwayTeam, p.White, p.WhiteElo, p.Black, p.BlackElo, p.Result, p.Pgn!, expires)
            : new SharedGame(league?.Name, league?.Season, t.Round, null, null, null, anon.White, anon.White == club.AnonName ? null : p.WhiteElo,
                anon.Black, anon.Black == club.AnonName ? null : p.BlackElo, p.Result, p.Pgn!, expires);
    }

    /// <summary>Das Brett, wie es die Ansicht der Begegnung ohne Anmeldung ausgibt — dort, wo es eine Partie gibt. Über die ganze
    /// Begegnung statt nur dieses Bretts, damit eine geratene Vereinspartie an demselben Brett landet wie in der Ansicht.</summary>
    private async Task<LeagueFixtureGames.Pairing?> PairingAsync(LeagueClub club, LeagueGame g, CancellationToken ct)
    {
        var p = (await games.ForFixtureAsync(club, g.Tnr, g.Round, g.HomeTeam, ct)).FirstOrDefault(x => x.Board == g.Board);
        return string.IsNullOrEmpty(p?.Pgn) ? null : p;
    }

    private sealed record Claims(int ClubId, int Tnr, int Round, int MatchNo, int Board, int ClubGameId, DateOnly Expires);

    private string Seal(int clubId, int tnr, int round, int matchNo, int board, int clubGameId, DateOnly expires) =>
        _protector.Protect(string.Join('|', clubId, tnr, round, matchNo, board, clubGameId, expires.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));

    private Claims? Open(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        string payload;
        // Unprotect wirft auch FormatException-artige Fehler als CryptographicException; kaputtes Base64url ebenso.
        try { payload = _protector.Unprotect(token); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
        catch (FormatException) { return null; }
        var f = payload.Split('|');
        if (f.Length != 7 || !DateOnly.TryParseExact(f[6], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)) return null;
        var n = new int[6];
        for (var i = 0; i < 6; i++)
            if (!int.TryParse(f[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
        return new Claims(n[0], n[1], n[2], n[3], n[4], n[5], expires);
    }
}
