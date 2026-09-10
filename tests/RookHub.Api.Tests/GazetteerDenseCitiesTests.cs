using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die feine Ortsliste (<c>cities1000</c>) gilt NUR fuer die Laender, denen GeoNames keinen
/// Postleitzahl-Datensatz anbietet. Zwei Dinge duerfen dabei nicht kippen: sie darf die groben
/// Zeilen dieser Laender ERSETZEN (nicht ergaenzen, sonst steht jeder groessere Ort doppelt im
/// Lexikon und die Mehrdeutigkeitsregel sieht zwei Kandidaten fuer einen Ort), und sie darf die
/// uebrigen Laender NICHT anfassen — weltweit angewandt braechte sie vor allem gleichnamige
/// Kleinorte und damit mehr Mehrdeutigkeit statt weniger.
/// </summary>
public class GazetteerDenseCitiesTests
{
    private static GeoPlace City(string country, string name, int population = 0) => new()
    {
        Country = country,
        Name = name,
        NameNormalized = GeoTextNormalizer.Normalize(name),
        NameTranscribed = GeoTextNormalizer.NormalizeTranscribed(name, country) ?? string.Empty,
        Kind = GeoPlaceKind.City,
        Lat = 1,
        Lon = 1,
        Population = population,
    };

    [Fact]
    public void MergeDenseCities_ReplacesTheCoarseRowsOfDenseCountries()
    {
        var coarse = new List<GeoPlace> { City("VN", "Hanoi", 1_400_000), City("DE", "Muenster") };
        var dense = new List<GeoPlace> { City("VN", "Hanoi", 1_400_000), City("VN", "Xuyen Moc") };

        var merged = GazetteerImportService.MergeDenseCities(coarse, dense);

        // Hanoi genau EINMAL: die feine Liste ist eine Obermenge, doppelt waere ein zweiter Kandidat.
        Assert.Single(merged, p => p.Country == "VN" && p.Name == "Hanoi");
        Assert.Contains(merged, p => p.Country == "VN" && p.Name == "Xuyen Moc");
    }

    [Fact]
    public void MergeDenseCities_LeavesEveryOtherCountryUntouched()
    {
        var coarse = new List<GeoPlace> { City("DE", "Muenster"), City("DE", "Kiel"), City("VN", "Hanoi") };
        var dense = new List<GeoPlace> { City("VN", "Hanoi") };

        var merged = GazetteerImportService.MergeDenseCities(coarse, dense);

        Assert.Equal(2, merged.Count(p => p.Country == "DE"));
        Assert.Contains(merged, p => p.Name == "Muenster");
        Assert.Contains(merged, p => p.Name == "Kiel");
    }

    [Fact]
    public void MergeDenseCities_WithoutDenseRows_ChangesNothing()
    {
        // Der Fall „cities1000 nicht abrufbar": die grobe Liste muss unveraendert durchgehen.
        var coarse = new List<GeoPlace> { City("DE", "Kiel"), City("VN", "Hanoi") };

        var merged = GazetteerImportService.MergeDenseCities(coarse, []);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, p => p.Country == "VN");
    }

    [Fact]
    public void DenseCityCountries_AreCountriesTheGeocoderCanActuallyFilterOn()
    {
        // Ohne Zuordnung Foederation -> ISO-2 laeuft die Ortssuche ohne Landfilter, und dann
        // brachte eine feinere Liste fuer dieses Land nichts als zusaetzliche Kandidaten.
        var known = FideCountryCodes.KnownIso2.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = GazetteerImportService.DenseCityCountries.Where(c => !known.Contains(c)).ToList();

        Assert.Empty(orphans);
    }
}

/// <summary>
/// Der Rueckweg ISO-2 → Foederation faellt ohne ausdrueckliche Entscheidung auf die
/// ALPHABETISCHE Reihenfolge zurueck, und die ist willkuerlich. Als Katalonien als eigene
/// Foederation nach Spanien zugeordnet wurde, waehlte sie „CAT" vor „ESP" — jede spanische
/// Laenderflagge waere danach Katalonien gewesen.
/// </summary>
public class FideCountryReverseTests
{
    [Fact]
    public void EveryAmbiguousIso2HasAPreference()
    {
        var ambiguous = FideCountryCodes.All
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Where(iso2 => !FideCountryCodes.Preferred.ContainsKey(iso2))
            .ToList();

        Assert.Empty(ambiguous);
    }

