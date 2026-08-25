using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlokeBot.Core.Tests;

internal sealed class FailDurableAlertSaveInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    ) =>
        eventData
            .Context?.ChangeTracker.Entries<DurableAlert>()
            .Any(entry => entry.State == EntityState.Added) == true
            ? ValueTask.FromException<InterceptionResult<int>>(
                new InvalidOperationException("Simulated durable alert save failure.")
            )
            : ValueTask.FromResult(result);
}
