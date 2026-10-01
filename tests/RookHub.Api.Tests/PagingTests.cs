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

    /// <summary>A9-011: <c>?page=2147483647</c> lief in <c>Skip((page - 1) * pageSize)</c> unchecked über
    /// (→ OFFSET -200 → MariaDB-Syntaxfehler → 500). Die Seite ist jetzt nach oben gedeckelt, der Offset
    /// passt für jede Obergrenze in ein int.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(1500)]
    public void Normalize_HugePage_KeepsTheSkipOffsetInRange(int max)
    {
        var (p, s) = Paging.Normalize(int.MaxValue, int.MaxValue, max);
        Assert.Equal(int.MaxValue / max, p);
        Assert.Equal(max, s);
        var offset = checked((p - 1) * s);                       // würde ohne Deckel OverflowException werfen
        Assert.InRange(offset, 0, int.MaxValue);
    }

    [Fact]
    public void Normalize_RespectsCustomMax()
    {
        var (_, s) = Paging.Normalize(1, 500, maxPageSize: 200);
        Assert.Equal(200, s);
    }
}
