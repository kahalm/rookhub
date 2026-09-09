using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die reinen Hilfsfunktionen rund ums Turnierverzeichnis: Bedenkzeit-Einordnung, Textfaltung
/// fuers Geocoding, Distanzrechnung und der Aenderungs-Hash.
/// </summary>
public class TournamentSpeedClassifierTests
{
    [Theory]
    // Echte Bedenkzeit-Texte aus der AUT-Trefferliste.
    [InlineData("90 min/40 moves + 15 min rest + 30 sec", TournamentSpeed.Standard)]
    [InlineData("120 min/40 Zuege + 30 Minuten fuer Rest + 30 sec", TournamentSpeed.Standard)]
    [InlineData("90min + 30s/move", TournamentSpeed.Standard)]
    [InlineData("10 Minuten + 3 Sekunden / Zug", TournamentSpeed.Rapid)]
    [InlineData("15 Minuten", TournamentSpeed.Rapid)]
    [InlineData("1 Std.", TournamentSpeed.Rapid)]
    [InlineData("3min + 2sek/Zug", TournamentSpeed.Blitz)]
    [InlineData("1min+2sec", TournamentSpeed.Blitz)]
    [InlineData("7 min + 5 sec/Zug", TournamentSpeed.Rapid)]
    // Am Dev-Stand tatsaechlich vorgefunden — sechs Turniere blieben ohne Klasse, obwohl die
    // Bedenkzeit dastand: das MINUTENZEICHEN, die nackte Kurzform und eine fremde Sprache.
    [InlineData("90'/40m + 30'/end & 30\"/m", TournamentSpeed.Standard)]
    [InlineData("90'/40 moves + 30'/end & 30\"/move from move 1", TournamentSpeed.Standard)]
    [InlineData("90+30", TournamentSpeed.Standard)]
    [InlineData("10 minuta po igraču", TournamentSpeed.Rapid)]
    [InlineData("5+3", TournamentSpeed.Blitz)]
    public void Classify_RealWorldTimeControls(string text, TournamentSpeed expected)
        => Assert.Equal(expected, TournamentSpeedClassifier.Classify(text));

