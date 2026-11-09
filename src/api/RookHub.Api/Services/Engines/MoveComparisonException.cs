namespace RookHub.Api.Services.Engines;

/// <summary>Absage beim Anlegen eines Vergleichs — <see cref="Reason"/> ist der Code, den die Seite übersetzt.</summary>
public sealed class MoveComparisonException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;
}
