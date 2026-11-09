using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.Engines;
using Xunit;

namespace RookHub.Api.Tests.Engines;

/// <summary>Die kuratierte Merksatz-Liste (0.917.0): lädt, ist eindeutig, belegt; Modell-Antworten werden auf den Wortlaut gebracht.</summary>
public class ChessMaximsTests
{
    [Fact]
    public void Liste_laedt_eindeutig_und_jeder_Satz_ist_belegt()
    {
        var all = ChessMaxims.All;
        Assert.InRange(all.Count, 30, 80);
        Assert.Equal(all.Count, all.Select(m => m.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(m => m.Text).Distinct().Count());
        Assert.All(all, m =>
        {
            Assert.Matches("^[a-z0-9-]+$", m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.Text));
            Assert.False(string.IsNullOrWhiteSpace(m.When));
            Assert.NotEmpty(m.Sources);
            Assert.All(m.Sources, s => Assert.True(s.EbookId > 0));
        });
        // Die beiden Beispiele des Users (09.11.) sind drin.
        Assert.NotNull(ChessMaxims.ById("raum-und-tausch"));
        Assert.NotNull(ChessMaxims.ById("laeufer-bauern-andere-farbe"));
    }

    [Fact]
    public void Canonicalize_Kennung_wird_Wortlaut_Erfundenes_faellt_weg()
    {
        var m = ChessMaxims.ById("raum-und-tausch")!;
        Assert.Equal("Absatz eins.\n\nAbsatz zwei.\n\nMerksatz: " + m.Text,
            ChessMaxims.Canonicalize("Absatz eins.\n\nAbsatz zwei.\n\nMerksatz: raum-und-tausch"));
        Assert.Equal("Absatz eins.\n\nMerksatz: " + m.Text,
            ChessMaxims.Canonicalize("Absatz eins.\n\n**Merksatz:** „" + m.Text + "“"));
        Assert.Equal("Absatz eins.", ChessMaxims.Canonicalize("Absatz eins.\n\nMerksatz: Erst denken, dann ziehen."));
        Assert.Equal("Absatz eins.", ChessMaxims.Canonicalize("Absatz eins."));
    }

    [Fact]
    public void PDF_Fundstellen_tragen_eine_Seite()
    {
        var pdf = ChessMaxims.All.SelectMany(m => m.Sources).Where(s => s.Location?.StartsWith("PDF-S.") == true).ToList();
        Assert.NotEmpty(pdf);
        Assert.All(pdf, s => Assert.True(s.Page > 0));
    }
}

/// <summary>GET /api/maxims (0.917.0): je Buchstelle, ob der Aufrufer das Buch öffnen darf.</summary>
public class ChessMaximsControllerTests : IDisposable
{
    private readonly RookHub.Api.Data.AppDbContext _db = new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Anonym_sieht_nur_fuer_alle_freigegebene_Buecher_als_oeffenbar()
    {
        var src = ChessMaxims.ById("raum-und-tausch")!.Sources;
        var open = src[0].EbookId;
        var closed = src.Select(s => s.EbookId).First(id => id != open);
        var everyone = new RookHub.Api.Models.Group { Name = "Everyone", IsEveryone = true, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(everyone);
        _db.Ebooks.Add(new RookHub.Api.Models.Ebook { Id = open, Title = "offen", Source = RookHub.Api.Models.EbookSource.Library });
        _db.Ebooks.Add(new RookHub.Api.Models.Ebook { Id = closed, Title = "zu", Source = RookHub.Api.Models.EbookSource.Library });
        await _db.SaveChangesAsync();
        _db.EbookGroupAccesses.Add(new RookHub.Api.Models.EbookGroupAccess { EbookId = open, GroupId = everyone.Id });
        await _db.SaveChangesAsync();

        var ctl = new RookHub.Api.Controllers.Engines.ChessMaximsController(_db)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };
        var res = await ctl.List(CancellationToken.None);
        var list = Assert.IsType<List<RookHub.Api.DTOs.Engines.ChessMaximDto>>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(res.Result).Value);
        var m = list.Single(x => x.Id == "raum-und-tausch");
        Assert.All(m.Sources.Where(s => s.EbookId == open), s => Assert.True(s.Accessible));
        Assert.All(m.Sources.Where(s => s.EbookId == closed), s => Assert.False(s.Accessible));
        Assert.Equal(ChessMaxims.All.Count, list.Count);
    }
}
