using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Geteilte Seiten-Normalisierung (ersetzt fünf gedriftete Klemm-Kopien in den Services).</summary>
public class PagingTests
{
    [Theory]
    [InlineData(1, 20, 1, 20)]     // gültig → unverändert
    [InlineData(0, 20, 1, 20)]     // page < 1 → 1
    [InlineData(-5, 0, 1, 1)]      // beides zu klein
    [InlineData(3, 1000, 3, 100)]  // pageSize über der Obergrenze → 100
    public void Normalize_ClampsPageAndPageSize(int page, int size, int expPage, int expSize)
    {
        var (p, s) = Paging.Normalize(page, size);
        Assert.Equal(expPage, p);
        Assert.Equal(expSize, s);
    }

    [Fact]
    public void Normalize_RespectsCustomMax()
    {
        var (_, s) = Paging.Normalize(1, 500, maxPageSize: 200);
        Assert.Equal(200, s);
    }
}
