using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.ChessBase;

namespace RookHub.Api.Tests;

/// <summary>
/// Die ChessBase-Leser (0.598.0) an kleinen, hier zusammengesetzten Datenbanken — die echten Dateien (eigene Partien,
/// Morphys Weltmeisterschafts-Sammlung) prüft <see cref="ChessBaseFixtureTests"/> nur lokal. Die Zahlen stehen LITERAL
/// in der Formatbeschreibung (Morphy <c>format/v1</c>, <c>format/v2</c>).
/// </summary>
public class ChessBaseReaderTests
{
    // ── Tabellen und Kodierungen ──────────────────────────────────────────────────────────────

    [Fact]
    public void V1Tables_ArePermutations()
    {
        foreach (var t in new[] { CbhTables.Mode0, CbhTables.Mode4, CbhTables.Mode5, CbhTables.Mode10 })
            Assert.Equal(Enumerable.Range(0, 256), t.Select(b => (int)b).OrderBy(b => b));
        Assert.Equal(0xa2, CbhTables.Mode0[0]);                               // erste Zeile der Tabelle im Spec
        Assert.Equal(0x80, CbhTables.Mode0[255]);
    }

    [Fact]
    public void V2MoveTable_MatchesTheDocumentedBlocks()
    {
        Assert.Equal(0xb129, Cb2MoveTable.Count);
        Assert.Equal(new Cb2MoveTable.MoveWord(true, 0, 8, 0, 0), Cb2MoveTable.Decode(0x0001));      // König a1-a2
        Assert.Equal(new Cb2MoveTable.MoveWord(true, 0, 1, 0, 0), Cb2MoveTable.Decode(0x0007));      // König a1-b1
        Assert.Equal(new Cb2MoveTable.MoveWord(true, 0, 9, 0, 0), Cb2MoveTable.Decode(0x09d9));      // erste Damenzug a1-b2
        Assert.Equal(new Cb2MoveTable.MoveWord(false, 0, 8, 0, 0), Cb2MoveTable.Decode(0x55f9));     // schwarzer König a1-a2
        Assert.Equal(new Cb2MoveTable.MoveWord(true, 8, 24, 0, 0), Cb2MoveTable.Decode(0xabf1));     // a2-a4
        Assert.Equal(new Cb2MoveTable.MoveWord(false, 8, 0, MainlineBoard.Queen, 0), Cb2MoveTable.Decode(0xae8d)); // a2-a1=D
        Assert.Equal(new Cb2MoveTable.MoveWord(true, 0, 0, 0, 1), Cb2MoveTable.Decode(0xb12a));      // weiß O-O
        Assert.Equal(new Cb2MoveTable.MoveWord(false, 0, 0, 0, -1), Cb2MoveTable.Decode(0xb12b));    // schwarz O-O-O
        Assert.Null(Cb2MoveTable.Decode(0xb12d));                                                    // Chess960
        Assert.Equal((true, MainlineBoard.Rook, 6), Cb2MoveTable.DecodePiece(0xc02d + 4 * 64 + 6 * 8)); // weißer Turm g1
        Assert.Equal((false, MainlineBoard.Pawn, 8), Cb2MoveTable.DecodePiece(0xc2dd));                // schwarzer Bauer a2
    }

    [Fact]
    public void UploadExtensions_MirrorLeagueHub()
    {
        // SPIEGEL von CHESSBASE_UPLOAD_EXTENSIONS in src-leaguehub/app/core/chessbase-upload.ts — die Seite schickt nur diese.
        Assert.Equal(new[] { ".cbh", ".cbg", ".cbp", ".cbt", ".cbc", ".2cbh", ".2cbg", ".2lid" }, ChessBaseFiles.Upload);
        Assert.All(ChessBaseFiles.Required.Values.SelectMany(e => e), e => Assert.Contains(e, ChessBaseFiles.Upload));
    }

    [Fact]
    public void V1SetupPosition_ReadsTheBitStream()
    {
        // Das Beispiel der Formatbeschreibung: 58 0e 93 = weißer Bauer a2, schwarzer Turm b1, weißer Springer b4.
        var s = new byte[28];
        s[0] = 1;
        s[3] = 1;
        s[4] = 0x58; s[5] = 0x0e; s[6] = 0x93;
        Assert.Equal("8/8/8/8/1N6/8/P7/1r6 w - - 0 1", CbhReader.SetupFen(s));
    }

