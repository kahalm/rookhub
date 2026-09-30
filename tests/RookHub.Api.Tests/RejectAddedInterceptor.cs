using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RookHub.Api.Tests;

/// <summary>
/// Laesst jedes SaveChanges scheitern wie ein Unique-Index-Treffer, solange ein passender
/// <c>Added</c>-Eintrag im Change-Tracker steht. Die InMemory-Datenbank kennt keine
/// Unique-Indizes; so laesst sich nachstellen, was MariaDB mit einem „Duplicate entry" tut —
/// und dass ein nicht geleerter Tracker den Fehler an jedes weitere SaveChanges weiterreicht.
/// </summary>
public sealed class RejectAddedInterceptor(Func<object, bool> poisoned) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfPoisoned(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ThrowIfPoisoned(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ThrowIfPoisoned(DbContextEventData eventData)
    {
        if (eventData.Context!.ChangeTracker.Entries()
            .Any(e => e.State == EntityState.Added && poisoned(e.Entity)))
        {
            throw new DbUpdateException("An error occurred while saving the entity changes.",
                new InvalidOperationException("Duplicate entry 'poison' for key 'PublicId'"));
        }
    }
}
