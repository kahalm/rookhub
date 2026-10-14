using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Social;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookProcessingTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private IServiceProvider Services(bool engineCheck = true)
    {
        var s = new ServiceCollection();
        s.AddSingleton<AppDbContext>(_kit.Db);
        s.AddSingleton(TestServices.Notifications(_kit.Db));
        s.AddSingleton<IOptions<EbookOptions>>(Options.Create(new EbookOptions { EngineCheck = engineCheck }));
        return s.BuildServiceProvider();
    }

    private static Ebook Ready(int owner) => new()
    {
        Title = "Buch", OwnerUserId = owner, Source = EbookSource.Upload,
        CoverState = EbookCoverState.Done, DiagramState = EbookDiagramState.Done, DiagramCount = 12,
        SolutionCheckState = EbookDiagramState.Done, ConversionState = EbookConversionState.NotNeeded,
    };

    [Fact]
    public void IsProcessing_EachPendingStepCounts_CheckOnlyWithDiagramsAndWhenEnabled()
    {
        var b = Ready(1);
        Assert.False(EbookProcessing.IsProcessing(b, true));
        b.CoverState = EbookCoverState.Pending; Assert.True(EbookProcessing.IsProcessing(b, true));
        b = Ready(1); b.ConversionState = EbookConversionState.Pending; Assert.True(EbookProcessing.IsProcessing(b, true));
        b = Ready(1); b.DiagramState = EbookDiagramState.Pending; Assert.True(EbookProcessing.IsProcessing(b, true));
        b = Ready(1); b.SolutionCheckState = EbookDiagramState.Pending;
        Assert.True(EbookProcessing.IsProcessing(b, true));
        Assert.False(EbookProcessing.IsProcessing(b, false));
        b.DiagramCount = 0; Assert.False(EbookProcessing.IsProcessing(b, true));
    }

    [Fact]
    public async Task NotifiesTheUploaderOnce_WithLinkAndDiagramCount()
    {
        var u = await _kit.UserAsync("u");
        var b = Ready(u.Id);
        _kit.Db.Ebooks.Add(b);
        await _kit.Db.SaveChangesAsync();
        var sp = Services();

        await EbookProcessing.NotifyIfReadyAsync(sp, b, default);
        await EbookProcessing.NotifyIfReadyAsync(sp, b, default);

        var n = await _kit.Db.Notifications.SingleAsync();
        Assert.Equal(u.Id, n.UserId);
        Assert.Equal(EbookProcessing.NotificationType, n.Type);
        Assert.Equal($"/ebooks/{b.Id}", n.Link);
        Assert.Contains("12", n.DataJson);
        Assert.NotNull(b.ReadyNotifiedAt);
    }

    [Fact]
    public async Task NoNotification_WhileProcessing_OrForLibraryBooks()
    {
        var u = await _kit.UserAsync("u");
        var pending = Ready(u.Id); pending.DiagramState = EbookDiagramState.Pending;
        var library = Ready(u.Id); library.Source = EbookSource.Library;
        _kit.Db.Ebooks.AddRange(pending, library);
        await _kit.Db.SaveChangesAsync();
        var sp = Services();
        await EbookProcessing.NotifyIfReadyAsync(sp, pending, default);
        await EbookProcessing.NotifyIfReadyAsync(sp, library, default);
        Assert.Empty(_kit.Db.Notifications);
    }
}
