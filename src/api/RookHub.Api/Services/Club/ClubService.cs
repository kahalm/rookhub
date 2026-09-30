using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Club;

/// <summary>
/// Wer gerade fragt. <see cref="Manager"/> (Admin oder <see cref="Permissions.ClubManage"/>) sieht und darf alles;
/// <see cref="Trainer"/> (<see cref="Permissions.ClubTrainer"/>) nur die Gruppen, denen er zugeteilt ist, und deren Kinder.
/// </summary>
public readonly record struct ClubActor(int UserId, bool Manager, bool Trainer)
{
    public bool Any => Manager || Trainer;
}

/// <summary>
/// ClubHub — Kartei der Kinder und Jugendlichen des Vereins (Wunsch 2026-09-30): Blätter mit Kontakten (beliebig viele
/// Telefonnummern und E-Mail-Adressen, jede mit Hinweis wie „Mutter Daniela"), Trainingsgruppen mit Anwesenheit, Lernstand
/// (Stufe + datierte Notizen) und die Verknüpfung mit einem Konto.
///
/// <para><b>Sichtbarkeit ist die eine Regel dieses Dienstes:</b> jede Methode nimmt den <see cref="ClubActor"/> und liefert
/// für ein nicht sichtbares Kind bzw. eine fremde Gruppe dasselbe wie für ein unbekanntes (404) — ein Trainer erfährt nicht
/// einmal, dass es das Blatt gibt. Im Log stehen nie Namen oder Nummern, nur Ids.</para>
/// </summary>
public class ClubService
{
    /// <summary>So lange gilt ein Einmal-Code zum Verknüpfen.</summary>
    public static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromDays(14);
    /// <summary>Ohne 0/O, 1/I — der Code wird abgetippt, oft von einem Kind.</summary>
    private const string LinkAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int LinkCodeLength = 10;
    /// <summary>Einheiten in der Anwesenheitstabelle einer Gruppe.</summary>
    public const int DefaultSessionWindow = 12;
    public const int MaxSessionWindow = 60;

    private static readonly Regex PhonePattern = new(@"^[+0-9(][0-9 ()/\-.]*$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly Func<DateTime> _utcNow;

    public ClubService(AppDbContext db) : this(db, () => DateTime.UtcNow) { }

    internal ClubService(AppDbContext db, Func<DateTime> utcNow)
    {
        _db = db;
        _utcNow = utcNow;
    }

    // ---- Sichtbarkeit -------------------------------------------------------------------------

    private static void Require(ClubActor actor)
    {
        if (!actor.Any) throw new ForbiddenException("ClubHub ist für dieses Konto nicht freigeschaltet.");
    }

    private static void RequireManager(ClubActor actor)
    {
        if (!actor.Manager) throw new ForbiddenException("Das darf nur die Leitung.");
    }

    /// <summary>Gruppen, denen der Trainer zugeteilt ist (für die Leitung unerheblich).</summary>
    private Task<List<int>> OwnGroupIdsAsync(ClubActor actor, CancellationToken ct) =>
        _db.ClubGroupTrainers.Where(t => t.UserId == actor.UserId).Select(t => t.GroupId).ToListAsync(ct);

    private IQueryable<ClubMember> Visible(ClubActor actor, List<int> own) =>
        actor.Manager ? _db.ClubMembers : _db.ClubMembers.Where(m => m.Groups.Any(g => own.Contains(g.GroupId)));

    private async Task<ClubMember> LoadVisibleAsync(ClubActor actor, int id, CancellationToken ct)
    {
        Require(actor);
        var own = actor.Manager ? [] : await OwnGroupIdsAsync(actor, ct);
        return await Visible(actor, own).Include(m => m.Contacts).Include(m => m.Groups)
                   .FirstOrDefaultAsync(m => m.Id == id, ct)
               ?? throw new NotFoundException("Dieses Kind gibt es nicht.");
    }

    private async Task<ClubGroup> LoadGroupAsync(ClubActor actor, int id, CancellationToken ct)
    {
        Require(actor);
        var group = await _db.ClubGroups.Include(g => g.Trainers).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null || (!actor.Manager && group.Trainers.All(t => t.UserId != actor.UserId)))
            throw new NotFoundException("Diese Gruppe gibt es nicht.");
        return group;
    }

    // ---- Kinder -------------------------------------------------------------------------------

    /// <summary>Wonach die Kartei ordnet: der Nachname — und wo keiner eingetragen ist, der Vorname. So steht „Daniel" ohne
    /// Nachnamen bei D statt vor allen anderen.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<ClubMember, string>> SortName =
        m => m.LastName == "" ? m.FirstName : m.LastName;

