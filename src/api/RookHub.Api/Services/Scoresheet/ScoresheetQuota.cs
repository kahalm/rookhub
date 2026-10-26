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

/// <summary>
/// Kontingent der Einlesungen (Zerlegung Z6 aus <see cref="ScoresheetScanService"/>): Tageszahlen je Weg und je IP,
/// Verbrauch gegen die Kostenbremse (<see cref="ScoresheetBudget"/>) und der Stand für die Seite.
/// </summary>
internal sealed class ScoresheetQuota
{
    private readonly AppDbContext _db;
    private readonly IScoresheetVisionClient _vision;
    private readonly ScoresheetBudget _budget;
    private readonly int _dailyLimit;
    private readonly int _leagueDailyLimit;
    private readonly string _ipSecret;

    internal ScoresheetQuota(AppDbContext db, IScoresheetVisionClient vision, ScoresheetBudget budget, int dailyLimit,
        int leagueDailyLimit, string ipSecret, bool external)
    {
        _db = db;
        _vision = vision;
        _budget = budget;
        _dailyLimit = dailyLimit;
        _leagueDailyLimit = leagueDailyLimit;
        _ipSecret = ipSecret;
        External = external;
    }

    /// <summary>Liest von außen (<see cref="ScoresheetScanService.External"/>).</summary>
    private bool External { get; }

