using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>E-Books ohne Anmeldung (0.778.0): „Alle“-Bücher sehen, selbst hochladen, Lesestand, Übernahme ins Konto.</summary>
public class EbookAnonymousTests : IDisposable
{
    private const string Anon = "0123456789abcdef0123456789abcdef";
    private const string OtherAnon = "fedcba9876543210fedcba9876543210";
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private static EbookCaller A(string id = Anon) => EbookCaller.Anonymous(id);

    [Fact]
    public void Caller_InvalidVisitorIdCountsAsNone()
    {
        Assert.Null(EbookCaller.Anonymous("not-a-visitor").AnonId);
        Assert.Null(EbookCaller.Anonymous(null).AnonId);
        Assert.Equal(Anon, A().AnonId);
        EbookCaller user = 5;
        Assert.False(user.IsAnonymous);
    }

    [Fact]
    public async Task Anonymous_SeesEveryoneBooksAndOwnUploads_NothingElse()
    {
        var owner = await _kit.UserAsync("owner");
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var team = await _kit.GroupAsync("Team", false, owner.Id);
        var pub = await _kit.BookAsync("Für alle", owner.Id, groupIds: everyone.Id);
        await _kit.BookAsync("Nur Team", owner.Id, groupIds: team.Id);
        await _kit.BookAsync("Privat", owner.Id);
        var mine = await _kit.BookAsync("Mein anonymer Upload", null);
        mine.AnonymousSessionId = Anon;
        var foreign = await _kit.BookAsync("Fremder anonymer Upload", null);
        foreign.AnonymousSessionId = OtherAnon;
        await _kit.Db.SaveChangesAsync();

        var list = await _kit.Service.ListAsync(A(), manageAll: true, new EbookQuery());   // manageAll gilt anonym nie
        Assert.Equal(["Für alle", "Mein anonymer Upload"], list.Items.Select(i => i.Title).OrderBy(t => t).ToArray());
        Assert.True(list.Items.Single(i => i.Id == mine.Id).IsOwner);
        Assert.True(list.Items.Single(i => i.Id == mine.Id).CanManage);
        Assert.False(list.Items.Single(i => i.Id == pub.Id).CanManage);
        Assert.False(list.CanManageAll);

        var mineOnly = await _kit.Service.ListAsync(A(), false, new EbookQuery { Mine = true });
        Assert.Equal([mine.Id], mineOnly.Items.Select(i => i.Id).ToArray());
        // Ohne Kennung: nur „Alle“.
        var none = await _kit.Service.ListAsync(EbookCaller.Anonymous(null), false, new EbookQuery());
        Assert.Equal([pub.Id], none.Items.Select(i => i.Id).ToArray());
        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.GetAsync(A(), false, foreign.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _kit.Service.DeleteAsync(A(), false, pub.Id));
    }

    [Fact]
    public async Task Anonymous_UploadsWithOwnQuota_NeverShared_AndCanDeleteOwnBook()
    {
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var dto = await _kit.Service.UploadAsync(A(), false, new MemoryStream(EbookTestKit.TinyPdf()), "Mein Buch.pdf", EbookTestKit.TinyPdf().Length,
            new EbookMetaDto { GroupIds = [everyone.Id] });
        var book = await _kit.Db.Ebooks.Include(b => b.GroupAccess).SingleAsync();
        Assert.Null(book.OwnerUserId);
        Assert.Equal(Anon, book.AnonymousSessionId);
        Assert.Empty(book.GroupAccess);   // anonym nie freigegeben
        Assert.True(dto.IsOwner);
        Assert.Equal(EbookTestKit.TinyPdf().Length, await _kit.Service.UsedBytesAsync(A()));
        Assert.Equal(0, await _kit.Service.UsedBytesAsync(A(OtherAnon)));

        await _kit.Service.DeleteAsync(A(), false, book.Id);
        Assert.Empty(_kit.Db.Ebooks);
    }

