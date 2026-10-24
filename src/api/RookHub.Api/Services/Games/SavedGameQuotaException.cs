namespace RookHub.Api.Services.Games;

/// <summary>Belegung eines Kontos an gespeicherten Partien (<see cref="SavedGameService.UsageQuery"/>).</summary>
internal sealed record SavedGameUsage(int Games, long Chars);

/// <summary>Das Konto ist am Deckel für gespeicherte Partien (<see cref="SavedGameService.MaxGamesPerUser"/>,
/// <see cref="SavedGameService.MaxPgnCharsPerUser"/>, A6-007). Eine <see cref="ArgumentException"/>, damit die
/// bestehenden 400-Zweige (<c>{ message }</c>) greifen; wer den Grund nennen will, fängt sie davor.</summary>
public sealed class SavedGameQuotaException(string message) : ArgumentException(message);
