using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Exceptions;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Social;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>R3-002: anonyme Uploads je Anschluss gedeckelt (unabhängig von der Besucher-Kennung) und Glocke gebündelt.</summary>
public class EbookAnonymousUploadGuardTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    private readonly EbookAnonymousUploadGuard _guard = new();
    public void Dispose() => _kit.Dispose();

    private EbookService Service(long anonQuota)
    {
        _kit.Options.AnonymousQuotaBytes = anonQuota;
        return new EbookService(_kit.Db, _kit.Storage, NullLogger<EbookService>.Instance,
            Microsoft.Extensions.Options.Options.Create(_kit.Options), new NotificationService(_kit.Db), _guard);
    }

    private static string Visitor(int i) => i.ToString("x32");

    private static Task<RookHub.Api.DTOs.Ebooks.EbookDto> Upload(EbookService svc, string visitor, int salt, string ip)
    {
        byte[] bytes = [.. EbookTestKit.TinyPdf(), (byte)salt, (byte)(salt >> 8)];   // je Upload andere Bytes (keine Dublette)
        return svc.UploadAsync(EbookCaller.Anonymous(visitor), false, new MemoryStream(bytes), $"b{salt}.pdf", bytes.Length, new(),
            CancellationToken.None, clientIpBucket: ip);
    }

    [Fact]
    public async Task SameIp_NewVisitorIds_DoNotMultiplyTheQuota_OtherIpStillWorks()
    {
        var size = EbookTestKit.TinyPdf().Length + 2;
        var svc = Service(anonQuota: size * 2);   // Kontingent: zwei Bücher
        await Upload(svc, Visitor(1), 1, "203.0.113.7");
        await Upload(svc, Visitor(2), 2, "203.0.113.7");
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => Upload(svc, Visitor(3), 3, "203.0.113.7"));
        Assert.Equal(ApiErrorCodes.EbookQuotaExceeded, ex.Code);
        await Upload(svc, Visitor(4), 4, "198.51.100.1");   // anderer Anschluss
        Assert.Equal(3, await _kit.Db.Ebooks.CountAsync());
    }

    [Fact]
    public async Task FailedUpload_GivesItsReservationBack()
    {
        var size = EbookTestKit.TinyPdf().Length + 2;
        var svc = Service(anonQuota: size * 2);
        await Upload(svc, Visitor(1), 1, "203.0.113.7");
        // Dublette (gleiche Bytes) → 409; die Reservierung darf das Kontingent nicht verbrauchen.
        await Assert.ThrowsAsync<ConflictException>(() => Upload(svc, Visitor(2), 1, "203.0.113.7"));
        await Upload(svc, Visitor(3), 2, "203.0.113.7");
        Assert.Equal(2, await _kit.Db.Ebooks.CountAsync());
    }

    [Fact]
    public async Task ManyAnonymousUploads_RingTheAdminBellOnce_SignedInUploadsEveryTime()
    {
        await _kit.UserAsync("admin", admin: true);
        var user = await _kit.UserAsync("leser");
        var svc = Service(anonQuota: 300L * 1024 * 1024);
        for (var i = 1; i <= 10; i++) await Upload(svc, Visitor(i), i, "203.0.113." + i);
        Assert.Equal(1, await _kit.Db.Notifications.CountAsync());

        byte[] own = [.. EbookTestKit.TinyPdf(), 99, 99];
        await svc.UploadAsync(user.Id, false, new MemoryStream(own), "eigen.pdf", own.Length, new(), CancellationToken.None);
        byte[] own2 = [.. EbookTestKit.TinyPdf(), 98, 98];
        await svc.UploadAsync(user.Id, false, new MemoryStream(own2), "eigen2.pdf", own2.Length, new(), CancellationToken.None);
        Assert.Equal(3, await _kit.Db.Notifications.CountAsync());
    }

    [Fact]
    public void Guard_CountsUploadsPerIp_AndForgetsAfterTheWindow()
    {
        var g = new EbookAnonymousUploadGuard();
        var t0 = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < EbookAnonymousUploadGuard.MaxUploadsPerIp; i++) Assert.True(g.TryReserve("ip", 1, 0, t0));
        Assert.False(g.TryReserve("ip", 1, 0, t0));
        Assert.True(g.TryReserve("other", 1, 0, t0));
        Assert.True(g.TryReserve("ip", 1, 0, t0 + EbookAnonymousUploadGuard.Window));

        Assert.True(g.TryTakeAnonymousBell(t0));
        Assert.False(g.TryTakeAnonymousBell(t0.AddMinutes(59)));
        Assert.True(g.TryTakeAnonymousBell(t0.AddMinutes(61)));
    }
}
