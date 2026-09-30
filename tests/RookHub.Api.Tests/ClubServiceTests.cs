using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Club;

namespace RookHub.Api.Tests;

/// <summary>
/// ClubHub — Kartei der Kinder und Jugendlichen (Wunsch 2026-09-30): Blätter mit mehreren Telefonnummern/E-Mail-Adressen
/// samt Hinweis („Mutter Daniela"), Gruppen mit Anwesenheit, Lernstand, Verknüpfung mit einem Konto. Die eine Regel, an der
/// alles hängt, ist die Sichtbarkeit: Leitung alles, Trainer nur die Kinder seiner Gruppen.
/// </summary>
public class ClubServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTime _now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private ClubService Svc() => new(_db, () => _now);
    public void Dispose() => _db.Dispose();

    private static ClubActor Manager(int userId = 1) => new(userId, true, false);
    private static ClubActor Trainer(int userId) => new(userId, false, true);

    private async Task<int> UserAsync(string name)
    {
        var u = new AppUser { Username = name, PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> GroupAsync(string name, params int[] trainers)
    {
        var g = await Svc().CreateGroupAsync(Manager(), new ClubGroupInputDto { Name = name });
        foreach (var t in trainers)
            _db.ClubGroupTrainers.Add(new ClubGroupTrainer { GroupId = g.Id, UserId = t });
        await _db.SaveChangesAsync();
        return g.Id;
    }

    private static ClubMemberInputDto Kid(string first, string last, params int[] groups) =>
        new() { FirstName = first, LastName = last, GroupIds = groups.ToList() };

    // ---- Kontakte -----------------------------------------------------------------------------

    [Fact]
    public async Task Create_KeepsSeveralPhonesAndMailsWithTheirHints_InTheOrderGiven_AndDropsEmptyRows()
    {
        var input = Kid("Daniel", "Huber");
        input.Contacts =
        [
            new() { Kind = "phone", Value = " +43 660 1234567 ", Label = " Mutter Daniela " },
            new() { Kind = "phone", Value = "0512/58 12 34", Label = "Vater Franz" },
            new() { Kind = "phone", Value = "   ", Label = "leer gelassene Zeile" },
            new() { Kind = "email", Value = "daniela@example.org", Label = "Mutter Daniela" },
            new() { Kind = "EMAIL", Value = "opa@example.org" },
        ];

        var dto = await Svc().CreateMemberAsync(Manager(), input);

        Assert.Equal(
            [("phone", "+43 660 1234567", "Mutter Daniela"), ("phone", "0512/58 12 34", "Vater Franz"),
             ("email", "daniela@example.org", "Mutter Daniela"), ("email", "opa@example.org", null)],
            dto.Contacts.Select(c => (c.Kind, c.Value, c.Label)));
        // Die Liste ist zugleich die Telefonliste: dieselben Kontakte, dieselbe Reihenfolge.
        var row = Assert.Single(await Svc().ListMembersAsync(Manager(), null, false));
        Assert.Equal(dto.Contacts.Select(c => c.Value), row.Contacts.Select(c => c.Value));
    }

    [Theory]
    [InlineData("phone", "ruf mich an")]
    [InlineData("phone", "12")]
    [InlineData("email", "daniela.example.org")]
    [InlineData("fax", "0512 581234")]
    public async Task Create_RejectsAContactThatIsNeitherNumberNorAddress(string kind, string value)
    {
        var input = Kid("Daniel", "Huber");
        input.Contacts = [new() { Kind = kind, Value = value }];
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateMemberAsync(Manager(), input));
        Assert.Empty(_db.ClubMembers);
    }

    [Fact]
    public async Task Update_ReplacesTheContacts_WithoutLeavingOldRows()
    {
        var input = Kid("Daniel", "Huber");
        input.Contacts = [new() { Kind = "phone", Value = "0660 111", Label = "Mutter" }, new() { Kind = "phone", Value = "0660 222", Label = "Vater" }];
        var dto = await Svc().CreateMemberAsync(Manager(), input);

        input.Contacts = [new() { Kind = "phone", Value = "0660 333", Label = "Oma" }];
        var updated = await Svc().UpdateMemberAsync(Manager(), dto.Id, input);

        Assert.Equal(["0660 333"], updated.Contacts.Select(c => c.Value));
        Assert.Equal("0660 333", Assert.Single(_db.ClubContacts).Value);
    }

    // ---- Stammdaten ---------------------------------------------------------------------------

    [Fact]
    public async Task BirthDate_SetsTheYear_AndAYearAloneIsEnough()
    {
        var full = Kid("Anna", "Moser");
        full.BirthDate = "2015-03-12";
        full.BirthYear = 1999;                                   // das Datum gewinnt
        var a = await Svc().CreateMemberAsync(Manager(), full);
        Assert.Equal(("2015-03-12", 2015), (a.BirthDate, a.BirthYear));

        var yearOnly = Kid("Ben", "Moser");
        yearOnly.BirthYear = 2016;
        var b = await Svc().CreateMemberAsync(Manager(), yearOnly);
        Assert.Equal((null, 2016), (b.BirthDate, b.BirthYear));
    }

    [Theory]
    [InlineData("2027-01-01", null)]      // in der Zukunft
    [InlineData("12.03.2015", null)]      // nicht ISO
    [InlineData(null, 2027)]
    [InlineData(null, 1899)]
    public async Task BirthDate_RejectsNonsense(string? date, int? year)
    {
        var input = Kid("Anna", "Moser");
        input.BirthDate = date;
        input.BirthYear = year;
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateMemberAsync(Manager(), input));
    }

    [Fact]
    public async Task FirstNameIsRequired_TheLastNameIsNot_TheTrainerOftenDoesNotKnowIt()
    {
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateMemberAsync(Manager(), Kid("  ", "Huber")));
        var dto = await Svc().CreateMemberAsync(Manager(), Kid(" Daniel ", " Huber "));
        Assert.Equal(("Daniel", "Huber"), (dto.FirstName, dto.LastName));

        var noLast = await Svc().CreateMemberAsync(Manager(), new ClubMemberInputDto { FirstName = "Emil", LastName = null });
        Assert.Equal(("Emil", ""), (noLast.FirstName, noLast.LastName));
        await Svc().CreateMemberAsync(Manager(), Kid("Anna", "  "));
        await Svc().CreateMemberAsync(Manager(), Kid("Zoe", "Auer"));
        // Geordnet wird nach dem Nachnamen — ohne ihn nach dem Vornamen: Anna, Auer, (Emil), Huber.
        Assert.Equal(["Anna", "Zoe", "Emil", "Daniel"], (await Svc().ListMembersAsync(Manager(), null, false)).Select(m => m.FirstName));
        // Nachtragen und wieder leeren geht beides.
        var later = await Svc().UpdateMemberAsync(Manager(), noLast.Id, Kid("Emil", "Moser"));
        Assert.Equal("Moser", later.LastName);
        Assert.Equal("", (await Svc().UpdateMemberAsync(Manager(), noLast.Id, Kid("Emil", ""))).LastName);
    }

    [Fact]
    public async Task List_FiltersByGroup_AndHidesArchived()
    {
        var g = await GroupAsync("Anfänger");
        await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber", g));
        await Svc().CreateMemberAsync(Manager(), Kid("Anna", "Moser"));
        var gone = Kid("Carla", "Auer", g);
        gone.Archived = true;
        await Svc().CreateMemberAsync(Manager(), gone);

        Assert.Equal(["Huber", "Moser"], (await Svc().ListMembersAsync(Manager(), null, false)).Select(m => m.LastName));
        Assert.Equal(["Huber"], (await Svc().ListMembersAsync(Manager(), g, false)).Select(m => m.LastName));
        Assert.Equal(["Auer"], (await Svc().ListMembersAsync(Manager(), null, true)).Select(m => m.LastName));
    }

    // ---- Sichtbarkeit -------------------------------------------------------------------------

    [Fact]
    public async Task WithoutAClubRight_EverythingIsForbidden()
    {
        var nobody = new ClubActor(7, false, false);
        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().ListMembersAsync(nobody, null, false));
        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().ListGroupsAsync(nobody));
        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().CreateMemberAsync(nobody, Kid("A", "B")));
    }

    [Fact]
    public async Task Trainer_SeesOnlyTheChildrenOfHisGroups_AndAForeignChildLooksUnknown()
    {
        var (tina, tom) = (await UserAsync("tina"), await UserAsync("tom"));
        var (mine, other) = (await GroupAsync("Anfänger", tina), await GroupAsync("Turnier", tom));
        var own = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber", mine));
        var foreign = await Svc().CreateMemberAsync(Manager(), Kid("Anna", "Moser", other));
        await Svc().CreateMemberAsync(Manager(), Kid("Ohne", "Gruppe"));

        Assert.Equal(["Huber"], (await Svc().ListMembersAsync(Trainer(tina), null, false)).Select(m => m.LastName));
        Assert.Equal(["Anfänger"], (await Svc().ListGroupsAsync(Trainer(tina))).Select(g => g.Name));
        Assert.Equal("Daniel", (await Svc().GetMemberAsync(Trainer(tina), own.Id)).FirstName);
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().GetMemberAsync(Trainer(tina), foreign.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().UpdateMemberAsync(Trainer(tina), foreign.Id, Kid("X", "Y", mine)));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().AddNoteAsync(Trainer(tina), foreign.Id, "neugierig"));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().GetGroupAsync(Trainer(tina), other));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().SaveSessionAsync(Trainer(tina), other, new ClubSessionInputDto { Date = "2026-09-25" }));
        // Auch der Filter auf eine fremde Gruppe verrät nichts.
        Assert.Empty(await Svc().ListMembersAsync(Trainer(tina), other, false));
    }

    [Fact]
    public async Task Trainer_CreatesChildrenOnlyInHisOwnGroups_AndLeavesForeignMembershipsAlone()
    {
        var (tina, tom) = (await UserAsync("tina"), await UserAsync("tom"));
        var (mine, other) = (await GroupAsync("Anfänger", tina), await GroupAsync("Turnier", tom));

        // Ohne eigene Gruppe legte er ein Blatt an, das er selbst nicht sieht.
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateMemberAsync(Trainer(tina), Kid("A", "B")));
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateMemberAsync(Trainer(tina), Kid("A", "B", other)));
        Assert.Empty(_db.ClubMembers);

        var kid = await Svc().CreateMemberAsync(Trainer(tina), Kid("Daniel", "Huber", mine, other));
        Assert.Equal(["Anfänger"], kid.Groups.Select(g => g.Name));          // die fremde Gruppe wurde nicht gesetzt

        await Svc().AddGroupMemberAsync(Manager(), other, kid.Id);             // die Leitung nimmt das Kind zusätzlich dorthin
        var updated = await Svc().UpdateMemberAsync(Trainer(tina), kid.Id, Kid("Daniel", "Huber", mine));
        Assert.Equal(["Anfänger", "Turnier"], updated.Groups.Select(g => g.Name));   // bleibt, obwohl nicht mitgeschickt

        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().UpdateMemberAsync(Trainer(tina), kid.Id, Kid("Daniel", "Huber")));
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateMemberAsync(Manager(), Kid("A", "B", 9999)));
    }

    [Fact]
    public async Task Delete_IsForTheManagement_AndTakesContactsNotesAndAttendanceAlong()
    {
        var tina = await UserAsync("tina");
        var g = await GroupAsync("Anfänger", tina);
        var input = Kid("Daniel", "Huber", g);
        input.Contacts = [new() { Kind = "phone", Value = "0660 111", Label = "Mutter" }];
        var kid = await Svc().CreateMemberAsync(Manager(), input);
        await Svc().AddNoteAsync(Trainer(tina), kid.Id, "kann die Gabel");
        await Svc().SaveSessionAsync(Trainer(tina), g, new ClubSessionInputDto
        { Date = "2026-09-25", Attendance = [new() { MemberId = kid.Id, Status = "present" }] });

        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().DeleteMemberAsync(Trainer(tina), kid.Id));
        await Svc().DeleteMemberAsync(Manager(), kid.Id);

        Assert.Empty(_db.ClubMembers);
        Assert.Empty(_db.ClubContacts);
        Assert.Empty(_db.ClubNotes);
        Assert.Empty(_db.ClubAttendances);
        Assert.Empty(_db.ClubGroupMembers);
        Assert.Single(_db.ClubSessions);                                       // die Einheit gehört der Gruppe
    }

    // ---- Notizen ------------------------------------------------------------------------------

    [Fact]
    public async Task Notes_AreDatedAndSigned_NewestFirst_AndOnlyTheAuthorOrTheManagementDeletes()
    {
        var (tina, tom) = (await UserAsync("tina"), await UserAsync("tom"));
        var g = await GroupAsync("Anfänger", tina, tom);
        var kid = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber", g));

        await Svc().AddNoteAsync(Trainer(tina), kid.Id, " Bauerndiplom bestanden ");
        _now = _now.AddDays(7);
        var dto = await Svc().AddNoteAsync(Trainer(tom), kid.Id, "rechnet zwei Züge");
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().AddNoteAsync(Trainer(tom), kid.Id, "  "));

        Assert.Equal([("rechnet zwei Züge", "tom", true), ("Bauerndiplom bestanden", "tina", false)],
            dto.NoteEntries.Select(n => (n.Text, n.Author, n.CanDelete)));
        var tinas = dto.NoteEntries[1].Id;
        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().DeleteNoteAsync(Trainer(tom), kid.Id, tinas));
        var after = await Svc().DeleteNoteAsync(Manager(), kid.Id, tinas);
        Assert.Equal(["rechnet zwei Züge"], after.NoteEntries.Select(n => n.Text));
    }

    // ---- Konto verknüpfen ---------------------------------------------------------------------

    [Fact]
    public async Task Link_TheAccountRedeemsTheCode_AndThenOnlyOneSheetPerAccount()
    {
        var child = await UserAsync("daniel2015");
        var kid = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber"));
        var other = await Svc().CreateMemberAsync(Manager(), Kid("Anna", "Moser"));

        var code = await Svc().CreateLinkCodeAsync(Manager(), kid.Id);
        Assert.Equal(ClubService.LinkCodeLength, code.Code.Length);
        Assert.Equal(_now + ClubService.LinkCodeLifetime, code.Expires);
        Assert.Equal(code.Code, (await Svc().GetMemberAsync(Manager(), kid.Id)).LinkCode);
        Assert.False((await Svc().LinkStateAsync(child)).Linked);

        // Abgetippt: klein, mit Bindestrich und Leerzeichen.
        var typed = $" {code.Code[..5].ToLowerInvariant()}-{code.Code[5..].ToLowerInvariant()} ";
        var state = await Svc().RedeemAsync(child, typed);
        Assert.Equal((true, "Daniel"), (state.Linked, state.FirstName));

        var dto = await Svc().GetMemberAsync(Manager(), kid.Id);
        Assert.Equal(("daniel2015", true, null), (dto.LinkedUsername, dto.Linked, dto.LinkCode));
        Assert.Equal((child, "daniel2015"), await Svc().LinkedAccountAsync(Manager(), kid.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().RedeemAsync(child, code.Code));         // Einmal-Code
        await Assert.ThrowsAsync<ConflictException>(() => Svc().CreateLinkCodeAsync(Manager(), kid.Id));

        var second = await Svc().CreateLinkCodeAsync(Manager(), other.Id);
        await Assert.ThrowsAsync<ConflictException>(() => Svc().RedeemAsync(child, second.Code));       // schon an einem Blatt
        Assert.Null((await Svc().GetMemberAsync(Manager(), other.Id)).LinkedUsername);
    }

    [Fact]
    public async Task Link_ExpiredOrUnknownCode_IsTheSameError_AndBothSidesCanSeparate()
    {
        var child = await UserAsync("daniel2015");
        var kid = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber"));
        var code = await Svc().CreateLinkCodeAsync(Manager(), kid.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => Svc().RedeemAsync(child, "ABCDEFGHJK"));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().RedeemAsync(child, ""));
        _now += ClubService.LinkCodeLifetime + TimeSpan.FromMinutes(1);
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().RedeemAsync(child, code.Code));
        Assert.Null((await Svc().GetMemberAsync(Manager(), kid.Id)).LinkCode);                          // abgelaufen = nicht mehr gezeigt

        var fresh = await Svc().CreateLinkCodeAsync(Manager(), kid.Id);
        await Svc().RedeemAsync(child, fresh.Code);
        await Svc().SelfUnlinkAsync(child);                                                             // das Konto trennt sich selbst
        Assert.False((await Svc().LinkStateAsync(child)).Linked);
        Assert.Null(await Svc().LinkedAccountAsync(Manager(), kid.Id));

        await Svc().RedeemAsync(child, (await Svc().CreateLinkCodeAsync(Manager(), kid.Id)).Code);
        Assert.False((await Svc().UnlinkAsync(Manager(), kid.Id)).Linked);                              // oder der Trainer
        Assert.False((await Svc().LinkStateAsync(child)).Linked);
    }

    [Fact]
    public async Task DeletingTheAccount_LoosensTheLink_AndRemovesTrainerAssignments_TheSheetStays()
    {
        var user = new AppUser { Username = "tina", Email = "tina@t.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw") };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var g = await GroupAsync("Anfänger", user.Id);
        var kid = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber", g));
        await Svc().RedeemAsync(user.Id, (await Svc().CreateLinkCodeAsync(Manager(), kid.Id)).Code);
        await Svc().AddNoteAsync(Trainer(user.Id), kid.Id, "kann die Gabel");

        await TestServices.Profile(_db, new NoOpTaskQueue()).DeleteAccountAsync(user.Id, "pw");

        Assert.Empty(_db.ClubGroupTrainers);
        var dto = await Svc().GetMemberAsync(Manager(), kid.Id);
        Assert.Equal((false, null), (dto.Linked, dto.LinkedUsername));
        var note = Assert.Single(dto.NoteEntries);                                                      // die Notiz bleibt, ohne Namen
        Assert.Equal(("kann die Gabel", null), (note.Text, note.Author));
    }

    // ---- Gruppen ------------------------------------------------------------------------------

    [Fact]
    public async Task Groups_OnlyTheManagementCreatesAndAssignsTrainers_ByUsername()
    {
        var tina = await UserAsync("Tina");
        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().CreateGroupAsync(Trainer(tina), new ClubGroupInputDto { Name = "X" }));
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateGroupAsync(Manager(), new ClubGroupInputDto { Name = " " }));

        // Trainingstag 5 = Freitag: daran hängt der Vorschlag „heute abhaken" in der Oberfläche.
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().CreateGroupAsync(Manager(), new ClubGroupInputDto { Name = "X", Weekday = 8 }));
        var g = await Svc().CreateGroupAsync(Manager(), new ClubGroupInputDto { Name = " Anfänger ", Schedule = "17:00, Vereinsheim", Weekday = 5 });
        Assert.Equal(("Anfänger", "17:00, Vereinsheim", 5, true), (g.Name, g.Schedule, g.Weekday, g.CanManage));
        Assert.Equal(5, Assert.Single(await Svc().ListGroupsAsync(Manager())).Weekday);

        await Assert.ThrowsAsync<NotFoundException>(() => Svc().AddTrainerAsync(Manager(), g.Id, "niemand"));
        var with = await Svc().AddTrainerAsync(Manager(), g.Id, " tina ");                              // Groß/klein egal
        with = await Svc().AddTrainerAsync(Manager(), g.Id, "Tina");                                     // zweimal = einmal
        Assert.Equal(["Tina"], with.Trainers.Select(t => t.Username));
        Assert.False((await Svc().GetGroupAsync(Trainer(tina), g.Id)).CanManage);
        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().AddTrainerAsync(Trainer(tina), g.Id, "Tina"));
        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().UpdateGroupAsync(Trainer(tina), g.Id, new ClubGroupInputDto { Name = "Y" }));

        // Ein Kind aus der Gruppe nehmen: das Blatt bleibt, nur die Zugehörigkeit geht.
        var kid = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber", g.Id));
        Assert.Single((await Svc().GetGroupAsync(Trainer(tina), g.Id)).Members);
        Assert.Empty((await Svc().RemoveGroupMemberAsync(Trainer(tina), g.Id, kid.Id)).Members);
        Assert.Empty((await Svc().GetMemberAsync(Manager(), kid.Id)).Groups);

        Assert.Empty((await Svc().RemoveTrainerAsync(Manager(), g.Id, tina)).Trainers);
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().GetGroupAsync(Trainer(tina), g.Id));
    }

    [Fact]
    public async Task DeleteGroup_RemovesSessionsAndAttendance_ButKeepsTheChildren()
    {
        var g = await GroupAsync("Anfänger");
        var kid = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber", g));
        await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        { Date = "2026-09-25", Attendance = [new() { MemberId = kid.Id, Status = "present" }] });

        await Svc().DeleteGroupAsync(Manager(), g);

        Assert.Empty(_db.ClubGroups);
        Assert.Empty(_db.ClubSessions);
        Assert.Empty(_db.ClubAttendances);
        Assert.Empty((await Svc().GetMemberAsync(Manager(), kid.Id)).Groups);
    }

    // ---- Einheiten und Anwesenheit ------------------------------------------------------------

    [Fact]
    public async Task Session_OnePerGroupAndDay_SavingAgainReplaces_AndAnEmptyStatusTakesTheEntryBack()
    {
        var g = await GroupAsync("Anfänger");
        var a = await Svc().CreateMemberAsync(Manager(), Kid("Anna", "Auer", g));
        var b = await Svc().CreateMemberAsync(Manager(), Kid("Ben", "Berger", g));
        var outsider = await Svc().CreateMemberAsync(Manager(), Kid("Carla", "Fremd"));

        var first = await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        {
            Date = "2026-09-25", Topic = " Gabel ",
            Attendance = [new() { MemberId = a.Id, Status = "present" }, new() { MemberId = b.Id, Status = "absent" },
                          new() { MemberId = outsider.Id, Status = "present" }],                         // nicht in der Gruppe → zählt nicht
        });
        Assert.Equal(("Gabel", 1, 1), (first.Topic, first.Present, first.Absent));

        var again = await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        {
            Date = "2026-09-25", Topic = "Gabel und Spieß",
            Attendance = [new() { MemberId = a.Id, Status = "" }, new() { MemberId = b.Id, Status = "absent" }],
        });
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(("Gabel und Spieß", 0, 1), (again.Topic, again.Present, again.Absent));
        Assert.Single(_db.ClubSessions);
        Assert.Equal((b.Id, ClubAttendanceStatus.Absent), Assert.Single(_db.ClubAttendances.Select(x => new { x.MemberId, x.Status }).ToList()
            .Select(x => (x.MemberId, x.Status))));

        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto { Date = "25.09.2026" }));
        // „entschuldigt" gibt es nicht mehr (Wunsch 2026-09-30) — da oder nicht da.
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        { Date = "2026-09-26", Attendance = [new() { MemberId = a.Id, Status = "excused" }] }));
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        { Date = "2026-09-26", Attendance = [new() { MemberId = a.Id, Status = "vielleicht" }] }));
    }

    [Fact]
    public async Task Session_EditById_MovesTheDate_ButNotOntoAnotherSession()
    {
        var g = await GroupAsync("Anfänger");
        var s1 = await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto { Date = "2026-09-18" });
        var s2 = await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto { Date = "2026-09-25" });

        var moved = await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto { Date = "2026-09-19", Topic = "verschoben" }, s1.Id);
        Assert.Equal((s1.Id, "2026-09-19", "verschoben"), (moved.Id, moved.Date, moved.Topic));
        await Assert.ThrowsAsync<ConflictException>(() => Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto { Date = "2026-09-25" }, s1.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto { Date = "2026-10-02" }, 9999));

        Assert.Equal("2026-09-25", (await Svc().GetSessionAsync(Manager(), s2.Id)).Date);
        // Die Anwesenheitsliste fragt nach dem TAG, bevor sie speichert — sonst überschriebe eine leer geöffnete eine alte.
        Assert.Equal(s2.Id, (await Svc().GetSessionByDateAsync(Manager(), g, "2026-09-25"))!.Id);
        Assert.Null(await Svc().GetSessionByDateAsync(Manager(), g, "2026-09-26"));
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc().GetSessionByDateAsync(Manager(), g, "heute"));
        await Svc().DeleteSessionAsync(Manager(), s2.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().GetSessionAsync(Manager(), s2.Id));
    }

    [Fact]
    public async Task GroupDetail_ShowsTheAttendanceTable_OldestSessionFirst_WithEachChildsRate()
    {
        var g = await GroupAsync("Anfänger");
        var a = await Svc().CreateMemberAsync(Manager(), Kid("Anna", "Auer", g));
        var b = await Svc().CreateMemberAsync(Manager(), Kid("Ben", "Berger", g));
        var archived = Kid("Carla", "Weg", g);
        archived.Archived = true;
        await Svc().CreateMemberAsync(Manager(), archived);

        await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        { Date = "2026-09-25", Topic = "Gabel", Notes = " Arbeitsblatt 3, zum Schluss Simultan ",
          Attendance = [new() { MemberId = a.Id, Status = "present" }, new() { MemberId = b.Id, Status = "absent" }] });
        await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        { Date = "2026-09-11", Attendance = [new() { MemberId = a.Id, Status = "present" }] });
        await Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        { Date = "2026-09-18", Attendance = [new() { MemberId = a.Id, Status = "absent" }, new() { MemberId = b.Id, Status = "present" }] });

        var dto = await Svc().GetGroupAsync(Manager(), g);
        Assert.Equal(["2026-09-11", "2026-09-18", "2026-09-25"], dto.Sessions.Select(s => s.Date));
        // Thema und „was wurde gemacht" reisen mit jeder Einheit — daraus wird das Trainingstagebuch der Gruppe.
        Assert.Equal(("Gabel", "Arbeitsblatt 3, zum Schluss Simultan", 1, 1), (dto.Sessions[2].Topic, dto.Sessions[2].Notes, dto.Sessions[2].Present, dto.Sessions[2].Absent));
        Assert.Equal((null, null), (dto.Sessions[0].Topic, dto.Sessions[0].Notes));
        Assert.Equal((2, 3, "2026-09-25"), (dto.MemberCount, dto.SessionCount, dto.LastSession));
        Assert.Equal(["Auer", "Berger"], dto.Members.Select(m => m.LastName));                          // archivierte fehlen
        Assert.Equal(["present", "absent", "present"], dto.Members[0].Statuses);
        Assert.Equal([null, "present", "absent"], dto.Members[1].Statuses);
        Assert.Equal((2, 3), (dto.Members[0].Present, dto.Members[0].Recorded));
        Assert.Equal((1, 2), (dto.Members[1].Present, dto.Members[1].Recorded));

        // Das Fenster zeigt nur die jüngsten Einheiten, die Quote zählt über alle.
        var narrow = await Svc().GetGroupAsync(Manager(), g, take: 2);
        Assert.Equal(["2026-09-18", "2026-09-25"], narrow.Sessions.Select(s => s.Date));
        Assert.Equal((2, 3), (narrow.Members[0].Present, narrow.Members[0].Recorded));

        var sheet = await Svc().GetMemberAsync(Manager(), a.Id);
        Assert.Equal((2, 1), (sheet.Attendance.Present, sheet.Attendance.Absent));
        Assert.Equal([("2026-09-25", "present", "Anfänger"), ("2026-09-18", "absent", "Anfänger"), ("2026-09-11", "present", "Anfänger")],
            sheet.Attendance.Recent.Select(r => (r.Date, r.Status, r.Group)));

        var row = Assert.Single(await Svc().ListGroupsAsync(Manager()));
        Assert.Equal((2, 3, "2026-09-25"), (row.MemberCount, row.SessionCount, row.LastSession));
    }

    // ---- Lernstand aus dem Konto --------------------------------------------------------------

    [Fact]
    public async Task Progress_SumsWhatRookHubAndKidHubAlreadyCount()
    {
        var child = await UserAsync("daniel2015");
        _db.KidsLevelProgresses.AddRange(
            new KidsLevelProgress { UserId = child, Level = 1, Stars = 3 },
            new KidsLevelProgress { UserId = child, Level = 2, Stars = 2 },
            new KidsLevelProgress { UserId = child, Level = 3, Stars = 0 });                            // begonnen, nicht geschafft
        await _db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new ClubProgressService(new PuzzleStatsService(_db, cache), new TrainingGoalService(_db), new KidsProgressService(_db));

        var p = await svc.GetAsync(child, "daniel2015");

        Assert.Equal(("daniel2015", 2, 5, 0, 0, null), (p.Username, p.KidsLevelsDone, p.KidsStars, p.PuzzleAttempts, p.Minutes28, p.LastActive));
    }

    // ---- Controller: woher die Rechte kommen ---------------------------------------------------

    private ClubController Controller(int userId, bool admin = false, params string[] perms)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        claims.AddRange(perms.Select(p => new Claim(PermissionAuthorizationHandler.PermissionClaimType, p)));
        return new ClubController(Svc())
        {
            ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Jwt")) } },
        };
    }

    [Fact]
    public async Task Controller_AdminAndManagerSeeAll_TrainerHisGroups_AnyoneElseNothing()
    {
        var tina = await UserAsync("tina");
        var g = await GroupAsync("Anfänger", tina);
        await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber", g));
        await Svc().CreateMemberAsync(Manager(), Kid("Anna", "Moser"));

        static int Count(ActionResult<List<ClubMemberListDto>> r) => ((List<ClubMemberListDto>)((OkObjectResult)r.Result!).Value!).Count;
        Assert.Equal(2, Count(await Controller(99, admin: true).Members(null)));
        Assert.Equal(2, Count(await Controller(98, perms: Permissions.ClubManage).Members(null)));
        Assert.Equal(1, Count(await Controller(tina, perms: Permissions.ClubTrainer).Members(null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => Controller(tina).Members(null));
        await Assert.ThrowsAsync<ForbiddenException>(() => Controller(tina, perms: Permissions.LeagueView).Members(null));
    }

    [Fact]
    public async Task Controller_RedeemNeedsNoClubRight_TheAccountIsTheOneLinking()
    {
        var child = await UserAsync("daniel2015");
        var kid = await Svc().CreateMemberAsync(Manager(), Kid("Daniel", "Huber"));
        var code = await Svc().CreateLinkCodeAsync(Manager(), kid.Id);

        var result = await Controller(child).Redeem(new ClubLinkRedeemDto { Code = code.Code }, default);

        Assert.True(((ClubLinkStateDto)((OkObjectResult)result.Result!).Value!).Linked);
        // Das verknüpfte Konto selbst bekommt dadurch KEIN Club-Recht.
        await Assert.ThrowsAsync<ForbiddenException>(() => Controller(child).Member(kid.Id, default));
        // Ohne Verknüpfung gibt es keinen Lernstand aus einem Konto: 204, der Fortschritts-Dienst wird nicht gefragt.
        var unlinked = await Svc().CreateMemberAsync(Manager(), Kid("Anna", "Moser"));
        Assert.IsType<NoContentResult>((await Controller(99, admin: true).Progress(unlinked.Id, null!, default)).Result);
    }

    [Fact]
    public void BothClubRights_AreKnownPermissions_SoTheAdminRoleCarriesThem()
    {
        Assert.Contains(Permissions.ClubManage, Permissions.All);
        Assert.Contains(Permissions.ClubTrainer, Permissions.All);
    }
}