    /// <summary>
    /// Die nackte Kurzform gilt nur, wenn der Text aus NICHTS anderem besteht — sonst verschluckt
    /// sie Zahlenpaare aus Fliesstext („Runde 1+2 am Samstag") und macht daraus eine Bedenkzeit.
    /// </summary>
    [Theory]
    [InlineData("Runde 1+2 am Samstag")]
    [InlineData("Gruppe A 3+4")]
    public void Classify_ShorthandOnlyCountsWhenItIsTheWholeText(string text)
        => Assert.Equal(TournamentSpeed.Unknown, TournamentSpeedClassifier.Classify(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nach Vereinbarung")]
    public void Classify_WithoutUsableTime_IsUnknown(string? text)
        => Assert.Equal(TournamentSpeed.Unknown, TournamentSpeedClassifier.Classify(text));

    [Fact]
    public void TotalMinutes_UsesFirstTimeAndAddsIncrement()
    {
        // "90 min fuer 40 Zuege + 30 min Rest" ist ein 90-Minuten-Turnier mit Zusatzphase,
        // kein 30-Minuten-Turnier: die ERSTE Angabe zaehlt.
        Assert.Equal(120, TournamentSpeedClassifier.TotalMinutes("90 min/40 Z + 30 min Rest + 30 Sek"));
    }

    [Fact]
    public void TotalMinutes_AbsurdValue_IsRejected()
        => Assert.Null(TournamentSpeedClassifier.TotalMinutes("9000 min"));
}

public class GeoTextNormalizerTests
{
    [Theory]
    [InlineData("Hauptstraße", "hauptstrasse")]
    [InlineData("Mürzzuschlag", "murzzuschlag")]
    [InlineData("Sankt Pölten", "sankt polten")]
    [InlineData("Zürich", "zurich")]
    [InlineData("Plzeň", "plzen")]
    [InlineData("  Wien , 1010 ", "wien 1010")]
    [InlineData(null, "")]
    public void Normalize_FoldsUmlautsAndDiacritics(string? input, string expected)
        => Assert.Equal(expected, GeoTextNormalizer.Normalize(input));

    [Fact]
    public void PostalCandidates_FindsCodeAndIgnoresShortHouseNumbers()
    {
        var candidates = GeoTextNormalizer.PostalCandidates("Rifer Hauptstraße 37 5400 Hallein (RIF)");

        Assert.Contains("5400", candidates);
        Assert.DoesNotContain("37", candidates);
    }

    /// <summary>
    /// Die britischen Inseln schreiben ihre Postleitzahl alphanumerisch. Bis zur dritten Runde war
    /// der Ausdruck rein numerisch — „CF31 3NR" war damit keine Postleitzahl, sondern gar nichts,
    /// und die walisische Quelle liefert sie bei 30 von 38 Turnieren mit.
    /// </summary>
    [Theory]
    [InlineData("Bridgend Life Centre, Bridgend CF31 3NR", "CF31 3NR")]
    [InlineData("Pembroke, SA714LA", "SA714LA")]
    [InlineData("The Clissold Arms, London N2 9HR", "N2 9HR")]
    [InlineData("Durham Clayport Library, Durham DH1 1WA", "DH1 1WA")]
    public void PostalCandidates_FindsBritishCodes(string text, string expected) =>
        Assert.Contains(expected.Replace(" ", ""),
            GeoTextNormalizer.PostalCandidates(text).Select(c => c.Replace(" ", "")));

    /// <summary>
    /// Und beide Schreibweisen werden angeboten: die Quelle klebt die Postleitzahl gern an die
    /// Anschrift („SA714LA"), das Lexikon speichert sie mit Leerzeichen („SA71 4LA"). Welche
    /// stimmt, entscheidet der Gazetteer-Treffer — nicht dieser Ausdruck.
    /// </summary>
    [Fact]
    public void PostalCandidates_OffersBothSpellingsOfABritishCode()
    {
        var candidates = GeoTextNormalizer.PostalCandidates("Pembroke SA714LA");

        Assert.Contains("SA714LA", candidates);
        Assert.Contains("SA71 4LA", candidates);
    }

    /// <summary>Irische Eircodes trennen nach den ERSTEN drei Zeichen, nicht vor den letzten drei.</summary>
    [Fact]
    public void PostalCandidates_OffersTheIrishSpellingToo()
    {
        var candidates = GeoTextNormalizer.PostalCandidates("Dublin D02XY45");

        Assert.Contains("D02 XY45", candidates);
    }

    /// <summary>
    /// Und der bestehende Ziffern-Weg bleibt unberuehrt — das ist der Fall, den fast ganz Europa
    /// benutzt.
    /// </summary>
    [Theory]
    [InlineData("Halle 1, Eichetstrasse 29, 5020 Salzburg", "5020")]
    [InlineData("Vlcie hrdlo 1/A, 824 12 Bratislava", "824 12")]
    [InlineData("80807 Muenchen", "80807")]
    public void PostalCandidates_StillFindsNumericCodes(string text, string expected) =>
        Assert.Contains(expected, GeoTextNormalizer.PostalCandidates(text));

    [Fact]
    public void PostalCandidates_KeepsCompactVariantOfSeparatedCodes()
    {
        // "SE-114 35" / "00-950": der Gazetteer speichert mal mit, mal ohne Trenner.
        var candidates = GeoTextNormalizer.PostalCandidates("Storgatan 1, 114 35 Stockholm");

        Assert.Contains("114 35", candidates);
        Assert.Contains("11435", candidates);
    }

    [Fact]
    public void PlaceCandidates_LongestPhrasesFirst()
    {
        var candidates = GeoTextNormalizer.PlaceCandidates("Sparkassensaal Bad Ischl, Auböckplatz 1");

        var badIschl = candidates.IndexOf("bad ischl");
        var ischl = candidates.IndexOf("ischl");
        Assert.True(badIschl >= 0 && ischl >= 0);
        // "bad ischl" muss VOR "ischl" probiert werden, sonst gewinnt der falsche Ort.
        Assert.True(badIschl < ischl);
    }

    [Fact]
    public void PlaceCandidates_DropsPureDigits()
    {
        var candidates = GeoTextNormalizer.PlaceCandidates("1090 Wien");

        Assert.Contains("wien", candidates);
        Assert.DoesNotContain(candidates, c => c.All(char.IsDigit));
    }

    // ----- Der Schraegstrich: Ortsliste oder abgekuerzter Name? -------------

    /// <summary>
    /// chess-results kuerzt die Bindewoerter des amtlichen Namens weg. Verglichen wird deshalb
    /// Wort fuer Wort ohne sie, und jedes Textwort muss ein WORTANFANG des zugehoerigen
    /// Gazetteer-Wortes sein.
    /// </summary>
    [Theory]
    [InlineData("st veit glan", "st veit an der glan")]
    [InlineData("spittal drau", "spittal an der drau")]
    [InlineData("frankfurt m", "frankfurt am main")]
    [InlineData("klagenfurt worthersee", "klagenfurt am worthersee")]
    [InlineData("neumarkt w", "neumarkt am wallersee")]
    public void DescribesSamePlace_AbbreviatedName_Matches(string text, string place)
    {
        Assert.True(GeoTextNormalizer.DescribesSamePlace(text, place));
    }

    /// <summary>
    /// Die wichtigere Haelfte: was NICHT als Abkuerzung durchgehen darf. Die Wortzahl ist die
    /// Bedingung, die „Sorocaba/SP" (Ort/Bundesstaat, 83 der 105 Schraegstrich-Faelle am
    /// Dev-Stand) weiterhin durch die Zerlegung laufen laesst.
    /// </summary>
    [Theory]
    [InlineData("sorocaba sp", "sorocaba")]              // Wortzahl passt nicht
    [InlineData("schwaz jenbach", "schwaz")]             // eine echte Ortsliste
    [InlineData("st veit glan", "st veit im jauntal")]   // anderes unterscheidendes Wort
    [InlineData("st veit glan", "st veit am vogau")]
    [InlineData("ischl", "bad ischl")]                   // „bad" gehoert zum Namen, ist kein Bindewort
    [InlineData("", "st veit an der glan")]
    public void DescribesSamePlace_DifferentPlace_DoesNot(string text, string place)
    {
        Assert.False(GeoTextNormalizer.DescribesSamePlace(text, place));
    }

    /// <summary>
    /// Der Schraegstrich ist KEIN Spielort-Trenner mehr — sonst zerfaellt „St. Veit/Glan", noch
    /// bevor es als ein Name geprueft werden kann. Komma, Semikolon, „und" und „&amp;" bleiben.
    /// </summary>
    [Fact]
    public void VenueSegments_SlashIsNotASeparator()
    {
        Assert.Equal(["Schwaz/Jenbach"], GeoTextNormalizer.VenueSegments("Schwaz/Jenbach"));
        Assert.Equal(["Mayrhofen", "St. Veit/Glan"],
            GeoTextNormalizer.VenueSegments("Mayrhofen, St. Veit/Glan"));
        Assert.Equal(["Graz", "Leoben"], GeoTextNormalizer.VenueSegments("Graz und Leoben"));
    }

    [Fact]
    public void SlashParts_SplitsOnlyOnSlashes()
    {
        Assert.Equal(["Schwaz", "Jenbach", "Kufstein"],
            GeoTextNormalizer.SlashParts("Schwaz/Jenbach/Kufstein"));
        Assert.Equal(["Vereinstreff St. Veit", "Glan"],
            GeoTextNormalizer.SlashParts("Vereinstreff St. Veit/Glan"));
        Assert.Empty(GeoTextNormalizer.SlashParts(null));
    }
}

public class GeoDistanceTests
{
    [Fact]
    public void Haversine_KnownDistance_WienGraz()
    {
        // Wien (48.2082/16.3738) -> Graz (47.0707/15.4395): Luftlinie ca. 145 km.
        var km = GeoDistance.Haversine(48.2082, 16.3738, 47.0707, 15.4395);
        Assert.InRange(km, 140, 152);
    }

    [Fact]
    public void Haversine_SamePoint_IsZero()
        => Assert.Equal(0, GeoDistance.Haversine(48.2, 16.3, 48.2, 16.3), 6);

    [Fact]
    public void BoundingBox_EnclosesTheCircle()
    {
        var box = GeoDistance.BoundingBox(48.2082, 16.3738, 100);

        // Ein Punkt genau 100 km noerdlich muss noch in der Box liegen.
        Assert.True(box.MaxLat >= 48.2082 + 100 / 111.33);
        Assert.True(box.MinLat <= 48.2082 - 100 / 111.33);
        // Laengengrade sind auf 48 Grad Breite enger -> groesserer Gradabstand als bei der Breite.
        Assert.True(box.MaxLon - 16.3738 > box.MaxLat - 48.2082);
    }

    [Fact]
    public void BoundingBox_NearPole_DoesNotProduceNaN()
    {
        var box = GeoDistance.BoundingBox(90.0, 0.0, 500);

        Assert.False(double.IsNaN(box.MinLon) || double.IsNaN(box.MaxLon));
        Assert.Equal(90.0, box.MaxLat);
        Assert.InRange(box.MaxLon, 0, 180);
    }
}

public class FideCountryCodesTests
{
    [Theory]
    [InlineData("AUT", "AT")]
    [InlineData("GER", "DE")]
    [InlineData("SUI", "CH")]
    [InlineData("SVK", "SK")]
    [InlineData("SLO", "SI")]
    [InlineData("CRO", "HR")]
    [InlineData("ENG", "GB")]
    [InlineData("SCO", "GB")]
    public void ToIso2_MapsKnownFederations(string fide, string iso) =>
        Assert.Equal(iso, FideCountryCodes.ToIso2(fide));

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("")]
    [InlineData(null)]
    public void ToIso2_UnknownCode_ReturnsNull_RatherThanGuessing(string? fide) =>
        Assert.Null(FideCountryCodes.ToIso2(fide));

    /// <summary>
    /// Der Rueckweg — gebraucht, seit eine Quelle ihr Land als Laenderfaehnchen fuehrt (chess.cz).
    /// </summary>
    [Theory]
    [InlineData("CZ", "CZE")]
    [InlineData("SK", "SVK")]
    [InlineData("cz", "CZE")]
    [InlineData("XX", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FromIso2_MapsBack(string? iso2, string? expected) =>
        Assert.Equal(expected, FideCountryCodes.FromIso2(iso2));

    /// <summary>
    /// Mehrere Foederationen zeigen auf dasselbe Land (ENG/SCO/WLS auf GB). Der Rueckweg braucht
    /// dort eine ENTSCHEIDUNG, und die muss festliegen statt von der Reihenfolge eines Dictionary
    /// abzuhaengen.
    /// </summary>
    [Theory]
    [InlineData("GB", "ENG")]
    [InlineData("MK", "MKD")]
    [InlineData("IM", "IOM")]
    public void FromIso2_ResolvesTheAmbiguousOnesTheSameWayEveryTime(string iso2, string expected) =>
        Assert.Equal(expected, FideCountryCodes.FromIso2(iso2));

    /// <summary>Hin und zurueck muss bei jedem Land wieder dasselbe Land ergeben.</summary>
    [Fact]
    public void FromIso2_AndBack_IsStable()
    {
        foreach (var iso2 in FideCountryCodes.KnownIso2)
        {
            var federation = FideCountryCodes.FromIso2(iso2);
            Assert.NotNull(federation);
            Assert.Equal(iso2, FideCountryCodes.ToIso2(federation));
        }
    }
}

public class FederationCatalogTests
{
    [Fact]
    public void All_CoversTheSearchDropdown()
    {
        // Das Laenderfeld der Turniersuche hatte am 2026-09-06 257 dreibuchstabige Codes.
        Assert.InRange(FederationCatalog.All.Count, 200, 300);
        Assert.All(FederationCatalog.All, code => Assert.Matches("^[A-Z]{3}$", code));
        Assert.Equal(FederationCatalog.All.Count, FederationCatalog.All.Distinct().Count());
    }

    [Fact]
    public void All_ContainsTheNeighbourFederations()
    {
        foreach (var code in new[] { "AUT", "GER", "SUI", "ITA", "CZE", "SVK", "HUN", "SLO", "LIE" })
            Assert.Contains(code, FederationCatalog.All);
    }
}

/// <summary>
/// Zeitsteuerung und Auswahl der gestaffelten Sweeps.
/// </summary>
public class TournamentDirectorySchedulerTests : IDisposable
{
    private readonly RookHub.Api.Data.AppDbContext _db;

    public TournamentDirectorySchedulerTests()
    {
        _db = new RookHub.Api.Data.AppDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void TimeUntilNextRun_BeforeThreeUtc_WaitsUntilToday()
    {
        var delay = TournamentDirectoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 6, 1, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void TimeUntilNextRun_AfterThreeUtc_WaitsUntilTomorrow()
    {
        var delay = TournamentDirectoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 6, 5, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(22), delay);
    }

    [Fact]
    public void TimeUntilNextRun_ExactlyAtRunTime_DoesNotReturnZero()
    {
        // Null Wartezeit wuerde den Loop in derselben Sekunde erneut feuern.
        var delay = TournamentDirectoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc));
        Assert.True(delay >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task BuildRunListAsync_WithoutWeeklyBatch_IsOnlyTheDailyFederations()
    {
        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, ["AUT", "GER"], 0, default);
        Assert.Equal(["AUT", "GER"], run);
    }

    [Fact]
    public async Task BuildRunListAsync_RotationStartsOnAnEmptySweepTable()
    {
        // Die Sweep-Tabelle ist beim ersten Lauf leer; Grundmenge ist deshalb der Katalog.
        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, ["AUT"], 5, default);

        Assert.Equal(6, run.Count);
        Assert.Equal("AUT", run[0]);
        Assert.DoesNotContain("AUT", run.Skip(1));
    }

    [Fact]
    public async Task BuildRunListAsync_PrefersFederationsSweptLongestAgo()
    {
        var now = new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc);
        // Alle Katalog-Foederationen als "gerade eben gesweept" markieren, drei davon aelter.
        foreach (var code in FederationCatalog.All)
        {
            _db.TournamentDirectorySweeps.Add(new RookHub.Api.Models.TournamentDirectorySweep
            {
                Federation = code,
                LastSweptAt = code switch
                {
                    "FRA" => now.AddDays(-9),
                    "ESP" => now.AddDays(-8),
                    "POL" => now.AddDays(-7),
                    _ => now,
                }
            });
        }
        await _db.SaveChangesAsync();

        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, ["AUT"], 3, default);

        Assert.Equal(["AUT", "FRA", "ESP", "POL"], run);
    }