    [Fact]
    public async Task Anonymous_UploadRules_NeedVisitorId_RespectQuotas()
    {
        var pdf = EbookTestKit.TinyPdf();
        await Assert.ThrowsAsync<ForbiddenException>(() => _kit.Service.UploadAsync(EbookCaller.Anonymous(null), false,
            new MemoryStream(pdf), "a.pdf", pdf.Length, new EbookMetaDto()));
        _kit.Options.AnonymousQuotaBytes = pdf.Length - 1;
        await Assert.ThrowsAsync<DomainValidationException>(() => _kit.Service.UploadAsync(A(), false,
            new MemoryStream(pdf), "a.pdf", pdf.Length, new EbookMetaDto()));
        _kit.Options.AnonymousQuotaBytes = 10_000_000;
        _kit.Options.AnonymousTotalBytes = pdf.Length - 1;   // Gesamtdeckel aller anonymen Uploads
        await Assert.ThrowsAsync<DomainValidationException>(() => _kit.Service.UploadAsync(A(), false,
            new MemoryStream(pdf), "a.pdf", pdf.Length, new EbookMetaDto()));
        _kit.Options.AnonymousUploads = false;
        await Assert.ThrowsAsync<ForbiddenException>(() => _kit.Service.UploadAsync(A(), false,
            new MemoryStream(pdf), "a.pdf", pdf.Length, new EbookMetaDto()));
        var list = await _kit.Service.ListAsync(A(), false, new EbookQuery());
        Assert.False(list.UploadEnabled);
    }

    [Fact]
    public async Task Anonymous_ProgressIsKeptPerVisitor_AndClaimMovesBooksAndProgress()
    {
        var owner = await _kit.UserAsync("owner");
        var me = await _kit.UserAsync("me");
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var pub = await _kit.BookAsync("Für alle", owner.Id, groupIds: everyone.Id);
        var older = await _kit.BookAsync("Schon gelesen", owner.Id, groupIds: everyone.Id);
        var mine = await _kit.BookAsync("Anonym hochgeladen", null);
        mine.AnonymousSessionId = Anon;
        await _kit.Db.SaveChangesAsync();

        await _kit.Service.SetProgressAsync(A(), false, pub.Id, 12, 100);
        Assert.Equal(12, (await _kit.Service.GetProgressAsync(A(), false, pub.Id))!.Page);
        Assert.Null(await _kit.Service.GetProgressAsync(A(OtherAnon), false, pub.Id));
        Assert.Equal(12, (await _kit.Service.ListAsync(A(), false, new EbookQuery())).Items.Single(i => i.Id == pub.Id).Progress!.Page);

        // Im Konto ist „Schon gelesen“ neuer als der anonyme Stand → bleibt.
        await _kit.Service.SetProgressAsync(A(), false, older.Id, 3, 50);
        await _kit.Service.SetProgressAsync(me.Id, false, older.Id, 40, 50);

        var claimed = await _kit.Service.ClaimAnonymousAsync(me.Id, Anon);
        Assert.Equal(1, claimed);
        var book = await _kit.Db.Ebooks.SingleAsync(b => b.Id == mine.Id);
        Assert.Equal(me.Id, book.OwnerUserId);
        Assert.Null(book.AnonymousSessionId);
        Assert.Equal(12, (await _kit.Service.GetProgressAsync(me.Id, false, pub.Id))!.Page);
        Assert.Equal(40, (await _kit.Service.GetProgressAsync(me.Id, false, older.Id))!.Page);
        Assert.Empty(_kit.Db.EbookAnonymousProgresses);
        // Danach sieht der anonyme Besucher das Buch nicht mehr als seines.
        Assert.DoesNotContain(mine.Id, (await _kit.Service.ListAsync(A(), false, new EbookQuery())).Items.Select(i => i.Id));
        await Assert.ThrowsAsync<DomainValidationException>(() => _kit.Service.ClaimAnonymousAsync(me.Id, "kaputt"));
    }

    [Fact]
    public async Task Cleanup_RemovesUnclaimedOldAnonymousBooks_KeepsFreshOnes()
    {
        var old = await _kit.BookAsync("Alt", null);
        old.AnonymousSessionId = Anon;
        old.CreatedAt = DateTime.UtcNow.AddDays(-40);
        var fresh = await _kit.BookAsync("Neu", null);
        fresh.AnonymousSessionId = Anon;
        await _kit.Db.SaveChangesAsync();
        var removed = await _kit.Service.CleanupAnonymousAsync(TimeSpan.FromDays(30));
        Assert.Equal(1, removed);
        Assert.Equal(["Neu"], _kit.Db.Ebooks.Select(b => b.Title).ToArray());
    }
}
