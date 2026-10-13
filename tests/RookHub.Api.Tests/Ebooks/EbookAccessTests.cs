using Microsoft.EntityFrameworkCore;
using RookHub.Api.Exceptions;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookAccessTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task Owner_Admin_GroupMember_And_Everyone_See_The_Book_Others_Dont()
    {
        var owner = await _kit.UserAsync("owner");
        var member = await _kit.UserAsync("member");
        var stranger = await _kit.UserAsync("stranger");
        var admin = await _kit.UserAsync("admin", admin: true);
        var group = await _kit.GroupAsync("Verein", false, member.Id);
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);

        var shared = await _kit.BookAsync("Geteilt", owner.Id, "pdf", null, group.Id);
        var open = await _kit.BookAsync("Für alle", owner.Id, "pdf", null, everyone.Id);
        var secret = await _kit.BookAsync("Privat", owner.Id);

        async Task<List<string>> Visible(int userId, bool manageAll) =>
            await EbookAccess.Visible(_kit.Db, userId, manageAll).OrderBy(b => b.Title).Select(b => b.Title).ToListAsync();

        Assert.Equal(["Für alle", "Geteilt", "Privat"], await Visible(owner.Id, false));
        Assert.Equal(["Für alle", "Geteilt"], await Visible(member.Id, false));
        Assert.Equal(["Für alle"], await Visible(stranger.Id, false));
        Assert.Equal(["Für alle", "Geteilt", "Privat"], await Visible(admin.Id, true));
        Assert.True(await EbookAccess.CanAccessAsync(_kit.Db, member.Id, shared.Id, false));
        Assert.False(await EbookAccess.CanAccessAsync(_kit.Db, stranger.Id, secret.Id, false));
        Assert.True(await EbookAccess.CanAccessAsync(_kit.Db, stranger.Id, open.Id, false));
    }

    [Fact]
    public async Task LoadManageable_Is404ForInvisible_403ForVisibleButForeign()
    {
        var owner = await _kit.UserAsync("owner");
        var member = await _kit.UserAsync("member");
        var group = await _kit.GroupAsync("G", false, member.Id);
        var shared = await _kit.BookAsync("Geteilt", owner.Id, "pdf", null, group.Id);
        var secret = await _kit.BookAsync("Privat", owner.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => EbookAccess.LoadManageableAsync(_kit.Db, member.Id, secret.Id, false));
        await Assert.ThrowsAsync<ForbiddenException>(() => EbookAccess.LoadManageableAsync(_kit.Db, member.Id, shared.Id, false));
        await Assert.ThrowsAsync<NotFoundException>(() => EbookAccess.LoadManageableAsync(_kit.Db, member.Id, 999999, true));
        var loaded = await EbookAccess.LoadManageableAsync(_kit.Db, owner.Id, shared.Id, false);
        Assert.Equal("Geteilt", loaded.Title);
        var asManager = await EbookAccess.LoadManageableAsync(_kit.Db, member.Id, secret.Id, true);
        Assert.Equal("Privat", asManager.Title);
    }

    [Fact]
    public async Task OwnerlessBook_IsManageableOnlyByManagers()
    {
        var user = await _kit.UserAsync("u");
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var orphan = await _kit.BookAsync("Verwaist", null, "pdf", null, everyone.Id);
        Assert.False(EbookAccess.CanManage(orphan, user.Id, false));
        Assert.True(EbookAccess.CanManage(orphan, user.Id, true));
    }
}
