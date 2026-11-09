using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Club;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Club;

namespace RookHub.Api.Tests.Club;

/// <summary>
/// R2-001: Ob ein Blatt ein Kind oder ein Trainer ist, legt nur die Leitung fest. Ein Trainer-Konto, das ein Kind seiner Gruppe
/// zum Trainer umstufte, nahm es damit aus ALLEN Gruppen (auch fremden) und zeigte es jedem Trainer-Konto; umgekehrt zog es
/// ein Trainer-Blatt in die eigene Gruppe.
/// </summary>
public class ClubTrainerRankTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTime _now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private ClubService Svc() => new(_db, () => _now);
    private static ClubActor Manager() => new(1, true, false);
    private static ClubActor Trainer(int userId) => new(userId, false, true);

    private async Task<int> UserAsync(string name)
    {
        var u = new AppUser { Username = name, PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> GroupAsync(string name, int trainer)
    {
        var g = await Svc().CreateGroupAsync(Manager(), new ClubGroupInputDto { Name = name });
        _db.ClubGroupTrainers.Add(new ClubGroupTrainer { GroupId = g.Id, UserId = trainer });
        await _db.SaveChangesAsync();
        return g.Id;
    }

    [Fact]
    public async Task Trainer_stuftKindNichtZumTrainerUm_fremdeGruppeBleibt_zweiterTrainerSiehtEsNicht()
    {
        var (tina, ute) = (await UserAsync("tina"), await UserAsync("ute"));
        var (mine, theirs) = (await GroupAsync("Anfänger", tina), await GroupAsync("Turnier", ute));
        var third = await GroupAsync("Mädchen", await UserAsync("vera"));
        var kid = await Svc().CreateMemberAsync(Manager(), new ClubMemberInputDto { FirstName = "Kim", GroupIds = [mine, theirs] });

        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().UpdateMemberAsync(Trainer(tina), kid.Id,
            new ClubMemberInputDto { FirstName = "Kim", IsTrainer = true, GroupIds = [mine] }));

        var sheet = await Svc().GetMemberAsync(Manager(), kid.Id);
        Assert.False(sheet.IsTrainer);
        Assert.Equal([mine, theirs], sheet.Groups.Select(g => g.Id).Order());
        // Ein drittes Trainer-Konto ohne gemeinsame Gruppe sieht das Kind weiter nicht.
        var vera = _db.ClubGroupTrainers.Single(t => t.GroupId == third).UserId;
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().GetMemberAsync(Trainer(vera), kid.Id));
    }

    [Fact]
    public async Task Trainer_machtAusTrainerBlattKeinKind_Leitung_darfBeides()
    {
        var tina = await UserAsync("tina");
        var mine = await GroupAsync("Anfänger", tina);
        var coach = await Svc().CreateMemberAsync(Manager(), new ClubMemberInputDto { FirstName = "Georg", IsTrainer = true });

        await Assert.ThrowsAsync<ForbiddenException>(() => Svc().UpdateMemberAsync(Trainer(tina), coach.Id,
            new ClubMemberInputDto { FirstName = "Georg", IsTrainer = false, GroupIds = [mine] }));
        Assert.True((await Svc().GetMemberAsync(Manager(), coach.Id)).IsTrainer);

        // Ein Trainer-Konto darf ein Trainer-Blatt sonst weiter pflegen (Name), ohne Gruppen zu setzen.
        var renamed = await Svc().UpdateMemberAsync(Trainer(tina), coach.Id,
            new ClubMemberInputDto { FirstName = "Georgi", IsTrainer = true, GroupIds = [mine] });
        Assert.Equal(("Georgi", true), (renamed.FirstName, renamed.IsTrainer));
        Assert.Empty(renamed.Groups);

        // Die Leitung stuft in beide Richtungen um.
        var kid = await Svc().UpdateMemberAsync(Manager(), coach.Id, new ClubMemberInputDto { FirstName = "Georg", GroupIds = [mine] });
        Assert.Equal((false, mine), (kid.IsTrainer, Assert.Single(kid.Groups).Id));
        var back = await Svc().UpdateMemberAsync(Manager(), coach.Id, new ClubMemberInputDto { FirstName = "Georg", IsTrainer = true });
        Assert.True(back.IsTrainer);
        Assert.Empty(back.Groups);
    }
}