    [Theory]
    [InlineData(0, "????.??.??")]
    [InlineData(2025 << 9, "2025.??.??")]
    [InlineData((2025 << 9) | (12 << 5) | 5, "2025.12.05")]
    public void Dates(int packed, string pgn) => Assert.Equal(pgn, ChessBaseFields.Date(packed));

    [Fact]
    public void Eco_Result_Round()
    {
        Assert.Equal("C01", ChessBaseFields.Eco((201 + 1) * 128));
        Assert.Equal("A00", ChessBaseFields.Eco(128));
        Assert.Null(ChessBaseFields.Eco(0));
        Assert.Null(ChessBaseFields.Eco(64576 + 518));                       // Chess960-Startnummer, kein ECO
        Assert.Equal(new[] { "0-1", "1/2-1/2", "1-0", "*", "0-1", "1/2-1/2", "1-0", "*" }, Enumerable.Range(0, 8).Select(ChessBaseFields.Result));
        Assert.Equal("?", ChessBaseFields.Round(0, 0));
        Assert.Equal("5.2", ChessBaseFields.Round(5, 2));
    }

    // ── Klassisch: eine Partie aus Hand kodierten Bytes ─────────────────────────────────────────

    /// <summary>1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Bxc6 dxc6 5.O-O Bg4 6.h3 Bh5 7.Re1 — mit Schlagen (die Liste der Läufer
    /// schrumpft), Rochade (der Turm wandert in seiner Liste) und einem Zug über den Rand (Re1 = „sieben nach rechts").</summary>
    private static readonly int[] V1Codes = { 128, 128, 105, 101, 84, 111, 81, 125, 9, 77, 139, 67, 66, 255 };
    private static readonly string[] Expected = { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Bxc6", "dxc6", "O-O", "Bg4", "h3", "Bh5", "Re1" };

    private static IEnumerable<(string, byte[])> ClassicDatabase(int[] codes)
    {
        var inverse = new byte[256];
        for (var i = 0; i < 256; i++) inverse[CbhTables.Mode0[i]] = (byte)i;
        var moves = new List<byte>();
        var n = 0;
        foreach (var c in codes)
        {
            moves.Add((byte)(inverse[c] + n));                               // Modus 0: wert = tabelle[(byte − n) mod 256]
            if (c is not (254 or 255 or 236)) n++;
        }
        var cbg = new List<byte>(new byte[26]) { 0x00 };
        cbg[1] = 26;
        var size = 4 + moves.Count;
        cbg.AddRange(new[] { (byte)(size >> 16), (byte)(size >> 8), (byte)size });
        cbg.AddRange(moves);

        var cbh = new byte[46 * 2];
        var r = 46;
        cbh[r] = 0x01;
        cbh[r + 4] = 26;                                                      // Züge ab Byte 26 der .cbg
        cbh[r + 0x0e] = 1;                                                    // Schwarz = Spieler 1
        var date = (2024 << 9) | (5 << 5) | 12;
        cbh[r + 0x18] = (byte)(date >> 16); cbh[r + 0x19] = (byte)(date >> 8); cbh[r + 0x1a] = (byte)date;
        cbh[r + 0x1b] = 2;                                                    // 1-0
        cbh[r + 0x1d] = 3;
        cbh[r + 0x1f] = 0x07; cbh[r + 0x20] = 0x08;                           // 1800
        return new[]
        {
            ("Test.cbh", cbh), ("Test.cbg", cbg.ToArray()),
            ("Test.cbp", Entities(58, ("Muster", 0, 30), ("Max", 30, 20), ("Beispiel", 0, 30), ("Berta", 30, 20))),
            ("Test.cbt", Entities(90, ("TMM Gebietsklasse 2024/25", 0, 40), ("Schwaz", 40, 30))),
            ("Test.ini", Encoding.ASCII.GetBytes("[DescrCBG]")),
        };
    }

    /// <summary>Eine klassische Entitätsdatei: 28 Byte Kopf, je Satz 9 Byte Baumknoten + Daten. Je Eintrag zwei Felder
    /// (Nachname + Vorname bzw. Titel + Ort), in der Reihenfolge der Einträge.</summary>
    private static byte[] Entities(int dataSize, params (string Text, int Offset, int Length)[] fields)
    {
        const int perRecord = 2;
        var records = fields.Length / perRecord;
        var b = new byte[28 + records * (9 + dataSize)];
        BitConverter.GetBytes(records).CopyTo(b, 0);
        BitConverter.GetBytes(1234567890).CopyTo(b, 8);
        BitConverter.GetBytes(dataSize).CopyTo(b, 0x0c);
        BitConverter.GetBytes(-1).CopyTo(b, 0x10);
        BitConverter.GetBytes(records).CopyTo(b, 0x14);
        for (var i = 0; i < fields.Length; i++)
        {
            var rec = 28 + i / perRecord * (9 + dataSize) + 9;
            Encoding.Latin1.GetBytes(fields[i].Text).CopyTo(b, rec + fields[i].Offset);
        }
        return b;
    }

    [Fact]
    public void Classic_DecodesTheMainline_WithCapturesCastlingAndWraparound()
    {
        var result = ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(ClassicDatabase(V1Codes)), 100);
        var game = Assert.Single(result.Games);
        Assert.Null(game.Error);
        Assert.Equal(Expected, game.Moves);
        Assert.Equal(("Muster, Max", "Beispiel, Berta"), (game.White, game.Black));
        Assert.Equal(("TMM Gebietsklasse 2024/25", "Schwaz", "2024.05.12", "3", "1-0", 1800),
            (game.Event, game.Site, game.Date, game.Round, game.Result, game.WhiteElo));
        Assert.Contains("[White \"Muster, Max\"]", result.Pgn);
        Assert.Contains("1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Bxc6 dxc6 5. O-O Bg4 6. h3 Bh5 7. Re1 1-0", result.Pgn);
    }

