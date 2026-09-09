using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Aufloesung der Freitext-Spielorte gegen den lokalen Gazetteer. Die Testdaten sind echte
/// Ortstexte aus der chess-results-Trefferliste - genau daran haengt, ob ein Pin auf der Karte
/// steht oder nicht.
/// </summary>
public class GeocodingServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GeocodingService _service;

    public GeocodingServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new GeocodingService(_db);
    }

    public void Dispose() => _db.Dispose();

    private void Seed(params GeoPlace[] places)
    {
        // Beide Schreibweisen, genau wie der Gazetteer-Import sie schreibt.
        foreach (var p in places)
        {
            p.NameNormalized = GeoTextNormalizer.Normalize(p.Name);
            p.NameTranscribed = GeoTextNormalizer.NormalizeTranscribed(p.Name, p.Country);
        }
        _db.GeoPlaces.AddRange(places);
        _db.SaveChanges();
    }

    private static GeoPlace Postal(string country, string code, string name, double lat, double lon) =>
        new() { Country = country, PostalCode = code, Name = name, Lat = lat, Lon = lon, Kind = GeoPlaceKind.PostalCode };

    private static GeoPlace City(string country, string name, double lat, double lon, int population) =>
        new() { Country = country, Name = name, Lat = lat, Lon = lon, Kind = GeoPlaceKind.City, Population = population };

    private static GeoPlace Region(string country, string name, double lat, double lon) =>
        new() { Country = country, Name = name, Lat = lat, Lon = lon, Kind = GeoPlaceKind.Region };

    // ----- Zweite Schreibweise (Umschrift) ----------------------------------

    /// <summary>
    /// „Muenchen" im Ortstext gegen „München" im Lexikon. Die erste Suchform faltet den Umlaut zu
    /// <c>munchen</c>, die ausgeschriebene Form ergibt <c>muenchen</c> — die beiden trafen sich
    /// nie. Am 2026-09-09 auf Dev: 53 unverortete deutsche Eintraege scheiterten daran.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AusgeschriebenerUmlautImText_findetDenOrt()
    {
        Seed(City("DE", "München", 48.1372, 11.5755, 1_450_000));

        var result = await _service.ResolveAsync("Muenchen", null, "GER");

        Assert.NotNull(result);
        Assert.Equal("München", result!.PlaceName);
    }

    /// <summary>Und die Gegenrichtung bleibt selbstverstaendlich erhalten.</summary>
    [Fact]
    public async Task ResolveAsync_UmlautImText_findetDenOrtWeiterhin()
    {
        Seed(City("DE", "München", 48.1372, 11.5755, 1_450_000));

        var result = await _service.ResolveAsync("München", null, "GER");

        Assert.Equal("München", result!.PlaceName);
    }

    /// <summary>
    /// Die Gegenprobe zur Umschrift: beim SUCHEN <c>ue</c> zu <c>u</c> zu falten waere falsch, es
    /// machte aus „Quedlinburg" ein <c>qudlinburg</c>. Die Umschrift entsteht deshalb beim IMPORT
    /// und nur dort.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_UeMittenImWort_bleibtUnangetastet()
    {
        Seed(City("DE", "Quedlinburg", 51.7889, 11.1372, 24_000));

        Assert.Equal("Quedlinburg", (await _service.ResolveAsync("Quedlinburg", null, "GER"))!.PlaceName);
        Assert.Null(await _service.ResolveAsync("Qudlinburg", null, "GER"));
    }

    /// <summary>
    /// Kyrillischer Ortstext gegen kyrillisches Lexikon. Beide Seiten ergaben in der ersten
    /// Suchform eine LEERE Zeichenkette, weil der Aufraeumteil alles ausser <c>[a-z0-9]</c>
    /// verwirft. Auf Dev trugen 29 547 von 29 571 ukrainischen Postleitzahl-Zeilen deshalb einen
    /// leeren normalisierten Namen.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KyrillischerOrtstext_findetDieStadt()
    {
        Seed(City("UA", "Київ", 50.4547, 30.5238, 2_800_000));

        var result = await _service.ResolveAsync("Київ", null, "UKR");

        Assert.NotNull(result);
        Assert.Equal("Київ", result!.PlaceName);
    }

    /// <summary>Und lateinisch geschriebener Text gegen dasselbe kyrillische Lexikon.</summary>
    [Fact]
    public async Task ResolveAsync_LateinischerText_findetDieKyrillischeStadt()
    {
        Seed(City("UA", "Київ", 50.4547, 30.5238, 2_800_000));

        var result = await _service.ResolveAsync("Kyiv", null, "UKR");

        Assert.Equal("Київ", result!.PlaceName);
    }

    /// <summary>
    /// Der haeufigste ukrainische Fall: der Ortstext kommt kyrillisch von chess-results, der
    /// GeoNames-Ortsname steht LATEINISCH im Lexikon (398 von 400 ukrainischen Ortszeilen). Die
    /// Umschrift trifft beide, weil GeoNames fuer ukrainische Namen dieselbe Konvention benutzt.
    /// Betroffen waren 125 von 179 Eintraegen.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KyrillischerText_findetDenLateinischenLexikonnamen()
    {
        Seed(City("UA", "Zaporizhzhia", 47.8388, 35.1396, 710_000));

        var result = await _service.ResolveAsync("Запоріжжя", null, "UKR");

        Assert.NotNull(result);
        Assert.Equal("Zaporizhzhia", result!.PlaceName);
    }

    /// <summary>
    /// Der Fall, der die 29 596 eingespielten ukrainischen Postleitzahlen wirkungslos machte: der
    /// Postleitzahl-Weg verlangt eine Bestaetigung durch den Ortsnamen, und die konnte bei einem
    /// leeren Namen NIE gelingen.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KyrillischeAdresseMitPostleitzahl_wirdBestaetigt()
    {
        Seed(Postal("UA", "01001", "Київ", 50.4547, 30.5238),
             Postal("UA", "331", "Десь", 47.0, 35.0));

        var result = await _service.ResolveAsync("вул. Хрещатик 331, 01001 Київ", null, "UKR");

        Assert.NotNull(result);
        Assert.Equal(GeoSource.PostalCode, result!.Source);
        Assert.Equal("Київ", result.PlaceName);
    }

    /// <summary>
    /// Eine Schrift, die die Umschrift NICHT abdeckt (hier Georgisch): dann gibt es nichts zu
    /// vergleichen, und die Bestaetigung kann nicht verdient werden. Statt das stillschweigend als
    /// Nein zu behandeln, entscheidet die Laenge der Ziffernfolge — eine Hausnummer hat selten vier
    /// Stellen.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_OhneVergleichbarenNamen_zaehltDieLaengeDerZiffernfolge()
    {
        Seed(Postal("GE", "0105", "თბილისი", 41.6938, 44.8015));

        var result = await _service.ResolveAsync("რუსთაველის 12, 0105", null, "GEO");

        Assert.NotNull(result);
        Assert.Equal(GeoSource.PostalCode, result!.Source);
    }

    /// <summary>
    /// Russisch und Ukrainisch schreiben denselben Buchstaben anders um: <c>и</c> ist ukrainisch
    /// ein <c>y</c> („Київ" -> kyiv), russisch ein <c>i</c> („Истра" -> istra) — und GeoNames haelt
    /// es genauso. Mit einer Tabelle fuer beide findet in einem der Laender kein einziger Text
    /// seinen Ort. Russland ist mit 2 003 Turnieren die groesste Verortungsluecke.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_RussischerText_findetDenLateinischenLexikonnamen()
    {
        Seed(City("RU", "Istra", 55.9142, 36.8697, 33_000),
             City("RU", "Vladivostok", 43.1155, 131.8855, 600_000));

        Assert.Equal("Istra", (await _service.ResolveAsync("Истра", null, "RUS"))!.PlaceName);
        Assert.Equal("Vladivostok", (await _service.ResolveAsync("Владивосток", null, "RUS"))!.PlaceName);
    }

    /// <summary>Und die ukrainische Schreibweise bleibt dabei, wie sie ist.</summary>
    [Fact]
    public async Task ResolveAsync_UkrainischerText_bleibtBeiDerUkrainischenUmschrift()
    {
        Seed(City("UA", "Kyiv", 50.4547, 30.5238, 2_800_000));

        Assert.Equal("Kyiv", (await _service.ResolveAsync("Київ", null, "UKR"))!.PlaceName);
    }

    /// <summary>
    /// Ein Lexikon-Name, von dem nur eine ZAHL uebrig bleibt, darf nichts bestaetigen. Die
    /// russischen Postleitzahl-Zeilen heissen teils „Москва 194"; in der ersten Suchform faellt
    /// das Kyrillische weg und es bleibt <c>194</c>. Wuerde das als Ortsname gelten, bestaetigte
    /// es im Text eine HAUSNUMMER — genau der Fehler, gegen den die Bestaetigung gebaut ist.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ZahlAlsOrtsname_bestaetigtKeineHausnummer()
    {
        // Die Zeile mit dem Zahlen-Namen traegt eine dreistellige „Postleitzahl", die im Text als
        // Hausnummer steht. Die echte Postleitzahl gehoert zu Istra.
        Seed(new GeoPlace
             {
                 Country = "RU", PostalCode = "194", Name = "194",
                 Lat = 10.0, Lon = 10.0, Kind = GeoPlaceKind.PostalCode,
             },
             Postal("RU", "143500", "Istra", 55.9142, 36.8697));

        var result = await _service.ResolveAsync("ул. Ленина 194, 143500 Истра", null, "RUS");

        Assert.NotNull(result);
        Assert.Equal("Istra", result!.PlaceName);
    }

    /// <summary>
    /// Der Name mit Zahl DAHINTER bestaetigt dagegen weiter: „Москва 194" traegt „moskva", und
    /// darauf kommt es an.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_NameMitZahl_bestaetigtWeiterhin()
    {
        Seed(Postal("RU", "123456", "Москва 194", 55.7558, 37.6173));

        var result = await _service.ResolveAsync("ул. Тверская 8, 123456 Москва", null, "RUS");

        Assert.NotNull(result);
        Assert.Equal(GeoSource.PostalCode, result!.Source);
    }

    // ----- Postleitzahlen ---------------------------------------------------

    [Fact]
    public async Task ResolveAsync_PostalCodeInAddress_Wins()
    {
        Seed(Postal("AT", "5400", "Hallein", 47.6833, 13.1),
             City("AT", "Wien", 48.2082, 16.3738, 1_900_000));

        var result = await _service.ResolveAsync("Rifer Hauptstraße 37 5400 Hallein (RIF)", "Salzburg", "AUT");

        Assert.NotNull(result);
        Assert.Equal(GeoSource.PostalCode, result!.Source);
        Assert.Equal("Hallein", result.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_HouseNumberThatLooksLikeAPostalCode_DoesNotWin()
    {
        // "351" ist eine Hausnummer, "8051" die PLZ. Beide sind Ziffernfolgen; nur die PLZ steht
        // im Gazetteer, und ihr Ortsname steht auch im Text.
        Seed(Postal("AT", "8051", "Graz", 47.0707, 15.4395),
             Postal("AT", "351", "Irgendwo", 40.0, 10.0));

        var result = await _service.ResolveAsync("Shopping Nord, Wienerstraße 351, 8051 Graz", "Steiermark", "AUT");

        Assert.Equal("Graz", result!.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_NoPostalCode_FallsBackToPlaceName()
    {
        Seed(City("AT", "Ranshofen", 48.2333, 13.0333, 3000));

        var result = await _service.ResolveAsync("Ranshofen", "Salzburg", "AUT");

        Assert.Equal(GeoSource.City, result!.Source);
        Assert.Equal("Ranshofen", result.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_MultiWordPlace_BeatsItsSingleWordParts()
    {
        Seed(City("AT", "Bad Ischl", 47.7117, 13.6231, 14000),
             City("AT", "Ischl", 40.0, 10.0, 500_000));

        var result = await _service.ResolveAsync("Sparkassensaal Bad Ischl, Auböckplatz 1", null, "AUT");

        // Trotz kleinerer Einwohnerzahl muss der laengere Name gewinnen.
        Assert.Equal("Bad Ischl", result!.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousNameFarApart_NoLongerGuessesByPopulation()
    {
        // Bis 0.418.0 gewann hier die groessere Einwohnerzahl. Fuer weit auseinanderliegende
        // Gleichnamige ist das eine Muenze: am Dev-Stand 171 solche Eintraege, davon 29 mit
        // nachweislich falschem Pin (bis 489 km daneben). „Neustadt" gibt es in Deutschland
        // dutzendfach — die Turnhalle steht nicht zwingend in der groesseren Stadt.
        Seed(City("DE", "Neustadt", 49.35, 8.14, 53_000),
             City("DE", "Neustadt", 51.02, 13.75, 900));

        var result = await _service.ResolveAsync("Turnhalle Neustadt", null, "GER");

        Assert.Equal(GeoSource.Ambiguous, result!.Source);
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousNameCloseTogether_StillPicksTheLargestPlace()
    {
        // Dicht beieinander ist die Wahl gleichgueltig — dort bleibt es bei der Einwohnerzahl.
        Seed(City("DE", "Neustadt", 49.35, 8.14, 53_000),
             City("DE", "Neustadt", 49.37, 8.16, 900));

        var result = await _service.ResolveAsync("Turnhalle Neustadt", null, "GER");

        Assert.Equal(GeoSource.City, result!.Source);
        Assert.Equal(49.35, result.Lat, 2);
    }

    [Fact]
    public async Task ResolveAsync_UnknownPlace_FallsBackToRegionCentroid()
    {
        Seed(Region("AT", "Steiermark", 47.2, 15.0));

        var result = await _service.ResolveAsync("Volkshaus Irgendwo", "Steiermark", "AUT");

        Assert.Equal(GeoSource.Region, result!.Source);
        Assert.Equal(47.2, result.Lat, 2);
    }

    [Fact]
    public async Task ResolveAsync_StateIsDash_IsNotTreatedAsARegion()
    {
        // chess-results setzt in der State-Spalte gelegentlich einen blossen Bindestrich.
        Seed(Region("AT", "-", 0, 0));

        Assert.Null(await _service.ResolveAsync("Nirgendwo", "-", "AUT"));
    }

    [Fact]
    public async Task ResolveAsync_NothingMatches_ReturnsNull_InsteadOfAWrongPin()
    {
        Seed(City("AT", "Wien", 48.2082, 16.3738, 1_900_000));

        Assert.Null(await _service.ResolveAsync("Unbekannter Ort", null, "AUT"));
    }

    [Fact]
    public async Task ResolveAsync_UnknownFederation_StillResolvesByPlaceName()
    {
        // Fuer exotische Foederationen gibt es keine ISO-Zuordnung; die Ortssuche laeuft dann
        // ohne Landfilter weiter, statt aufzugeben.
        Seed(City("MN", "Ulaanbaatar", 47.9077, 106.8832, 1_400_000));

        var result = await _service.ResolveAsync("Ulaanbaatar", null, "MGL");

        Assert.Equal("Ulaanbaatar", result!.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_PostalCodeOfAnotherCountry_IsNotUsed()
    {
        Seed(Postal("DE", "5400", "Irgendwo in Deutschland", 51.0, 10.0));

        Assert.Null(await _service.ResolveAsync("5400 Hallein", null, "AUT"));
    }

    // ----- Mehrere Spielorte -----------------------------------------------

    /// <summary>Ein verlaesslich verorteter Anker fuer die Dichte-Entscheidung.</summary>
    private void Anchor(double lat, double lon, GeoSource source = GeoSource.PostalCode)
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = Guid.NewGuid().ToString("N")[..8], ChessResultsId = Guid.NewGuid().ToString("N")[..8], Name = "Anker",
            Lat = lat, Lon = lon, GeoSource = source,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task ResolveMany_TwoVenuesSeparatedByComma_YieldsBoth()
    {
        // Der Fall aus der Abnahme: „Mayrhofen, St.Veit" ist eine Liga mit ZWEI Spielorten.
        // Ohne Zerlegung bildete die Kandidatenerzeugung „st veit" ueber das Komma hinweg und
        // gewann gegen „mayrhofen", weil laengere Wortfolgen kuerzere schlagen — der Pin sass
        // 250 km entfernt.
        Seed(City("AT", "Mayrhofen", 47.17, 11.87, 3900),
             City("AT", "St. Veit", 46.77, 14.36, 12600));

        var venues = await _service.ResolveManyAsync("Mayrhofen, St.Veit", null, "AUT");

        Assert.Equal(2, venues.Count);
        Assert.Equal("Mayrhofen", venues[0].PlaceName);          // erster im Text = Hauptort
        Assert.Equal("St. Veit", venues[1].PlaceName);
    }

    [Fact]
    public async Task ResolveMany_ThreeVenuesWithSlashes_YieldsAll()
    {
        Seed(City("AT", "Schwaz", 47.35, 11.71, 13600),
             City("AT", "Jenbach", 47.39, 11.78, 7100),
             City("AT", "Kufstein", 47.58, 12.17, 19000));

        var venues = await _service.ResolveManyAsync("Schwaz/Jenbach/Kufstein", null, "AUT");

        Assert.Equal(["Schwaz", "Jenbach", "Kufstein"], venues.Select(v => v.PlaceName));
    }

    [Fact]
    public async Task ResolveMany_AddressWithCommas_IsONEVenue()
    {
        // Adressen benutzen dieselben Trenner wie Ortslisten. Der PLZ-Weg laeuft deshalb VOR der
        // Zerlegung — sonst waere jede Adresse ploetzlich drei Spielorte.
        Seed(Postal("AT", "5020", "Salzburg", 47.80, 13.04));

        var venues = await _service.ResolveManyAsync(
            "ASKOE Sportzentrum, Eichetstrasse 29-31, 5020 Salzburg", null, "AUT");

        Assert.Single(venues);
        Assert.Equal(GeoSource.PostalCode, venues[0].Source);
    }

    [Fact]
    public async Task ResolveMany_TwoPostalCodesFarApart_AreTwoVenues()
    {
        Seed(Postal("AT", "6290", "Mayrhofen", 47.17, 11.87),
             Postal("AT", "9300", "St. Veit an der Glan", 46.77, 14.36));

        var venues = await _service.ResolveManyAsync(
            "6290 Mayrhofen / 9300 St. Veit an der Glan", null, "AUT");

        Assert.Equal(2, venues.Count);
    }

    [Fact]
    public async Task ResolveMany_AbbreviatedPlaceNameStillKeepsItsPostalCode()
    {
        // chess-results schreibt „St.Veit", im Ortslexikon steht „St. Veit an der Glan".
        // Ohne die Wortanfang-Pruefung verliert dieser Spielort seine Postleitzahl — und damit
        // die verlaesslichste Verortung, die es fuer ihn gibt.
        Seed(Postal("AT", "9300", "St. Veit an der Glan", 46.77, 14.36));

        var venues = await _service.ResolveManyAsync("9300 St.Veit", null, "AUT");

        Assert.Single(venues);
        Assert.Equal(GeoSource.PostalCode, venues[0].Source);
        Assert.Equal(46.77, venues[0].Lat, 2);
    }

    [Fact]
    public async Task ResolveMany_NeighbouringPostalCodes_AreONEVenue()
    {
        // 24 Wiener Postleitzahlen sind ein Ort, nicht 24.
        Seed(Postal("AT", "1010", "Wien", 48.208, 16.372),
             Postal("AT", "1020", "Wien", 48.216, 16.400));

        var venues = await _service.ResolveManyAsync("1010 Wien und 1020 Wien", null, "AUT");

        Assert.Single(venues);
    }

    // ----- Mehrdeutige Ortsnamen -------------------------------------------

    [Fact]
    public async Task Resolve_AmbiguousNameFarApart_WithoutAnchors_GivesNoCoordinates()
    {
        // „Muenster" gibt es 19-mal, bis 489 km auseinander. Ein Pin, der Genauigkeit behauptet
        // und sie nicht hat, ist schlimmer als kein Pin.
        Seed(Postal("DE", "48143", "Münster", 51.96, 7.63),
             Postal("DE", "84579", "Münster", 48.26, 12.71));

        var result = await _service.ResolveAsync("Münster", null, "GER");

        Assert.NotNull(result);
        Assert.Equal(GeoSource.Ambiguous, result!.Source);
        Assert.Equal(0, result.Lat);
    }

    [Fact]
    public async Task Resolve_AmbiguousNameFarApart_IsDecidedByTournamentDensity()
    {
        // Wo ein Schachklub Turniere austraegt, stehen mehrere verlaessliche Pins.
        Seed(Postal("AT", "4020", "Linz", 48.31, 14.29),
             Postal("AT", "8530", "Linz", 46.75, 15.13));
        Anchor(48.30, 14.30);
        Anchor(48.32, 14.28);

        var result = await _service.ResolveAsync("Linz, Oberbank Donau Forum", null, "AUT");

        Assert.Equal(GeoSource.City, result!.Source);
        Assert.Equal(48.31, result.Lat, 2);
    }

    [Fact]
    public async Task Resolve_Density_IgnoresPinsThatAreThemselvesGuesses()
    {
        // An „Baernbach" nachgestellt: zaehlte man ALLE Pins, waehlten die falschen Pins den
        // falschen Ort — die Fehler bestaetigten sich selbst. Nur PLZ/manuell zaehlen.
        Seed(Postal("AT", "8572", "Bärnbach", 47.07, 15.13),
             Postal("AT", "6373", "Bärnbach", 47.47, 12.42));
        Anchor(47.07, 15.13);                                   // verlaesslich, richtiger Ort
        Anchor(47.47, 12.42, GeoSource.City);                   // geraten
        Anchor(47.47, 12.42, GeoSource.City);                   // geraten
        Anchor(47.47, 12.42, GeoSource.City);                   // geraten

        var result = await _service.ResolveAsync("Volkshaus Bärnbach", null, "AUT");

        Assert.Equal(47.07, result!.Lat, 2);
    }

    [Fact]
    public async Task Resolve_SameNameCloseTogether_StillPicksWithoutAsking()
    {
        // 24 Wiener Postleitzahlen sind kein Zweifelsfall — dort entscheidet wie bisher die
        // Einwohnerzahl, ohne Umweg ueber die Dichte.
        Seed(Postal("AT", "1010", "Wien", 48.208, 16.372),
             Postal("AT", "1020", "Wien", 48.216, 16.400));

        var result = await _service.ResolveAsync("alle Wien (Schachhaus)", null, "AUT");

        // Kein Zweifelsfall, also auch keine Nachfrage bei der Dichte: die 24 Wiener
        // Postleitzahlen liegen alle in Wien, jede Wahl ist richtig.
        Assert.Equal(GeoSource.City, result!.Source);
        Assert.Equal(48.2, result.Lat, 1);
    }

    [Fact]
    public async Task Resolve_AmbiguousButRegionKnown_FallsBackToTheRegionCentre()
    {
        Seed(Postal("DE", "48143", "Münster", 51.96, 7.63),
             Postal("DE", "84579", "Münster", 48.26, 12.71),
             Region("DE", "Bavaria", 48.80, 11.28));

        var result = await _service.ResolveAsync("Münster", "Bavaria", "GER");

        Assert.Equal(GeoSource.Region, result!.Source);
    }

    [Fact]
    public async Task ResolveMany_AddressWithoutPostalCode_IsONEVenue_TheLastPlaceInIt()
    {
        // Gemessen am Dev-Stand: von 262 als mehrortig erkannten Eintraegen hatten 191 eine
        // ZIFFER im Text und waren durchweg Adressen, die die Zerlegung zerschnitten hat —
        // „Festsaal der Gemeinde Schwarzach, Marktplatz 4, Schwarzach" wurde zu ZWEI Spielorten
        // desselben Ortes. Ohne Postleitzahl (Brasilien, Argentinien: keine im Ortslexikon) traf
        // das die zwei groessten Foederationen im Bestand.
        Seed(City("AT", "Hauptplatz", 48.0, 14.0, 200),
             City("AT", "Ansfelden", 48.21, 14.29, 16000));

        var venues = await _service.ResolveManyAsync(
            "Rathauskeller, Hauptplatz 40, Haid/Ansfelden", null, "AUT");

        // Ein Ort — und zwar der HINTERE: vorn stehen Gebaeude und Strasse.
        Assert.Single(venues);
        Assert.Equal("Ansfelden", venues[0].PlaceName);
    }

    [Fact]
    public async Task ResolveMany_SameTownTwiceInAnAddress_IsNotTwoVenues()
    {
        Seed(City("AT", "Schwarzach", 47.32, 13.15, 3600));

        var venues = await _service.ResolveManyAsync(
            "Festsaal der Gemeinde Schwarzach, Marktplatz 4, Schwarzach", null, "AUT");

        Assert.Single(venues);
    }

    // ----- Der Schraegstrich: Ortsliste oder abgekuerzter Name? -------------

    /// <summary>
    /// Der gemeldete Fehlgriff (tnr1351833): Ortstext „Vereinstreff St. Veit/Glan", Bundesland
    /// Kaernten — der Pin sass in TIROL, 250 km entfernt. Der Schraegstrich wurde als
    /// Spielort-Trenner verbraucht, „St. Veit" gibt es im Lexikon genau EINMAL, und damit sah der
    /// Eintrag vollkommen eindeutig aus. Nichts daran war als Zweifelsfall erkennbar.
    /// </summary>
    [Fact]
    public async Task ResolveMany_AbbreviatedNameWithSlash_FindsTheOfficialPlace()
    {
        Seed(City("AT", "St. Veit", 47.3167, 11.0667, 0),                 // Tirol
             City("AT", "St. Veit an der Glan", 46.7681, 14.3603, 12500)); // Kaernten

        var venues = await _service.ResolveManyAsync("Vereinstreff St. Veit/Glan", "Kärnten", "AUT");

        var venue = Assert.Single(venues);
        Assert.Equal("St. Veit an der Glan", venue.PlaceName);
    }

    /// <summary>
    /// Dasselbe Muster, anderer Ort: „Spittal/Drau" ist „Spittal an der Drau". Vorher landete der
    /// Pin auf dem FLUSS-Abschnitt „Drau", weil der als eigener Spielort gelesen wurde.
    /// </summary>
    [Fact]
    public async Task ResolveMany_SlashBeforeARiverName_IsOnePlace()
    {
        Seed(City("AT", "Drau", 46.60, 13.20, 0),
             City("AT", "Spittal an der Drau", 46.7889, 13.4972, 15400));

        var venues = await _service.ResolveManyAsync("Spittal/Drau", "Kärnten", "AUT");

        Assert.Equal("Spittal an der Drau", Assert.Single(venues).PlaceName);
    }

    /// <summary>
    /// Auch ein EINZELNER Buchstabe hinter dem Schraegstrich ist eine Abkuerzung: „Frankfurt/M"
    /// ist „Frankfurt am Main". Verglichen wird Wortanfang gegen Wortanfang, ohne Bindewoerter.
    /// </summary>
    [Fact]
    public async Task ResolveMany_SingleLetterAbbreviation_IsResolved()
    {
        Seed(City("DE", "Frankfurt am Main", 50.1106, 8.6822, 750000),
             City("DE", "Frankfurt (Oder)", 52.3412, 14.5487, 57000));

        var venues = await _service.ResolveManyAsync("Frankfurt/M", null, "GER");

        Assert.Equal("Frankfurt am Main", Assert.Single(venues).PlaceName);
    }

    /// <summary>
    /// Die Gegenprobe, die die erste Fassung dieser Regel umgeworfen hat: „Schwaz" allein ist ein
    /// Treffer, beweist aber NICHT, dass „Schwaz/Jenbach/Kufstein" ein Ort ist. Gewertet werden
    /// nur Wortfolgen, die den Schraegstrich UEBERSPANNEN.
    /// </summary>
    [Fact]
    public async Task ResolveMany_RealVenueList_IsNotCollapsedIntoOne()
    {
        Seed(City("AT", "Schwaz", 47.35, 11.71, 13600),
             City("AT", "Jenbach", 47.39, 11.78, 7100),
             City("AT", "Kufstein", 47.58, 12.17, 19000));

        var venues = await _service.ResolveManyAsync("Schwaz/Jenbach/Kufstein", null, "AUT");

        Assert.Equal(["Schwaz", "Jenbach", "Kufstein"], venues.Select(v => v.PlaceName));
    }

    /// <summary>
    /// Die brasilianische Schreibweise Ort/Bundesstaat — 83 der 105 Schraegstrich-Faelle am
    /// Dev-Stand. „sorocaba sp" hat zwei unterscheidende Woerter, „Sorocaba" eines: die Wortzahl
    /// passt nicht, es ist keine Abkuerzung, und die Zerlegung bleibt zustaendig.
    /// </summary>
    [Fact]
    public async Task ResolveMany_PlaceWithStateSuffix_StillResolvesToThePlace()
    {
        Seed(City("BR", "Sorocaba", -23.5015, -47.4526, 687000));

        var venues = await _service.ResolveManyAsync("Sorocaba/SP", "São Paulo (SP)", "BRA");

        Assert.Equal("Sorocaba", Assert.Single(venues).PlaceName);
    }

    /// <summary>
    /// Ein abgekuerzter Name darf nicht raten: passt der Wortanfang auf ZWEI weit auseinander
    /// liegende Orte, gibt es keinen Pin — dieselbe Regel wie bei jedem anderen mehrdeutigen
    /// Namen.
    /// </summary>
    [Fact]
    public async Task ResolveMany_AmbiguousAbbreviation_GetsNoPin()
    {
        Seed(City("AT", "Neumarkt an der Ybbs", 48.10, 15.10, 1000),
             City("AT", "Neumarkt am Wallersee", 47.95, 13.23, 6300));

        var venues = await _service.ResolveManyAsync("Neumarkt/W", null, "AUT");

        // „neumarkt w" trifft „…am Wallersee", nicht „…an der Ybbs" — ein eindeutiger Treffer.
        Assert.Equal("Neumarkt am Wallersee", Assert.Single(venues).PlaceName);
    }

    [Fact]
    public async Task ResolveMany_VenueListWithoutDigits_StaysMultiVenue()
    {
        // Die Gegenprobe: eine echte Liste hat keine Hausnummer.
        Seed(City("AT", "Bad Häring", 47.51, 12.11, 2600),
             City("AT", "Schwaz", 47.35, 11.71, 13600),
             City("AT", "Kufstein", 47.58, 12.17, 19000));

        var venues = await _service.ResolveManyAsync("Bad Häring/Schwaz/Kufstein", null, "AUT");

        Assert.Equal(3, venues.Count);
    }
}

public class GazetteerImportParsingTests
{
    [Fact]
    public void ParsePostalLines_BuildsPlacesAndRegionCentroids()
    {
        string[] lines =
        [
            "AT\t5400\tHallein\tSalzburg\t05\tHallein\t504\t\t\t47.6833\t13.1000\t4",
            "AT\t5020\tSalzburg\tSalzburg\t05\tSalzburg\t501\t\t\t47.8000\t13.0333\t4",
            "AT\t8010\tGraz\tSteiermark\t06\tGraz\t601\t\t\t47.0667\t15.4500\t4",
        ];

        var places = GazetteerImportService.ParsePostalLines("AT", lines, out var skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(3, places.Count(p => p.Kind == GeoPlaceKind.PostalCode));

        var regions = places.Where(p => p.Kind == GeoPlaceKind.Region).ToList();
        Assert.Equal(2, regions.Count);
        var salzburg = regions.Single(r => r.Name == "Salzburg");
        Assert.Equal((47.6833 + 47.8000) / 2, salzburg.Lat, 4);
        Assert.Equal("salzburg", salzburg.NameNormalized);
    }

    [Fact]
    public void ParsePostalLines_SkipsTruncatedAndUnparsableRows()
    {
        string[] lines =
        [
            "AT\t5400\tHallein",                                                  // zu kurz
            "AT\t5020\tSalzburg\tSalzburg\t05\t\t\t\t\tkeine-zahl\t13.0\t4",      // Koordinate kaputt
            "AT\t\tOhnePlz\tSalzburg\t05\t\t\t\t\t47.8\t13.0\t4",                 // ohne PLZ
            "AT\t8010\tGraz\tSteiermark\t06\t\t\t\t\t47.0667\t15.4500\t4",        // gut
        ];

        var places = GazetteerImportService.ParsePostalLines("AT", lines, out var skipped);

        Assert.Equal(3, skipped);
        Assert.Single(places, p => p.Kind == GeoPlaceKind.PostalCode);
    }

    [Fact]
    public void ParseCityLines_ReadsNameCountryCoordinatesAndPopulation()
    {
        var line = string.Join('\t',
            "2761369", "Vienna", "Vienna", "Wien,Wien city", "48.20849", "16.37208",
            "P", "PPLC", "AT", "", "09", "900", "90001", "", "1691468", "", "175",
            "Europe/Vienna", "2026-01-01");

        var places = GazetteerImportService.ParseCityLines([line], out var skipped);

        var city = Assert.Single(places);
        Assert.Equal(0, skipped);
        Assert.Equal("Vienna", city.Name);
        Assert.Equal("AT", city.Country);
        Assert.Equal(48.20849, city.Lat, 5);
        Assert.Equal(1_691_468, city.Population);
        Assert.Equal(GeoPlaceKind.City, city.Kind);
    }
}
