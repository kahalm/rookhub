using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.DTOs;

namespace RookHub.Api.Tests;

/// <summary>N7-002: Das FEN-Feld von position-lookup / position-tree / similar-positions ist gedeckelt.
/// Ohne Deckel lief <c>NormalizeKey</c> (<c>fen.Split(' ')</c>) bzw. <c>PositionSimilarity.Extract</c>
/// über einen 15-MB-Rumpf. [ApiController] weist zu lange FENs jetzt per Modellvalidierung mit 400 ab,
/// bevor der Dienst läuft; echte FENs (auch die längste denkbare) bleiben gültig.</summary>
public class PositionFenLengthCapTests
{
    // Längste „echte" FEN: acht volle Reihen ohne Leerfelder, alle Rochaderechte, ep-Feld, große Zähler.
    private const string LongLegalFen = "rnbqkbnr/pppppppp/PPPPPPPP/RNBQKBNR/rnbqkbnr/pppppppp/PPPPPPPP/RNBQKBNR w KQkq e3 100 1000";
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static bool DataAnnotationsValid(object dto)
        => Validator.TryValidateObject(dto, new ValidationContext(dto), new List<ValidationResult>(), validateAllProperties: true);

    /// <summary>Der Weg, den [ApiController] nimmt (MVC-Metadaten).</summary>
    private static bool MvcValid(object dto)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var sp = services.BuildServiceProvider();
        var ctx = new ActionContext(new DefaultHttpContext { RequestServices = sp }, new RouteData(), new ActionDescriptor());
        sp.GetRequiredService<IObjectModelValidator>().Validate(ctx, validationState: null, prefix: string.Empty, dto);
        return ctx.ModelState.IsValid;
    }

    public static IEnumerable<object[]> Dtos(string fen) => new[]
    {
        new object[] { new PositionLookupRequestDto { Fen = fen } },
        new object[] { new PositionTreeRequestDto { Fen = fen } },
        new object[] { new SimilarPositionsRequestDto { Fen = fen } },
    };

    public static IEnumerable<object[]> HugeFen() => Dtos(new string(' ', 1_000_000));
    public static IEnumerable<object[]> JustTooLong() => Dtos(new string('x', PositionLookupRequestDto.MaxFenLength + 1));
    public static IEnumerable<object[]> RealFens() => Dtos(StartFen).Concat(Dtos(LongLegalFen));

    [Theory]
    [MemberData(nameof(HugeFen))]
    [MemberData(nameof(JustTooLong))]
    public void OverlongFen_IsRejected(object dto)
    {
        Assert.False(DataAnnotationsValid(dto));
        Assert.False(MvcValid(dto));
    }

    [Theory]
    [MemberData(nameof(RealFens))]
    public void RealFen_StaysValid(object dto)
    {
        Assert.True(LongLegalFen.Length <= PositionLookupRequestDto.MaxFenLength);
        Assert.True(DataAnnotationsValid(dto));
        Assert.True(MvcValid(dto));
    }
}
