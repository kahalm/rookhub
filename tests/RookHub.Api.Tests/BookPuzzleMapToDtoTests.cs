using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Deckt die Buch-Titel-Zuordnung in <see cref="BookPuzzleService.MapToDto"/> ab
/// (BookTitle = Book.DisplayName, leer → null). Schützt vor dem 0.181.3-Regress
/// (referenzierte ein nicht existentes Book.Title → CI-Build-Fehler).</summary>
public class BookPuzzleMapToDtoTests
{
    private static BookPuzzle MakePuzzle(Book? book) => new()
    {
        Id = 1, LineId = "L1", BookFileName = "chessable-u5-x.pgn",
        Fen = "8/8/8/8/8/8/8/8 w - - 0 1", Moves = "e2e4", Book = book
    };

    [Fact]
    public void MapToDto_UsesBookDisplayNameAsTitle()
    {
        var book = new Book { Id = 7, FileName = "chessable-u5-x.pgn", DisplayName = "Mein Lieblingskurs" };
        var dto = BookPuzzleService.MapToDto(MakePuzzle(book));
        Assert.Equal("Mein Lieblingskurs", dto.BookTitle);
    }

    [Fact]
    public void MapToDto_EmptyDisplayName_YieldsNullTitle()
    {
        var book = new Book { Id = 7, FileName = "chessable-u5-x.pgn", DisplayName = "" };
        var dto = BookPuzzleService.MapToDto(MakePuzzle(book));
        Assert.Null(dto.BookTitle);
    }

    [Fact]
    public void MapToDto_NoBook_YieldsNullTitle()
    {
        var dto = BookPuzzleService.MapToDto(MakePuzzle(null));
        Assert.Null(dto.BookTitle);
    }
}