    [Fact]
    public void Classic_StopsAtTheFirstEndOfLine_VariationsFollowLater()
    {
        // 1.e4 (254 vor der Hauptfortsetzung) e5 255 — danach die Variante 1…c5 255: die Hauptvariante ist e4 e5.
        var result = ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(ClassicDatabase(new[] { 128, 254, 128, 255, 119, 255 })), 100);
        Assert.Equal(new[] { "e4", "e5" }, Assert.Single(result.Games).Moves);
    }

    [Fact]
    public void Classic_AnImpossibleCode_SkipsTheGameWithAReason()
    {
        var result = ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(ClassicDatabase(new[] { 1, 255 })), 100);
        var game = Assert.Single(result.Games);                              // ein Königszug im ersten Zug: überall steht eine Figur
        Assert.NotNull(game.Error);
        Assert.Equal(0, result.Converted);
        Assert.DoesNotContain("[Event", result.Pgn);
    }

    /// <summary>Nf3 Nf6 Ng1 Ng8 — vier legale Pendelzüge, beliebig oft wiederholbar (der zweite Springer jeder Seite).</summary>
    private static int[] Shuffle(int cycles) =>
        Enumerable.Repeat(new[] { 105, 108, 109, 104 }, cycles).SelectMany(c => c).Append(255).ToArray();

    [Fact]
    public void Classic_AnOverlongMainline_IsSkippedWithAReason()
    {
        // D1-001: ein Zugsatz darf bis 16 MB lang sein — ohne Deckel spielte der Leser Millionen legaler Pendelzüge nach.
        var atCap = Assert.Single(ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(ClassicDatabase(Shuffle(MainlineBoard.MaxPlies / 4))), 100).Games);
        Assert.Null(atCap.Error);
        Assert.Equal(MainlineBoard.MaxPlies, atCap.Moves.Count);
        Assert.Equal(new[] { "Nf3", "Nf6", "Ng1", "Ng8" }, atCap.Moves.Take(4));

        var result = ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(ClassicDatabase(Shuffle(MainlineBoard.MaxPlies / 4 + 1))), 100);
        var game = Assert.Single(result.Games);
        Assert.Contains("Halbzüge", game.Error);
        Assert.Empty(game.Moves);
        Assert.Equal(0, result.Converted);
    }

    [Fact]
    public void Convert_ACancelledToken_StopsTheRead()
    {
        Assert.Throws<OperationCanceledException>(() =>
            ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(ClassicDatabase(V1Codes)), 100, new CancellationToken(true)));
    }

    /// <summary>Eine gebaute Datenbank: <see cref="ChessBaseImportService.MaxGames"/> Kopfsätze, die ALLE auf denselben Zugsatz
    /// zeigen, und der besteht aus einer Million Überspring-Codes (254) — kein einziger Zug, aber je Partie eine Million
    /// Schritte. Ungebremst rechnete das weit über zehn Sekunden.</summary>
    private static List<IFormFile> SharedSkipStreamDatabase()
    {
        var files = ClassicDatabase(Enumerable.Repeat(254, 1_000_000).Append(255).ToArray()).ToList();
        var record = files[0].Item2[46..92];
        var cbh = files[0].Item2.Concat(Enumerable.Repeat(record, ChessBaseImportService.MaxGames - 1).SelectMany(r => r)).ToArray();
        files[0] = ("Test.cbh", cbh);
        return files.Select(f => Form(f.Item1, f.Item2)).ToList();
    }

    // Beide Upload-Tests ohne Stoppuhr: Budget und Client-Abbruch laufen über Timer, deren Rückruf der Thread-Pool
    // ausführt — auf einem vollen CI-Runner Sekunden zu spät (gemessen 5,06 s bzw. 7,5/8,4 s bei 100 ms). Was zählt, ist
    // der AUSGANG: ungebremst rechnet diese Datenbank weit über eine Minute und endet mit einem Ergebnis.

    [Fact]
    public async Task Upload_ABuiltDatabase_StopsAtTheBudget()
    {
        var files = SharedSkipStreamDatabase();
        var service = new ChessBaseImportService(NullLogger<ChessBaseImportService>.Instance) { Budget = TimeSpan.FromMilliseconds(50) };
        var (r, reason, _) = await service.ConvertAsync(files, default);
        Assert.Null(r);                                                       // kein Ergebnis: das Budget hat abgebrochen
        Assert.Equal("tooLarge", reason);
    }

    [Fact]
    public async Task Upload_ClientGone_StopsTheConversion()
    {
        // Bricht der Client ab, endet auch die Rechnung — vorher lief Convert ohne Token bis zum Schluss weiter. Ohne Budget
        // kann NUR der Client-Token die Rechnung beenden: käme er nicht an, liefe sie durch und gäbe ein Ergebnis statt der
        // Ausnahme (mit dem 15-s-Budget dagegen endete auch ein überhörter Token als Ausnahme — der Client ist ja weg). Die
        // Dateien entstehen VOR dem Token: ein schon abgelaufener Token scheiterte bereits beim Einlesen und bewiese nichts.
        var files = SharedSkipStreamDatabase();
        var service = new ChessBaseImportService(NullLogger<ChessBaseImportService>.Instance) { Budget = Timeout.InfiniteTimeSpan };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConvertAsync(files, cts.Token));
    }

    // ── ChessBase 2: Zugwörter ──────────────────────────────────────────────────────────────────

    private static ushort Word(int from, int to, bool white)
    {
        for (var w = 1; w < Cb2MoveTable.Count; w++)
            if (Cb2MoveTable.Decode((ushort)w) is { } m && m.White == white && m.From == from && m.To == to && m.Promotion == 0 && m.Castle == 0)
                return (ushort)w;
        throw new InvalidOperationException("kein Wort");
    }

    private static int Sq(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

    private static IEnumerable<(string, byte[])> Cb2Database()
    {
        // Hauptvariante 1.e4 e5 2.Nf3, Variante 1…c5 — sie steht erst NACH dem ersten ffff.
        var words = new List<ushort>
        {
            0xfffc, Word(Sq("e2"), Sq("e4"), true), Word(Sq("e7"), Sq("e5"), false), 0xfffd,
            Word(Sq("g1"), Sq("f3"), true), 0xffff, Word(Sq("c7"), Sq("c5"), false), 0xffff,
        };
        var content = words.SelectMany(BitConverter.GetBytes).ToArray();
        var cbg = new List<byte>(new byte[12]);
        cbg[8] = 12;
        cbg[11] = 5;
        cbg.AddRange(new byte[] { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 });
        cbg.AddRange(BitConverter.GetBytes(content.Length));
        cbg.AddRange(BitConverter.GetBytes(0));
        cbg.AddRange(new byte[8]);
        cbg.AddRange(new byte[] { 0x01, 0x00 });
        cbg.AddRange(content);
        cbg.AddRange(BitConverter.GetBytes((long)content.Length + 34));

        var cbh = new byte[192 * 2];
        var r = 192;
        cbh[r] = 0x01; cbh[r + 2] = 1; cbh[r + 3] = 1;
        BitConverter.GetBytes(12L).CopyTo(cbh, r + 0x08);
        BitConverter.GetBytes(0L).CopyTo(cbh, r + 0x18);
        BitConverter.GetBytes(1L).CopyTo(cbh, r + 0x20);
        BitConverter.GetBytes(0L).CopyTo(cbh, r + 0x28);
        BitConverter.GetBytes(-1L).CopyTo(cbh, r + 0x30);
        cbh[r + 0x58] = 1;                                                    // remis
        BitConverter.GetBytes((short)4).CopyTo(cbh, r + 0x5a);
        BitConverter.GetBytes((short)2100).CopyTo(cbh, r + 0x60);
        BitConverter.GetBytes((ushort)((201 + 1) * 128)).CopyTo(cbh, r + 0x80);
        BitConverter.GetBytes((2026 << 9) | (4 << 5) | 11).CopyTo(cbh, r + 0xbc);

        return new[] { ("Spiele.2cbh", cbh), ("Spiele.2cbg", cbg.ToArray()), ("Spiele.2lid", Lid()) };
    }

    /// <summary>Eine <c>.2lid</c>: Kopf big-endian, Blöcke mit je einem Container je Art (Spieler 1024, Turnier 1120 …).</summary>
    private static byte[] Lid()
    {
        int[] containers = { 1024, 1120, 220, 1024, 314, 532 };
        long[] counts = { 2, 1, 0, 0, 0, 0 };
        var block = containers.Sum();
        var b = new byte[184 + 2 * block];
        void Be32(int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        Be32(0, 184);
        Be32(4, 6);
        for (var i = 0; i < 6; i++)
        {
            Be32(8 + 20 * i, containers[i]);
            Be32(0x0c + 20 * i + 4, (int)counts[i]);
            Be32(0x14 + 20 * i, -1); Be32(0x14 + 20 * i + 4, -1);
        }
        byte[] Str(string s) { var u = Encoding.UTF8.GetBytes(s); return BitConverter.GetBytes(u.Length).Concat(u).ToArray(); }
        byte[] Player(string last, string first, long fide)
        {
            var body = Str(last).Concat(Str(first)).Concat(BitConverter.GetBytes(0)).Concat(BitConverter.GetBytes(0))
                .Concat(BitConverter.GetBytes(-1)).Concat(BitConverter.GetBytes(8)).Concat(BitConverter.GetBytes(fide)).ToArray();
            return BitConverter.GetBytes(body.Length).Concat(body).ToArray();
        }
        Player("Müßig", "Jürgen", 1600123).CopyTo(b, 184);
        Player("Probe", "Paula", 0).CopyTo(b, 184 + block);
        var tour = Str("Innsbruck").Concat(Str("Landesliga 2025/26")).Concat(new byte[108]).ToArray();
        BitConverter.GetBytes(tour.Length).Concat(tour).ToArray().CopyTo(b, 184 + 1024);
        return b;
    }

    [Fact]
    public void Cb2_DecodesTheMainline_NamesInUtf8_FideIdsFromThePlayer()
    {
        var result = ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(Cb2Database()), 100);
        var game = Assert.Single(result.Games);
        Assert.Null(game.Error);
        Assert.Equal(new[] { "e4", "e5", "Nf3" }, game.Moves);                // die Variante 1…c5 kommt nicht mit
        Assert.Equal(("Müßig, Jürgen", "Probe, Paula", "1600123", null),
            (game.White, game.Black, game.WhiteFideId, game.BlackFideId));
        Assert.Equal(("Landesliga 2025/26", "Innsbruck", "2026.04.11", "4", "1/2-1/2", "C01", 2100),
            (game.Event, game.Site, game.Date, game.Round, game.Result, game.Eco, game.WhiteElo));
        Assert.Null(game.Annotator);
        Assert.Contains("[WhiteFideId \"1600123\"]", result.Pgn);
    }

    // ── Dateien ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Files_TheHeaderAloneIsNotEnough()
    {
        var e = Assert.Throws<ChessBaseFormatException>(() =>
            ChessBaseFiles.FromFiles(ClassicDatabase(V1Codes).Where(f => !f.Item1.EndsWith(".cbg"))));
        Assert.Equal("missingFile", e.Reason);
        Assert.Contains("Test.cbg", e.Message);
    }

    [Fact]
    public void Files_TwoDatabasesAtOnce_AreRefused_UnknownFilesIgnored()
    {
        var both = ClassicDatabase(V1Codes).Concat(Cb2Database()).Append(("notes.txt", new byte[] { 1 }));
        Assert.Equal("multipleDatabases", Assert.Throws<ChessBaseFormatException>(() => ChessBaseFiles.FromFiles(both)).Reason);
        Assert.Equal("noDatabase", Assert.Throws<ChessBaseFormatException>(() =>
            ChessBaseFiles.FromFiles(new[] { ("partien.pgn", new byte[] { 1 }) })).Reason);
    }

    [Fact]
    public void Files_FromAZip_WithFolders()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in Cb2Database())
            {
                using var s = zip.CreateEntry("Datenbanken/" + name).Open();
                s.Write(data);
            }
        ms.Position = 0;
        var files = ChessBaseFiles.FromZip(ms);
        Assert.Equal((ChessBaseFormat.Cb2, "Spiele"), (files.Format, files.Name));
        Assert.Single(ChessBaseConverter.Convert(files, 100).Games);
        Assert.Equal("invalidZip", Assert.Throws<ChessBaseFormatException>(() =>
            ChessBaseFiles.FromZip(new MemoryStream(new byte[] { 1, 2, 3 }))).Reason);
    }

    [Fact]
    public void Convert_StopsAtTheCap()
    {
        var files = ClassicDatabase(V1Codes).ToList();
        var cbh = files[0].Item2.Concat(files[0].Item2[46..92]).ToArray();   // dieselbe Partie ein zweites Mal
        files[0] = ("Test.cbh", cbh);
        var result = ChessBaseConverter.Convert(ChessBaseFiles.FromFiles(files), 1);
        Assert.True(result.Truncated);
        Assert.Single(result.Games);
    }

    // ── Upload (Dienst) ─────────────────────────────────────────────────────────────────────────

    private static IFormFile Form(string name, byte[] data) => new FormFile(new MemoryStream(data), 0, data.Length, "files", name);

    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(data);
        return ms.ToArray();
    }

    [Fact]
    public async Task Upload_GzippedFiles_BecomePgn_UnneededOnesIgnored()
    {
        var files = ClassicDatabase(V1Codes).Select(f => f.Item1.EndsWith(".ini") ? Form(f.Item1, f.Item2) : Form(f.Item1 + ".gz", Gzip(f.Item2))).ToList();
        var (r, reason, _) = await new ChessBaseImportService(NullLogger<ChessBaseImportService>.Instance).ConvertAsync(files, default);
        Assert.Null(reason);
        Assert.Equal(("cbh", "Test", 1, 1, false), (r!.Format, r.Name, r.Games, r.Converted, r.Truncated));
        Assert.Contains("7. Re1 1-0", r.Pgn);
        Assert.Empty(r.Skipped);
    }

    [Fact]
    public async Task Upload_Refusals_CarryTheReason()
    {
        var service = new ChessBaseImportService(NullLogger<ChessBaseImportService>.Instance);
        Assert.Equal("noFile", (await service.ConvertAsync(Array.Empty<IFormFile>(), default)).Reason);
        Assert.Equal("missingFile", (await service.ConvertAsync(
            ClassicDatabase(V1Codes).Where(f => !f.Item1.EndsWith(".cbp")).Select(f => Form(f.Item1, f.Item2)).ToList(), default)).Reason);
        Assert.Equal("invalidFile", (await service.ConvertAsync(new[] { Form("Test.cbh.gz", new byte[] { 1, 2, 3, 4 }) }, default)).Reason);
        var big = new byte[ChessBaseImportService.MaxBodyBytes / 2 + 1];
        Assert.Equal("tooLarge", (await service.ConvertAsync(new[] { Form("a.cbg", big), Form("a.cbh", big) }, default)).Reason);
    }

    [Fact]
    public async Task Upload_SkippedGames_AreListedWithTheirNumber()
    {
        var (r, _, _) = await new ChessBaseImportService(NullLogger<ChessBaseImportService>.Instance)
            .ConvertAsync(ClassicDatabase(new[] { 1, 255 }).Select(f => Form(f.Item1, f.Item2)).ToList(), default);
        var skip = Assert.Single(r!.Skipped);
        Assert.Equal((1, "Muster, Max", "Beispiel, Berta"), (skip.Id, skip.White, skip.Black));
        Assert.Equal((1, 0, 1), (r.Games, r.Converted, r.SkippedCount));
    }

    [Fact]
    public void Files_AGzipBomb_IsStoppedWhileReading()
    {
        var bomb = Gzip(new byte[ChessBaseFiles.MaxTotalBytes + 1]);
        Assert.True(bomb.Length < 1024 * 1024);
        Assert.Equal("tooLarge", Assert.Throws<ChessBaseFormatException>(() =>
            ChessBaseFiles.FromUploads(new[] { ("x.cbg.gz", bomb), ("x.cbh", new byte[92]) })).Reason);
    }
}