    [Theory]
    [InlineData("ES", "ESP")]
    [InlineData("GB", "ENG")]
    [InlineData("MK", "MKD")]
    public void FromIso2_PicksTheDecidedFederation(string iso2, string expected)
        => Assert.Equal(expected, FideCountryCodes.FromIso2(iso2));
}

/// <summary>
/// GeoNames fuehrt manche Staedte unter dem englischen Exonym („Prague", „Athens"), waehrend die
/// Turniertexte den einheimischen Namen schreiben. Das Lexikon bekommt dafuer eine zweite Zeile am
/// SELBEN Punkt — kein Ersatz. Zwei Eigenschaften duerfen nicht kippen: die Zeile muss die
/// Koordinaten des kanonischen Orts uebernehmen (sonst waere sie ein Pin ins Nichts), und ein
/// Alias ohne Ziel darf gar keine Zeile erzeugen.
/// </summary>
public class PlaceAliasTests
{
    private static GeoPlace City(string country, string name, double lat, double lon, int pop = 0) => new()
    {
        Country = country,
        Name = name,
        NameNormalized = GeoTextNormalizer.Normalize(name),
        NameTranscribed = GeoTextNormalizer.NormalizeTranscribed(name, country) ?? string.Empty,
        Kind = GeoPlaceKind.City,
        Lat = lat,
        Lon = lon,
        Population = pop,
    };

    [Fact]
    public void AddAliasRows_PutsTheLocalNameOnTheSamePoint()
    {
        var cities = new List<GeoPlace> { City("CZ", "Prague", 50.088, 14.421, 1_300_000) };

        var result = GazetteerImportService.AddAliasRows(cities);

        var praha = Assert.Single(result, p => p.NameNormalized == "praha");
        Assert.Equal(50.088, praha.Lat);
        Assert.Equal(14.421, praha.Lon);
        // Der kanonische Eintrag bleibt: es ist ein zweiter Name, kein Ersatz.
        Assert.Contains(result, p => p.NameNormalized == "prague");
        // Abstand null — die Zusatzzeile darf keine Mehrdeutigkeit erzeugen.
        Assert.Equal(praha.Lat, result.First(p => p.NameNormalized == "prague").Lat);
    }

    [Fact]
    public void AddAliasRows_WithoutTheCanonicalPlace_AddsNothing()
    {
        // GeoNames koennte den Ort umbenennen oder unter die Einwohnergrenze fallen lassen.
        // Ein Alias ohne Ziel haette keine Koordinaten und waere schlimmer als keine Zeile.
        var cities = new List<GeoPlace> { City("CZ", "Brno", 49.195, 16.608) };

        var result = GazetteerImportService.AddAliasRows(cities);

        Assert.Single(result);
        Assert.DoesNotContain(result, p => p.NameNormalized == "praha");
    }

    [Fact]
    public void AddAliasRows_DoesNotCrossCountries()
    {
        // „Prague" gibt es auch in den USA (Oklahoma). Der Alias gehoert nach Tschechien.
        var cities = new List<GeoPlace> { City("US", "Prague", 35.487, -96.684) };

        var result = GazetteerImportService.AddAliasRows(cities);

        Assert.DoesNotContain(result, p => p.NameNormalized == "praha");
    }

    [Fact]
    public void PlaceAliases_TargetCountriesTheGeocoderCanFilterOn()
    {
        var known = FideCountryCodes.KnownIso2.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = GazetteerImportService.PlaceAliases
            .Select(a => a.Country).Distinct()
            .Where(c => !known.Contains(c)).ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void PlaceAliases_AliasAndCanonicalNeverNormalizeToTheSameString()
    {
        // Sonst waere die Zusatzzeile eine exakte Dublette — zwei Kandidaten fuer einen Ort.
        var pointless = GazetteerImportService.PlaceAliases
            .Where(a => GeoTextNormalizer.Normalize(a.Alias) == GeoTextNormalizer.Normalize(a.Canonical))
            .Select(a => $"{a.Country}:{a.Alias}").ToList();

        Assert.Empty(pointless);
    }
}