    /// <summary>
    /// Die Kartei. Bewusst OHNE Namenssuche am Server: die Kartei eines Vereins ist klein, die Oberfläche sucht im Browser —
    /// und ein Name in der Adresse stünde in jedem Zugriffsprotokoll.
    /// </summary>
    public async Task<List<ClubMemberListDto>> ListMembersAsync(ClubActor actor, int? groupId, bool archived,
        CancellationToken ct = default)
    {
        Require(actor);
        var own = actor.Manager ? [] : await OwnGroupIdsAsync(actor, ct);
        var query = Visible(actor, own).Where(m => m.Archived == archived);
        if (groupId is { } gid) query = query.Where(m => m.Groups.Any(g => g.GroupId == gid));
        var members = await query.Include(m => m.Contacts).Include(m => m.Groups)
            .OrderBy(SortName).ThenBy(m => m.FirstName).AsNoTracking().ToListAsync(ct);
        var names = await GroupNamesAsync(members.SelectMany(m => m.Groups).Select(g => g.GroupId), ct);
        return members.Select(m => Fill(new ClubMemberListDto(), m, names)).ToList();
    }

    public async Task<ClubMemberDto> GetMemberAsync(ClubActor actor, int id, CancellationToken ct = default)
    {
        var m = await LoadVisibleAsync(actor, id, ct);
        return await DetailAsync(actor, m, ct);
    }

    public async Task<ClubMemberDto> CreateMemberAsync(ClubActor actor, ClubMemberInputDto input, CancellationToken ct = default)
    {
        Require(actor);
        var now = _utcNow();
        var m = new ClubMember { CreatedAt = now, CreatedByUserId = actor.UserId };
        Apply(m, input, now);
        await ApplyGroupsAsync(actor, m, input.GroupIds, ct);
        _db.ClubMembers.Add(m);
        await _db.SaveChangesAsync(ct);
        return await DetailAsync(actor, m, ct);
    }

    public async Task<ClubMemberDto> UpdateMemberAsync(ClubActor actor, int id, ClubMemberInputDto input, CancellationToken ct = default)
    {
        var m = await LoadVisibleAsync(actor, id, ct);
        Apply(m, input, _utcNow());
        await ApplyGroupsAsync(actor, m, input.GroupIds, ct);
        await _db.SaveChangesAsync(ct);
        return await DetailAsync(actor, m, ct);
    }

    /// <summary>Das Blatt mit allem, was daran hängt (Kontakte, Notizen, Anwesenheit) — nur die Leitung.</summary>
    public async Task DeleteMemberAsync(ClubActor actor, int id, CancellationToken ct = default)
    {
        RequireManager(actor);
        var m = await _db.ClubMembers.Include(x => x.Contacts).Include(x => x.Groups).Include(x => x.NoteEntries)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Dieses Kind gibt es nicht.");
        // Ausgeschrieben statt dem Cascade-FK überlassen: die Tests laufen auf InMemory, und die cascadet nicht.
        _db.ClubAttendances.RemoveRange(await _db.ClubAttendances.Where(a => a.MemberId == id).ToListAsync(ct));
        _db.ClubContacts.RemoveRange(m.Contacts);
        _db.ClubGroupMembers.RemoveRange(m.Groups);
        _db.ClubNotes.RemoveRange(m.NoteEntries);
        _db.ClubMembers.Remove(m);
        await _db.SaveChangesAsync(ct);
    }

    private void Apply(ClubMember m, ClubMemberInputDto input, DateTime now)
    {
        m.FirstName = Clean(input.FirstName) ?? throw new DomainValidationException("Der Vorname fehlt.");
        m.LastName = Clean(input.LastName) ?? "";                       // optional — der Trainer kennt ihn oft nicht

        var maxYear = now.Year;
        if (Clean(input.BirthDate) is { } raw)
        {
            if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date.Year < 1900 || date > DateOnly.FromDateTime(now))
                throw new DomainValidationException("Das Geburtsdatum ist ungültig.");
            m.BirthDate = date;
            m.BirthYear = date.Year;
        }
        else
        {
            if (input.BirthYear is { } year && (year < 1900 || year > maxYear))
                throw new DomainValidationException("Der Jahrgang ist ungültig.");
            m.BirthDate = null;
            m.BirthYear = input.BirthYear;
        }

        m.Level = Clean(input.Level);
        m.Archived = input.Archived;
        m.UpdatedAt = now;

