using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Konto-Schluessel <c>Anthropic:ApiKey</c> gehoert seit dem 2026-09-25 allein dem Formular-Einlesen
/// („ausser Scoresheet soll nichts ueber den Key laufen"). Der Text-Client (Tipps, Uebersetzung) darf ihn
/// deshalb NICHT einschalten — er hat seinen eigenen Schluessel <c>Anthropic:TextApiKey</c>.
/// </summary>
public class ClaudeJsonClientTests
{
    private static ClaudeJsonClient Build(params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();
        return new ClaudeJsonClient(config, NullLogger<ClaudeJsonClient>.Instance);
    }

    [Fact]
    public void KontoSchluesselAllein_schaltetTippsUndUebersetzungNICHTein()
    {
        var client = Build(("Anthropic:ApiKey", "sk-ant-konto"));
        Assert.False(client.IsConfigured);
    }

    [Fact]
    public void EigenerTextSchluessel_schaltetEin_undDasUebersetzungsmodellBleibtEinEigenerSchalter()
    {
        var client = Build(("Anthropic:TextApiKey", "sk-ant-text"));
        Assert.True(client.IsConfigured);
        Assert.Equal("claude-sonnet-5", client.TranslationModel);

        var other = Build(("Anthropic:TextApiKey", "sk-ant-text"), ("Anthropic:TranslationModel", "claude-haiku-4-5-20251001"));
        Assert.Equal("claude-haiku-4-5-20251001", other.TranslationModel);
    }

    [Fact]
    public void OhneJedenSchluessel_inaktiv()
    {
        Assert.False(Build().IsConfigured);
        Assert.False(Build(("Anthropic:TextApiKey", "   ")).IsConfigured);
    }
}