    [Fact]
    public async Task BuildRunListAsync_NeverSweptBeatsEverything()
    {
        var now = new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc);
        foreach (var code in FederationCatalog.All.Where(c => c != "FIJ"))
        {
            _db.TournamentDirectorySweeps.Add(new RookHub.Api.Models.TournamentDirectorySweep
            {
                Federation = code, LastSweptAt = now.AddDays(-30)
            });
        }
        await _db.SaveChangesAsync();

        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, [], 1, default);

        Assert.Equal(["FIJ"], run);
    }

    /// <summary>
    /// Der naechtliche Durchgang MUSS die FIDE-Detailangaben mitholen — und der Dienst muss
    /// registriert sein.
    ///
    /// <para><b>Warum das geprueft wird, und warum als Quelltext-Pruefung.</b> Der Fehler, um den
    /// es geht, ist heute schon zweimal passiert: ein Feature war ausgerollt und trotzdem wirkungslos
    /// (die Spieltermine, weil der Parser die falsche Tabelle nahm; die Turnierart, weil kein Sweep
    /// nach der Auslieferung lief). Ein Nachtrag OHNE Aufruf im Scheduler waere genau derselbe Fall:
    /// jedes neue FIDE-Ereignis kaeme mit Name, Termin und Ort herein und wuerde nie wieder
    /// angefasst — ohne Fehler, ohne Hinweis, nur mit dauerhaft leeren Feldern.</para>
    ///
    /// <para>Die Alternative waere, den ganzen Host hochzufahren, um EINEN Aufruf zu beobachten;
    /// dafuer ist der Scheduler nicht zerlegt (nur <c>TimeUntilNextRun</c> und
    /// <c>BuildRunListAsync</c> sind herausgezogen). Diese Pruefung faengt den Fall, der wirklich
    /// vorkommt — dass der Aufruf vergessen oder beim Umbau entfernt wird.</para>
    /// </summary>
    [Fact]
    public void NightlyRun_AlsoFetchesTheFideEventDetails()
    {
        var scheduler = ReadSource("src/api/RookHub.Api/Services/TournamentDirectoryScheduler.cs");
        var program = ReadSource("src/api/RookHub.Api/Program.cs");

        Assert.Contains("GetRequiredService<FideEventDetailService>()", scheduler);
        Assert.Contains("FideDetailBatchSize", scheduler);
        Assert.Contains("AddScoped<FideEventDetailService>()", program);

        // Und NICHT mit retryEmpty: ein Ereignis ohne gepflegte Angaben ist der haeufige Fall und
        // darf nicht jede Nacht erneut abgefragt werden.
        Assert.Contains("details.RunAsync(_fideDetailBatchSize, retryEmpty: false", scheduler);
    }

    private static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Datei fehlt: {relativePath}");
        return File.ReadAllText(path);
    }
}
