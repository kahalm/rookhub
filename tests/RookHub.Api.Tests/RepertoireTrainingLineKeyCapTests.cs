using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.DTOs;

namespace RookHub.Api.Tests;

/// <summary>A7-012 / N7-005: Die Linien-Schlüssel-Listen des Repertoire-Trainers (promote, pause,
/// make-due) sind gedeckelt — Anzahl je Aufruf und Länge je Schlüssel. [ApiController] lehnt zu große
/// Listen per Modellvalidierung mit 400 ab, bevor der Dienst ein SQL-IN baut oder Zeilen anlegt.</summary>
public class RepertoireTrainingLineKeyCapTests
{
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

    private static void AssertValid(object dto, bool expected)
    {
        Assert.Equal(expected, DataAnnotationsValid(dto));
        Assert.Equal(expected, MvcValid(dto));
    }

    /// <summary>Schlüssel im Format des Frontends (<c>lineKeyFromSans</c>: „l" + cyrb53 in Basis 36).</summary>
    private static List<string> Keys(int n) => Enumerable.Range(0, n).Select(i => "l" + ((long)i * 7919 + 1_000_000_000_000).ToString("x")).ToList();

    [Fact]
    public void Promote_And_Pause_AcceptWholeRealCourse()
    {
        AssertValid(new PromoteLinesRequest { LineKeys = Keys(LineKeyListAttribute.MaxKeys) }, true);
        AssertValid(new SetPausedRequest { LineKeys = Keys(LineKeyListAttribute.MaxKeys), Paused = true }, true);
    }

    [Fact]
    public void Promote_And_Pause_RejectTooManyKeys()
    {
        AssertValid(new PromoteLinesRequest { LineKeys = Keys(LineKeyListAttribute.MaxKeys + 1) }, false);
        AssertValid(new SetPausedRequest { LineKeys = Keys(LineKeyListAttribute.MaxKeys + 1), Paused = true }, false);
    }

    [Fact]
    public void Promote_And_Pause_RejectKeyLongerThanTheColumn()
    {
        var tooLong = new List<string> { "l1", new string('x', LineKeyListAttribute.MaxKeyLength + 1) };
        AssertValid(new PromoteLinesRequest { LineKeys = tooLong }, false);
        AssertValid(new SetPausedRequest { LineKeys = tooLong }, false);

        var atColumn = new List<string> { new string('x', LineKeyListAttribute.MaxKeyLength) };
        AssertValid(new PromoteLinesRequest { LineKeys = atColumn }, true);
    }

    [Fact]
    public void MakeDue_CapsKeysLikePromote()
    {
        AssertValid(new MakeDueRequest { LineKeys = Keys(LineKeyListAttribute.MaxKeys) }, true);
        AssertValid(new MakeDueRequest { LineKeys = Keys(LineKeyListAttribute.MaxKeys + 1) }, false);
        AssertValid(new MakeDueRequest { LineKeys = new List<string> { new string('x', LineKeyListAttribute.MaxKeyLength + 1) } }, false);
    }

    [Fact]
    public void MakeDue_EmptyList_StaysValid_MeansWholeCourse()
    {
        // Fehlt "lineKeys" im JSON, bleibt die vorbelegte leere Liste = ganzer Kurs.
        AssertValid(new MakeDueRequest(), true);
    }

    [Fact]
    public void BlankAndNullKeys_StayAllowed_TheServiceFiltersThem()
    {
        AssertValid(new PromoteLinesRequest { LineKeys = new List<string> { "", "  ", null!, "l1" } }, true);
    }
}