    /// <summary>Was ein Nutzer heute und in 30 Tagen verbraucht hat, was alle zusammen heute — und ob er Admin ist.</summary>
    /// <para>Ohne Konto (<paramref name="userId"/> <c>null</c>, Teilen-Link) gibt es keine Nutzerbudgets — dort begrenzen
    /// die Zahlen je IP und je Tag (<see cref="ScoresheetScanService.AnonPerIpDailyLimit"/>, <see cref="ScoresheetScanService.AnonDailyLimit"/>), und an der Stelle des
    /// Nutzerverbrauchs steht, was ALLE Einlesungen ohne Konto heute verbraucht haben (ihr gemeinsames Tagesbudget,
    /// <see cref="ScoresheetBudget.AnonAllowance"/>); das Gesamtbudget gilt dazu.</para>
    internal async Task<(long UserToday, long UserMonth, long GlobalToday, bool IsAdmin)> SpentAsync(int? userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var day = now.AddDays(-1);
        var month = now.AddDays(-30);
        if (userId == null)
        {
            var anonToday = await _db.ScoresheetScans.Where(s => s.UserId == null && s.CreatedAt >= day)
                .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
            var global0 = await _db.ScoresheetScans.Where(s => s.CreatedAt >= day).SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
            return (anonToday, 0, global0, false);
        }
        var userToday = await _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= day)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var userMonth = await _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= month)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var globalToday = await _db.ScoresheetScans.Where(s => s.CreatedAt >= day)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var isAdmin = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.IsAdmin).FirstOrDefaultAsync(ct);
        return (userToday, userMonth, globalToday, isAdmin);
    }

    /// <summary>
    /// Die Einlesungen eines Nutzers seit <paramref name="since"/>, die gegen das Tageskontingent zählen: alle außer den
    /// gescheiterten, die keinen Cent gekostet haben (Modell nicht erreichbar, Budget schon vor dem ersten Aufruf leer) —
    /// bei einer Einlesung am Tag sperrte ein Ausfall auf unserer Seite sonst für 24 h. Eine Einlesung, deren Partie
    /// gelöscht wurde, zählt weiter (<see cref="DetachWithoutLoading"/>), sonst hieße „Partie löschen“ „noch einmal lesen“.
    /// </summary>
    /// <para>Je Weg getrennt (<paramref name="purpose"/>): RookHubs „Meine Partien" und die Vereins-Datenbank haben
    /// eigene Tageszahlen (<see cref="LimitFor"/>).</para>
    internal IQueryable<ScoresheetScan> CountingSince(int userId, DateTime since, string? purpose)
        => _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= since && s.Purpose == purpose
            && !(s.Status == ScoresheetScanStatus.Failed && s.CostMicroUsd == 0));

    internal int LimitFor(string? purpose) => purpose == ScoresheetScan.PurposeLeague ? _leagueDailyLimit : _dailyLimit;

    internal static string? CleanPurpose(string? purpose) => purpose == ScoresheetScan.PurposeLeague ? purpose : null;

    /// <summary>Darf für diesen Nutzer jetzt noch ein Modell-Aufruf starten, und wie lang darf die Antwort werden?</summary>
    internal async Task<CallAllowance> AllowanceAsync(int? userId, CancellationToken ct = default)
        => AllowanceFor(userId, await SpentAsync(userId, ct));

    /// <summary>Mit Konto die Nutzerbudgets samt Gesamtbudget, ohne Konto das gemeinsame Tagesbudget der Einlesungen ohne
    /// Konto samt Gesamtbudget (<see cref="SpentAsync"/>).</summary>
    internal CallAllowance AllowanceFor(int? userId, (long UserToday, long UserMonth, long GlobalToday, bool IsAdmin) spent)
        => userId == null
            ? _budget.AnonAllowance(spent.UserToday, spent.GlobalToday)
            : _budget.Allowance(spent.UserToday, spent.UserMonth, spent.GlobalToday, spent.IsAdmin);

    public async Task<ScoresheetStatusDto> StatusAsync(int userId, string? purpose = null)
    {
        purpose = CleanPurpose(purpose);
        var limit = LimitFor(purpose);
        var since = DateTime.UtcNow.AddDays(-1);
        var (today, month, global, admin) = await SpentAsync(userId);
        var dayShare = _budget.UserDailyMicroUsd > 0 ? (double)today / _budget.UserDailyMicroUsd : 1;
        var monthShare = _budget.UserMonthlyMicroUsd > 0 ? (double)month / _budget.UserMonthlyMicroUsd : 1;
        var used = await CountingSince(userId, since, purpose).CountAsync();
        // Das Fenster rollt über 24 h: frei wird es, wenn die älteste mitzählende Einlesung herausfällt.
        DateTime? next = null;
        if (!admin && used >= limit)
        {
            var oldest = await CountingSince(userId, since, purpose).OrderBy(s => s.CreatedAt).Skip(used - limit)
                .Select(s => s.CreatedAt).FirstOrDefaultAsync();
            next = DateTime.SpecifyKind(oldest.AddDays(1), DateTimeKind.Utc);
        }
        return new ScoresheetStatusDto
        {
            Available = External || _vision.IsConfigured,
            DailyLimit = limit,
            UsedToday = used,
            NextAllowedAt = next,
            BudgetUsedPercent = admin ? 0 : (int)Math.Clamp(Math.Round(Math.Max(dayShare, monthShare) * 100), 0, 100),
            Blocked = _budget.Check(today, month, global, admin),
            Unlimited = admin,
            Languages = ScoresheetNotation.Languages.Select(l => new ScoresheetLanguageDto
            {
                Code = l.Code,
                Name = l.Name,
                Pieces = $"{l.King} {l.Queen} {l.Rook} {l.Bishop} {l.Knight}",
            }).ToList(),
        };
    }

    /// <summary>HMAC der IP-Adresse — die Adresse selbst wird nie gespeichert. Gezählt wird je Anschluss
    /// (<see cref="IpKey"/>).</summary>
    public string AnonIpHash(System.Net.IPAddress? ip)
    {
        var bytes = System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_ipSecret),
            System.Text.Encoding.UTF8.GetBytes("scoresheet-ip:" + IpKey(ip)));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    /// <summary>Wofür die Grenzen je IP gelten: IPv4 die Adresse, echtes IPv6 das /64-Netz (<see cref="IpBucket"/>).</summary>
    internal static string IpKey(System.Net.IPAddress? ip) => IpBucket.Key(ip);

    internal IQueryable<ScoresheetScan> AnonCountingSince(DateTime since) =>
        _db.ScoresheetScans.Where(s => s.UserId == null && s.CreatedAt >= since
            && !(s.Status == ScoresheetScanStatus.Failed && s.CostMicroUsd == 0));

    /// <summary>Stand für eine Einlesung OHNE Konto (Teilen-Link): Tageszahl je IP, gesperrt wenn alle zusammen die
    /// Tagesgrenze erreicht haben (<c>anonDailyLimit</c>) oder das Gesamtbudget leer ist.</summary>
    public async Task<ScoresheetStatusDto> AnonStatusAsync(string ipHash)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var spent = await SpentAsync(null);
        var used = await AnonCountingSince(since).CountAsync(s => s.AnonIpHash == ipHash);
        DateTime? next = null;
        if (used >= ScoresheetScanService.AnonPerIpDailyLimit)
        {
            var oldest = await AnonCountingSince(since).Where(s => s.AnonIpHash == ipHash).OrderBy(s => s.CreatedAt)
                .Skip(used - ScoresheetScanService.AnonPerIpDailyLimit).Select(s => s.CreatedAt).FirstOrDefaultAsync();
            next = DateTime.SpecifyKind(oldest.AddDays(1), DateTimeKind.Utc);
        }
        var all = await AnonCountingSince(since).CountAsync();
        return new ScoresheetStatusDto
        {
            Available = External || _vision.IsConfigured,
            DailyLimit = ScoresheetScanService.AnonPerIpDailyLimit,
            UsedToday = used,
            NextAllowedAt = next,
            Blocked = all >= ScoresheetScanService.AnonDailyLimit ? "anonDailyLimit" : AllowanceFor(null, spent).Blocked,
            Languages = ScoresheetNotation.Languages.Select(l => new ScoresheetLanguageDto
            {
                Code = l.Code, Name = l.Name, Pieces = $"{l.King} {l.Queen} {l.Rook} {l.Bishop} {l.Knight}",
            }).ToList(),
        };
    }
}
