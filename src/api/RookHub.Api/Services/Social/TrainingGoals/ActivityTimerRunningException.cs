using RookHub.Api.DTOs.Social;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>Start ohne <c>replace</c>, obwohl schon ein Timer läuft (F5-008) → 409 mit dem laufenden Timer im Rumpf, damit
/// der Client ihn anzeigt und erst nach ausdrücklicher Rückfrage ersetzt.</summary>
public sealed class ActivityTimerRunningException : ConflictException
{
    public ActivityTimerRunningException(ActivityTimerDto running) : base("A timer is already running.") => Running = running;

    /// <summary>Der laufende Timer, wie <c>GET …/timer</c> ihn liefert.</summary>
    public ActivityTimerDto Running { get; }
}
