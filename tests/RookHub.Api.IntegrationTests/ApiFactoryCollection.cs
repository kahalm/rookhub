using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// ALLE Testklassen, die eine <see cref="ApiFactory"/> hochfahren, MUESSEN in dieser Sammlung
/// liegen — xUnit fuehrt Klassen derselben Sammlung nacheinander aus.
///
/// <para>Grund: <c>WebApplicationFactory</c> faehrt die Anwendung ueber ihren Einstiegspunkt hoch
/// und benutzt dafuer prozessweiten statischen Zustand (<c>HostFactoryResolver</c>). Zwei
/// Instanzen gleichzeitig vertragen sich nicht; die zweite scheitert mit
/// „The entry point exited without ever building an IHost" — und zwar nur im Gesamtlauf, in
/// Isolation ist derselbe Test gruen. Wer die naechste Integrationstest-Klasse mit ApiFactory
/// schreibt und das Attribut vergisst, sucht den Fehler lange.</para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiFactoryCollection
{
    public const string Name = "ApiFactory";
}