        var contacts = NormalizeContacts(input.Contacts);
        _db.ClubContacts.RemoveRange(m.Contacts);
        m.Contacts = contacts;
    }

    /// <summary>
    /// Kontakte prüfen und ordnen. Eine Zeile ohne Wert fällt still weg (eine leer gelassene Zeile des Formulars ist kein
    /// Fehler); eine mit einem Wert, der weder Nummer noch Adresse ist, ist einer — sonst stünde der Tippfehler in der Kartei.
    /// </summary>
    internal static List<ClubContact> NormalizeContacts(IEnumerable<ClubContactDto>? input)
    {
        var result = new List<ClubContact>();
        foreach (var c in input ?? [])
        {
            var value = Clean(c.Value);
            if (value == null) continue;
            var kind = (c.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == ClubContact.Phone)
            {
                if (!PhonePattern.IsMatch(value) || value.Count(char.IsDigit) < 3)
                    throw new DomainValidationException($"„{value}“ ist keine Telefonnummer.");
            }
            else if (kind == ClubContact.Email)
            {
                if (!EmailPattern.IsMatch(value))
                    throw new DomainValidationException($"„{value}“ ist keine E-Mail-Adresse.");
            }
            else
            {
                throw new DomainValidationException("Ein Kontakt ist eine Telefonnummer oder eine E-Mail-Adresse.");
            }
            result.Add(new ClubContact { Kind = kind, Value = value, Label = Clean(c.Label), Position = result.Count });
        }
        return result;
    }

    /// <summary>
    /// Gruppen des Kindes setzen. Die Leitung setzt die ganze Liste. Ein Trainer verfügt nur über SEINE Gruppen — die
    /// Zugehörigkeit zu fremden bleibt, wie sie ist — und muss das Kind in mindestens einer eigenen lassen: sonst legte er
    /// ein Blatt an (oder gäbe es ab), das er selbst nicht mehr sieht.
    /// </summary>
    private async Task ApplyGroupsAsync(ClubActor actor, ClubMember m, List<int>? groupIds, CancellationToken ct)
    {
        var wanted = (groupIds ?? []).Distinct().ToList();
        var known = await _db.ClubGroups.Where(g => wanted.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct);
        if (known.Count != wanted.Count) throw new DomainValidationException("Eine der Gruppen gibt es nicht.");

        List<int> target;
        if (actor.Manager)
        {
            target = wanted;
        }
        else
        {
            var own = await OwnGroupIdsAsync(actor, ct);
            var mine = wanted.Where(own.Contains).ToList();
            if (mine.Count == 0)
                throw new DomainValidationException("Wähle mindestens eine deiner Gruppen — sonst siehst du das Kind nicht mehr.");
            target = m.Groups.Select(g => g.GroupId).Where(id => !own.Contains(id)).Concat(mine).ToList();
        }

        foreach (var gm in m.Groups.Where(g => !target.Contains(g.GroupId)).ToList())
        {
            m.Groups.Remove(gm);
            _db.ClubGroupMembers.Remove(gm);
        }
        foreach (var id in target.Where(id => m.Groups.All(g => g.GroupId != id)))
            m.Groups.Add(new ClubGroupMember { GroupId = id });
    }

    private async Task<ClubMemberDto> DetailAsync(ClubActor actor, ClubMember m, CancellationToken ct)
    {
        var names = await GroupNamesAsync(m.Groups.Select(g => g.GroupId), ct);
        var dto = Fill(new ClubMemberDto(), m, names);
        dto.BirthDate = m.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        dto.CreatedAt = m.CreatedAt;
        dto.UpdatedAt = m.UpdatedAt;
        dto.CanDelete = actor.Manager;
        if (m.LinkedUserId is { } uid)
            dto.LinkedUsername = await _db.AppUsers.Where(u => u.Id == uid && u.DeletedAt == null).Select(u => u.Username).FirstOrDefaultAsync(ct);
        if (m.LinkCode != null && m.LinkCodeExpires > _utcNow())
        {
            dto.LinkCode = m.LinkCode;
            dto.LinkCodeExpires = m.LinkCodeExpires;
        }

        var notes = await _db.ClubNotes.Where(n => n.MemberId == m.Id).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .AsNoTracking().ToListAsync(ct);
        var authors = await UsernamesAsync(notes.Where(n => n.AuthorUserId != null).Select(n => n.AuthorUserId!.Value), ct);
        dto.NoteEntries = notes.Select(n => new ClubNoteDto
        {
            Id = n.Id, Text = n.Text, CreatedAt = n.CreatedAt,
            Author = n.AuthorUserId is { } a ? authors.GetValueOrDefault(a) : null,
            CanDelete = actor.Manager || n.AuthorUserId == actor.UserId,
        }).ToList();

        var attendance = await _db.ClubAttendances.Where(a => a.MemberId == m.Id)
            .Select(a => new { a.SessionId, a.Status, a.Session!.GroupId, a.Session.Date, a.Session.Topic })
            .ToListAsync(ct);
        var groupNames = await GroupNamesAsync(attendance.Select(a => a.GroupId), ct);
        dto.Attendance = new ClubAttendanceSummaryDto
        {
            Present = attendance.Count(a => a.Status == ClubAttendanceStatus.Present),
            Absent = attendance.Count(a => a.Status == ClubAttendanceStatus.Absent),
            Recent = attendance.OrderByDescending(a => a.Date).ThenByDescending(a => a.SessionId).Take(10)
                .Select(a => new ClubAttendanceEntryDto
                {
                    SessionId = a.SessionId, GroupId = a.GroupId, Group = groupNames.GetValueOrDefault(a.GroupId, ""),
                    Date = Iso(a.Date), Topic = a.Topic, Status = StatusKey(a.Status)!,
                }).ToList(),
        };
        return dto;
    }

    private static T Fill<T>(T dto, ClubMember m, Dictionary<int, string> groupNames) where T : ClubMemberListDto
    {
        dto.Id = m.Id;
        dto.FirstName = m.FirstName;
        dto.LastName = m.LastName;
        dto.BirthYear = m.BirthYear;
        dto.Level = m.Level;
        dto.Archived = m.Archived;
        dto.Linked = m.LinkedUserId != null;
        dto.Groups = m.Groups.Select(g => new ClubGroupRefDto { Id = g.GroupId, Name = groupNames.GetValueOrDefault(g.GroupId, "") })
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        dto.Contacts = m.Contacts.OrderBy(c => c.Position).ThenBy(c => c.Id)
            .Select(c => new ClubContactDto { Kind = c.Kind, Value = c.Value, Label = c.Label }).ToList();
        return dto;
    }

    // ---- Notizen ------------------------------------------------------------------------------

    public async Task<ClubMemberDto> AddNoteAsync(ClubActor actor, int memberId, string text, CancellationToken ct = default)
    {
        var m = await LoadVisibleAsync(actor, memberId, ct);
        var clean = Clean(text) ?? throw new DomainValidationException("Die Notiz ist leer.");
        _db.ClubNotes.Add(new ClubNote { MemberId = m.Id, AuthorUserId = actor.UserId, CreatedAt = _utcNow(), Text = clean });
        await _db.SaveChangesAsync(ct);
        return await DetailAsync(actor, m, ct);
    }

    /// <summary>Eigene Notizen löscht der Trainer selbst, fremde nur die Leitung.</summary>
    public async Task<ClubMemberDto> DeleteNoteAsync(ClubActor actor, int memberId, int noteId, CancellationToken ct = default)
    {
        var m = await LoadVisibleAsync(actor, memberId, ct);
        var note = await _db.ClubNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.MemberId == m.Id, ct)
                   ?? throw new NotFoundException("Diese Notiz gibt es nicht.");
        if (!actor.Manager && note.AuthorUserId != actor.UserId)
            throw new ForbiddenException("Fremde Notizen löscht nur die Leitung.");
        _db.ClubNotes.Remove(note);
        await _db.SaveChangesAsync(ct);
        return await DetailAsync(actor, m, ct);
    }

    // ---- Konto verknüpfen ---------------------------------------------------------------------

    /// <summary>Neuen Einmal-Code ausgeben (ersetzt einen offenen). 409, wenn schon ein Konto verknüpft ist.</summary>
    public async Task<ClubLinkCodeDto> CreateLinkCodeAsync(ClubActor actor, int memberId, CancellationToken ct = default)
    {
        var m = await LoadVisibleAsync(actor, memberId, ct);
        if (m.LinkedUserId != null) throw new ConflictException("Es ist schon ein Konto verknüpft — erst trennen.");
        m.LinkCode = NewLinkCode();
        m.LinkCodeExpires = _utcNow().Add(LinkCodeLifetime);
        await _db.SaveChangesAsync(ct);
        return new ClubLinkCodeDto { Code = m.LinkCode, Expires = m.LinkCodeExpires.Value };
    }

    /// <summary>Verknüpfung UND offenen Code entfernen — von der Trainerseite.</summary>
    public async Task<ClubMemberDto> UnlinkAsync(ClubActor actor, int memberId, CancellationToken ct = default)
    {
        var m = await LoadVisibleAsync(actor, memberId, ct);
        m.LinkedUserId = null;
        m.LinkCode = null;
        m.LinkCodeExpires = null;
        await _db.SaveChangesAsync(ct);
        return await DetailAsync(actor, m, ct);
    }

    /// <summary>
    /// Das KONTO löst den Code ein — das ist die Einwilligung, dass der Trainer seinen Fortschritt sieht. Unbekannt und
    /// abgelaufen sind derselbe Fehler; ein Konto hängt an höchstens einem Blatt.
    /// </summary>
    public async Task<ClubLinkStateDto> RedeemAsync(int userId, string code, CancellationToken ct = default)
    {
        var clean = NormalizeLinkCode(code);
        var now = _utcNow();
        var m = clean.Length == LinkCodeLength
            ? await _db.ClubMembers.FirstOrDefaultAsync(x => x.LinkCode == clean && x.LinkCodeExpires > now, ct)
            : null;
        if (m == null) throw new NotFoundException("Der Code ist unbekannt oder abgelaufen.");
        if (await _db.ClubMembers.AnyAsync(x => x.LinkedUserId == userId && x.Id != m.Id, ct))
            throw new ConflictException("Dieses Konto ist schon mit einem anderen Karteiblatt verknüpft.");
        if (m.LinkedUserId != null && m.LinkedUserId != userId)
            throw new ConflictException("Mit diesem Blatt ist schon ein anderes Konto verknüpft.");
        m.LinkedUserId = userId;
        m.LinkCode = null;
        m.LinkCodeExpires = null;
        await _db.SaveChangesAsync(ct);
        return new ClubLinkStateDto { Linked = true, FirstName = m.FirstName };
    }

    public async Task<ClubLinkStateDto> LinkStateAsync(int userId, CancellationToken ct = default)
    {
        var first = await _db.ClubMembers.Where(m => m.LinkedUserId == userId).Select(m => m.FirstName).FirstOrDefaultAsync(ct);
        return new ClubLinkStateDto { Linked = first != null, FirstName = first };
    }

    /// <summary>Das Konto trennt sich selbst — jederzeit, ohne Trainer.</summary>
    public async Task SelfUnlinkAsync(int userId, CancellationToken ct = default)
    {
        foreach (var m in await _db.ClubMembers.Where(m => m.LinkedUserId == userId).ToListAsync(ct))
            m.LinkedUserId = null;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Das verknüpfte Konto eines sichtbaren Kindes; <c>null</c> ohne Verknüpfung (oder Konto gelöscht).</summary>
    public async Task<(int UserId, string Username)?> LinkedAccountAsync(ClubActor actor, int memberId, CancellationToken ct = default)
    {
        var m = await LoadVisibleAsync(actor, memberId, ct);
        if (m.LinkedUserId is not { } uid) return null;
        var name = await _db.AppUsers.Where(u => u.Id == uid && u.DeletedAt == null).Select(u => u.Username).FirstOrDefaultAsync(ct);
        return name == null ? null : (uid, name);
    }

    internal static string NormalizeLinkCode(string? code) =>
        new((code ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string NewLinkCode() =>
        string.Create(LinkCodeLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = LinkAlphabet[RandomNumberGenerator.GetInt32(LinkAlphabet.Length)];
        });

    // ---- Gruppen ------------------------------------------------------------------------------

    public async Task<List<ClubGroupListDto>> ListGroupsAsync(ClubActor actor, CancellationToken ct = default)
    {
        Require(actor);
        var query = actor.Manager ? _db.ClubGroups : _db.ClubGroups.Where(g => g.Trainers.Any(t => t.UserId == actor.UserId));
        var groups = await query.Include(g => g.Trainers).OrderBy(g => g.Archived).ThenBy(g => g.Name).AsNoTracking().ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var memberCounts = await _db.ClubGroupMembers.Where(gm => ids.Contains(gm.GroupId) && !gm.Member!.Archived)
            .GroupBy(gm => gm.GroupId).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var sessions = await _db.ClubSessions.Where(s => ids.Contains(s.GroupId))
            .GroupBy(s => s.GroupId).Select(x => new { x.Key, Count = x.Count(), Last = x.Max(s => s.Date) })
            .ToDictionaryAsync(x => x.Key, ct);
        var trainers = await UsernamesAsync(groups.SelectMany(g => g.Trainers).Select(t => t.UserId), ct);
        return groups.Select(g =>
        {
            sessions.TryGetValue(g.Id, out var s);
            return FillGroup(new ClubGroupListDto(), g, memberCounts.GetValueOrDefault(g.Id), s?.Count ?? 0,
                s == null ? null : Iso(s.Last), trainers);
        }).ToList();
    }

    public async Task<ClubGroupDto> GetGroupAsync(ClubActor actor, int id, int take = DefaultSessionWindow, CancellationToken ct = default)
    {
        var group = await LoadGroupAsync(actor, id, ct);
        take = Math.Clamp(take, 1, MaxSessionWindow);

        var members = await _db.ClubGroupMembers.Where(gm => gm.GroupId == id && !gm.Member!.Archived)
            .Select(gm => gm.Member!).OrderBy(SortName).ThenBy(m => m.FirstName).AsNoTracking().ToListAsync(ct);
        var sessionCount = await _db.ClubSessions.CountAsync(s => s.GroupId == id, ct);
        var sessions = (await _db.ClubSessions.Where(s => s.GroupId == id).OrderByDescending(s => s.Date).Take(take)
            .AsNoTracking().ToListAsync(ct)).OrderBy(s => s.Date).ToList();
        // Die Quote zählt über ALLE Einheiten der Gruppe, die Tabelle zeigt nur die jüngsten.
        var all = await _db.ClubAttendances.Where(a => a.Session!.GroupId == id)
            .Select(a => new { a.SessionId, a.MemberId, a.Status }).ToListAsync(ct);
        var bySession = all.ToLookup(a => a.SessionId);
        var byMember = all.ToLookup(a => a.MemberId);
        var trainers = await UsernamesAsync(group.Trainers.Select(t => t.UserId), ct);

        var dto = FillGroup(new ClubGroupDto(), group, members.Count, sessionCount,
            sessions.Count == 0 ? null : Iso(sessions[^1].Date), trainers);
        dto.CanManage = actor.Manager;
        dto.Sessions = sessions.Select(s => SessionDto(new ClubSessionDto(), s, bySession[s.Id].Select(a => a.Status))).ToList();
        dto.Members = members.Select(m =>
        {
            var mine = byMember[m.Id].ToDictionary(a => a.SessionId, a => a.Status);
            return new ClubGroupMemberRowDto
            {
                Id = m.Id, FirstName = m.FirstName, LastName = m.LastName, BirthYear = m.BirthYear, Level = m.Level,
                Statuses = sessions.Select(s => mine.TryGetValue(s.Id, out var st) ? StatusKey(st) : null).ToList(),
                Present = mine.Values.Count(s => s == ClubAttendanceStatus.Present),
                Recorded = mine.Count,
            };
        }).ToList();
        return dto;
    }

    public async Task<ClubGroupDto> CreateGroupAsync(ClubActor actor, ClubGroupInputDto input, CancellationToken ct = default)
    {
        RequireManager(actor);
        var group = new ClubGroup { CreatedAt = _utcNow() };
        ApplyGroup(group, input);
        _db.ClubGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        return await GetGroupAsync(actor, group.Id, ct: ct);
    }

    public async Task<ClubGroupDto> UpdateGroupAsync(ClubActor actor, int id, ClubGroupInputDto input, CancellationToken ct = default)
    {
        RequireManager(actor);
        var group = await LoadGroupAsync(actor, id, ct);
        ApplyGroup(group, input);
        await _db.SaveChangesAsync(ct);
        return await GetGroupAsync(actor, id, ct: ct);
    }

    /// <summary>Die Gruppe samt Einheiten und Anwesenheit — die Kinder bleiben in der Kartei.</summary>
    public async Task DeleteGroupAsync(ClubActor actor, int id, CancellationToken ct = default)
    {
        RequireManager(actor);
        var group = await LoadGroupAsync(actor, id, ct);
        var sessions = await _db.ClubSessions.Where(s => s.GroupId == id).ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        _db.ClubAttendances.RemoveRange(await _db.ClubAttendances.Where(a => sessionIds.Contains(a.SessionId)).ToListAsync(ct));
        _db.ClubSessions.RemoveRange(sessions);
        _db.ClubGroupMembers.RemoveRange(await _db.ClubGroupMembers.Where(gm => gm.GroupId == id).ToListAsync(ct));
        _db.ClubGroupTrainers.RemoveRange(group.Trainers);
        _db.ClubGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
    }

    private static void ApplyGroup(ClubGroup group, ClubGroupInputDto input)
    {
        group.Name = Clean(input.Name) ?? throw new DomainValidationException("Der Name der Gruppe fehlt.");
        group.Schedule = Clean(input.Schedule);
        if (input.Weekday is < 1 or > 7) throw new DomainValidationException("Der Wochentag ist ungültig.");
        group.Weekday = input.Weekday;
        group.Archived = input.Archived;
    }

    /// <summary>
    /// Ein Konto als Trainer zuteilen (über den Benutzernamen). Die Zuteilung allein öffnet ClubHub nicht — das Konto
    /// braucht dazu eine Rolle mit <see cref="Permissions.ClubTrainer"/>.
    /// </summary>
    public async Task<ClubGroupDto> AddTrainerAsync(ClubActor actor, int groupId, string username, CancellationToken ct = default)
    {
        RequireManager(actor);
        var group = await LoadGroupAsync(actor, groupId, ct);
        var name = (username ?? "").Trim().ToLower();
        var userId = await _db.AppUsers.Where(u => u.DeletedAt == null && u.Username.ToLower() == name)
            .Select(u => (int?)u.Id).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Ein Konto mit diesem Benutzernamen gibt es nicht.");
        if (group.Trainers.All(t => t.UserId != userId))
        {
            _db.ClubGroupTrainers.Add(new ClubGroupTrainer { GroupId = groupId, UserId = userId });
            await _db.SaveChangesAsync(ct);
        }
        return await GetGroupAsync(actor, groupId, ct: ct);
    }

    public async Task<ClubGroupDto> RemoveTrainerAsync(ClubActor actor, int groupId, int userId, CancellationToken ct = default)
    {
        RequireManager(actor);
        var group = await LoadGroupAsync(actor, groupId, ct);
        _db.ClubGroupTrainers.RemoveRange(group.Trainers.Where(t => t.UserId == userId));
        await _db.SaveChangesAsync(ct);
        return await GetGroupAsync(actor, groupId, ct: ct);
    }

    /// <summary>Ein (für den Aufrufer sichtbares) Kind in die Gruppe nehmen.</summary>
    public async Task<ClubGroupDto> AddGroupMemberAsync(ClubActor actor, int groupId, int memberId, CancellationToken ct = default)
    {
        await LoadGroupAsync(actor, groupId, ct);
        var m = await LoadVisibleAsync(actor, memberId, ct);
        if (m.Groups.All(g => g.GroupId != groupId))
        {
            _db.ClubGroupMembers.Add(new ClubGroupMember { GroupId = groupId, MemberId = m.Id });
            await _db.SaveChangesAsync(ct);
        }
        return await GetGroupAsync(actor, groupId, ct: ct);
    }

    /// <summary>Aus der Gruppe nehmen — das Blatt und die erfasste Anwesenheit bleiben.</summary>
    public async Task<ClubGroupDto> RemoveGroupMemberAsync(ClubActor actor, int groupId, int memberId, CancellationToken ct = default)
    {
        await LoadGroupAsync(actor, groupId, ct);
        _db.ClubGroupMembers.RemoveRange(await _db.ClubGroupMembers.Where(gm => gm.GroupId == groupId && gm.MemberId == memberId).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
        return await GetGroupAsync(actor, groupId, ct: ct);
    }

    private static T FillGroup<T>(T dto, ClubGroup g, int memberCount, int sessionCount, string? lastSession,
        Dictionary<int, string> usernames) where T : ClubGroupListDto
    {
        dto.Id = g.Id;
        dto.Name = g.Name;
        dto.Schedule = g.Schedule;
        dto.Weekday = g.Weekday;
        dto.Archived = g.Archived;
        dto.MemberCount = memberCount;
        dto.SessionCount = sessionCount;
        dto.LastSession = lastSession;
        dto.Trainers = g.Trainers.Where(t => usernames.ContainsKey(t.UserId))
            .Select(t => new ClubTrainerDto { UserId = t.UserId, Username = usernames[t.UserId] })
            .OrderBy(t => t.Username, StringComparer.CurrentCultureIgnoreCase).ToList();
        return dto;
    }

    // ---- Einheiten und Anwesenheit ------------------------------------------------------------

    /// <summary>
    /// Eine Einheit speichern. Ohne <paramref name="sessionId"/>: die Einheit dieses Tages anlegen oder ersetzen (je Gruppe
    /// und Tag gibt es eine). Mit: genau diese ändern, auch ihr Datum — 409, wenn an dem Tag schon eine andere steht.
    /// Anwesenheit zählt nur für Kinder der Gruppe; ein leerer Status nimmt den Eintrag zurück.
    /// </summary>
    public async Task<ClubSessionDetailDto> SaveSessionAsync(ClubActor actor, int groupId, ClubSessionInputDto input,
        int? sessionId = null, CancellationToken ct = default)
    {
        await LoadGroupAsync(actor, groupId, ct);
        if (!DateOnly.TryParseExact((input.Date ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new DomainValidationException("Das Datum der Einheit ist ungültig.");

        var sameDay = await _db.ClubSessions.Include(s => s.Attendance)
            .FirstOrDefaultAsync(s => s.GroupId == groupId && s.Date == date, ct);
        ClubSession session;
        if (sessionId is { } sid)
        {
            session = sameDay?.Id == sid ? sameDay
                : await _db.ClubSessions.Include(s => s.Attendance).FirstOrDefaultAsync(s => s.Id == sid && s.GroupId == groupId, ct)
                  ?? throw new NotFoundException("Diese Einheit gibt es nicht.");
            if (sameDay != null && sameDay.Id != sid)
                throw new ConflictException("An diesem Tag gibt es schon eine Einheit der Gruppe.");
            session.Date = date;
        }
        else if (sameDay != null)
        {
            session = sameDay;
        }
        else
        {
            session = new ClubSession { GroupId = groupId, Date = date, CreatedAt = _utcNow(), CreatedByUserId = actor.UserId };
            _db.ClubSessions.Add(session);
        }
        session.Topic = Clean(input.Topic);
        session.Notes = Clean(input.Notes);

        var inGroup = (await _db.ClubGroupMembers.Where(gm => gm.GroupId == groupId).Select(gm => gm.MemberId).ToListAsync(ct)).ToHashSet();
        foreach (var entry in (input.Attendance ?? []).Where(e => inGroup.Contains(e.MemberId)).GroupBy(e => e.MemberId).Select(g => g.Last()))
        {
            var status = ParseStatus(entry.Status);
            var row = session.Attendance.FirstOrDefault(a => a.MemberId == entry.MemberId);
            if (status == null)
            {
                if (row == null) continue;
                session.Attendance.Remove(row);
                _db.ClubAttendances.Remove(row);
            }
            else if (row == null)
            {
                session.Attendance.Add(new ClubAttendance { MemberId = entry.MemberId, Status = status.Value });
            }
            else
            {
                row.Status = status.Value;
            }
        }
        await _db.SaveChangesAsync(ct);
        return SessionDetail(session);
    }

    public async Task<ClubSessionDetailDto> GetSessionAsync(ClubActor actor, int sessionId, CancellationToken ct = default)
    {
        Require(actor);
        var session = await _db.ClubSessions.Include(s => s.Attendance).AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                      ?? throw new NotFoundException("Diese Einheit gibt es nicht.");
        await LoadGroupAsync(actor, session.GroupId, ct);      // fremde Gruppe → 404 wie unbekannt
        return SessionDetail(session);
    }

    /// <summary>
    /// Die Einheit eines Tages, <c>null</c> wenn es keine gibt. Die Anwesenheitsliste fragt danach, BEVOR sie speichert: ein
    /// Speichern ersetzt die Einheit des Tages, und ohne diesen Blick überschriebe eine leer geöffnete Liste eine alte.
    /// </summary>
    public async Task<ClubSessionDetailDto?> GetSessionByDateAsync(ClubActor actor, int groupId, string date, CancellationToken ct = default)
    {
        await LoadGroupAsync(actor, groupId, ct);
        if (!DateOnly.TryParseExact((date ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new DomainValidationException("Das Datum der Einheit ist ungültig.");
        var session = await _db.ClubSessions.Include(s => s.Attendance).AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId && s.Date == day, ct);
        return session == null ? null : SessionDetail(session);
    }

    public async Task DeleteSessionAsync(ClubActor actor, int sessionId, CancellationToken ct = default)
    {
        Require(actor);
        var session = await _db.ClubSessions.Include(s => s.Attendance).FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                      ?? throw new NotFoundException("Diese Einheit gibt es nicht.");
        await LoadGroupAsync(actor, session.GroupId, ct);
        _db.ClubAttendances.RemoveRange(session.Attendance);
        _db.ClubSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
    }

    private static ClubSessionDetailDto SessionDetail(ClubSession s)
    {
        var dto = SessionDto(new ClubSessionDetailDto(), s, s.Attendance.Select(a => a.Status));
        dto.GroupId = s.GroupId;
        dto.Attendance = s.Attendance.OrderBy(a => a.MemberId)
            .Select(a => new ClubAttendanceInputDto { MemberId = a.MemberId, Status = StatusKey(a.Status) }).ToList();
        return dto;
    }

    private static T SessionDto<T>(T dto, ClubSession s, IEnumerable<ClubAttendanceStatus> statuses) where T : ClubSessionDto
    {
        var list = statuses.ToList();
        dto.Id = s.Id;
        dto.Date = Iso(s.Date);
        dto.Topic = s.Topic;
        dto.Notes = s.Notes;
        dto.Present = list.Count(x => x == ClubAttendanceStatus.Present);
        dto.Absent = list.Count(x => x == ClubAttendanceStatus.Absent);
        return dto;
    }

    internal static ClubAttendanceStatus? ParseStatus(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "" => null,
        "present" => ClubAttendanceStatus.Present,
        "absent" => ClubAttendanceStatus.Absent,
        _ => throw new DomainValidationException("Unbekannter Anwesenheits-Status."),
    };

    internal static string? StatusKey(ClubAttendanceStatus? status) => status switch
    {
        ClubAttendanceStatus.Present => "present",
        ClubAttendanceStatus.Absent => "absent",
        _ => null,
    };

    // ---- Kleinkram ----------------------------------------------------------------------------

    private async Task<Dictionary<int, string>> GroupNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new()
            : await _db.ClubGroups.Where(g => list.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
    }

    /// <summary>Benutzernamen lebender Konten — ein gelöschtes Konto fehlt im Ergebnis.</summary>
    private async Task<Dictionary<int, string>> UsernamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new()
            : await _db.AppUsers.Where(u => list.Contains(u.Id) && u.DeletedAt == null).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
