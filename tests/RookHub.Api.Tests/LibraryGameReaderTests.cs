using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Textdurchgang ueber den Rohbestand: zaehlt, was die Vorsortierung braucht, und erkennt
/// dieselbe Partie unter verschiedenen Kommentaren wieder.
/// </summary>
public class LibraryGameReaderTests
{
    [Fact]
    public void Analyse_zaehltHalbzuegeNurInDerHauptvariante()
    {
        var stats = LibraryGameReader.Analyse("1. e4 e5 2. Nf3 (2. f4 exf4 3. Bc4) 2... Nc6 1-0");

        Assert.Equal(4, stats.PlyCount);          // e4 e5 Nf3 Nc6 — die Klammer zaehlt nicht mit
        Assert.Equal(1, stats.VariationCount);
    }

    [Fact]
    public void Analyse_zaehltKommentierteHalbzuege_nichtKommentare()
    {
        // Zwei Kommentare an derselben Stelle sind EINE Stelle, an der die Partie etwas sagt.
        var stats = LibraryGameReader.Analyse("1. e4 {stark} {und beliebt} e5 2. Nf3 {Angriff} Nc6");

        Assert.Equal(3, stats.CommentCount);
        Assert.Equal(2, stats.CommentedPlies);
        Assert.Equal("stark".Length + "und beliebt".Length + "Angriff".Length, stats.CommentChars);
    }

    /// <summary>Was in einer Nebenvariante steht, bekommt der Spielende nie zu sehen — es zaehlt
    /// als geschriebener Text, aber nicht als kommentierter Halbzug der Partie.</summary>
    [Fact]
    public void Analyse_kommentarInNebenvariante_istKeinKommentierterHalbzug()
    {
        var stats = LibraryGameReader.Analyse("1. e4 e5 (1... c5 {die sizilianische Antwort}) 2. Nf3");

        Assert.Equal(1, stats.CommentCount);
        Assert.Equal(0, stats.CommentedPlies);
    }

    [Fact]
    public void Analyse_zaehltSymbolbewertungen()
    {
        var stats = LibraryGameReader.Analyse("1. e4 $1 e5 $2 2. Nf3 $14 Nc6");

        Assert.Equal(3, stats.NagCount);
        Assert.Equal(4, stats.PlyCount);
    }

    /// <summary>Der Dubletten-Griff: dieselbe Partie, zweimal kommentiert, ergibt denselben Hash.
    /// Ohne das Wegschneiden der Bewertungszeichen faende der Abgleich nichts.</summary>
    [Fact]
    public void Analyse_gleicheZuege_verschiedeneKommentare_gleicherHash()
    {
        var a = LibraryGameReader.Analyse("1. e4! {Aagaard: der beste Zug} e5 2. Nf3 Nc6 1-0");
        var b = LibraryGameReader.Analyse("1. e4 e5?! 2. Nf3!? {Ftacnik sieht das anders} Nc6 1-0");

        Assert.Equal(a.MovesHash, b.MovesHash);
        Assert.NotEmpty(a.MovesHash);
    }

    [Fact]
    public void Analyse_andereZuege_andererHash()
    {
        var a = LibraryGameReader.Analyse("1. e4 e5 2. Nf3");
        var b = LibraryGameReader.Analyse("1. d4 d5 2. c4");

        Assert.NotEqual(a.MovesHash, b.MovesHash);
    }

    /// <summary>Zugnummern ohne Leerzeichen am Zug („12.e4") kommen in aelteren Sammlungen vor.</summary>
    [Fact]
    public void Analyse_zugnummerKlebtAmZug()
    {
        var stats = LibraryGameReader.Analyse("1.e4 e5 2.Nf3 Nc6");

        Assert.Equal(4, stats.PlyCount);
        Assert.Equal(stats.MovesHash, LibraryGameReader.Analyse("1. e4 e5 2. Nf3 Nc6").MovesHash);
    }

    [Fact]
    public void Analyse_ohneZuege_gibtNichtsHer()
    {
        Assert.Equal(0, LibraryGameReader.Analyse("").PlyCount);
        Assert.Equal(0, LibraryGameReader.Analyse("1-0").PlyCount);
    }

    // ===== Die ganze Zeile ===================================================

    private static readonly Dictionary<string, string> Headers = new()
    {
        ["Event"] = "Rilton Cup 34th",
        ["Site"] = "Stockholm",
        ["Date"] = "2004.12.30",
        ["Round"] = "4",
        ["White"] = "Edlund, Robin",
        ["Black"] = "Peng, Zhaoqin",
        ["Result"] = "0-1",
        ["Annotator"] = "Aagaard,Jacob",
        ["ECO"] = "C10",
        ["WhiteElo"] = "2206",
        ["BlackElo"] = "2420",
        ["GameId"] = "1165542987534336",
        ["SourceTitle"] = "CBM 104 Extra",
        ["Source"] = "ChessBase",
    };

    [Fact]
    public void From_uebernimmtKopfdatenUndMerkmale()
    {
        const string moves = "1. e4 {ein Wort dazu} e6 2. d4 d5 0-1";

        var row = LibraryGameReader.From("[Event \"x\"]\n\n" + moves, Headers, moves, "meistergames.pgn");

        Assert.NotNull(row);
        Assert.Equal("Edlund, Robin", row!.White);
        Assert.Equal(2420, row.BlackElo);
        Assert.Equal(new DateOnly(2004, 12, 30), row.PlayedOn);
        Assert.Equal("Aagaard,Jacob", row.Annotator);
        Assert.Equal("CBM 104 Extra", row.SourceTitle);
        Assert.Equal("meistergames.pgn", row.SourceFile);
        Assert.Equal(4, row.PlyCount);
        Assert.Equal(1, row.CommentedPlies);
        // Noch niemand hat sie angesehen — und die spaeteren Spalten stehen bewusst auf null.
        Assert.Equal(LibraryGameStatus.New, row.Status);
        Assert.Null(row.Languages);
        Assert.Null(row.Score);
    }

    /// <summary>ChessBase schreibt Unbekanntes als „?" bzw. „????.??.??". Das ist kein Wert, und in
    /// der Spalte hat es nichts zu suchen — sonst sortiert die Durchsicht nach Fragezeichen.</summary>
    [Fact]
    public void From_leereChessBaseAngaben_werdenNull()
    {
        var headers = new Dictionary<string, string>
        {
            ["White"] = "?", ["Site"] = "?", ["Date"] = "????.??.??", ["Annotator"] = "",
            ["WhiteElo"] = "0",
        };

        var row = LibraryGameReader.From("x", headers, "1. e4 e5", null);

        Assert.NotNull(row);
        Assert.Null(row!.White);
        Assert.Null(row.Site);
        Assert.Null(row.PlayedOn);
        Assert.Null(row.Annotator);
        Assert.Null(row.WhiteElo);
    }

    [Fact]
    public void From_ohneZuege_gibtNichts()
        => Assert.Null(LibraryGameReader.From("x", Headers, "*", null));
}
