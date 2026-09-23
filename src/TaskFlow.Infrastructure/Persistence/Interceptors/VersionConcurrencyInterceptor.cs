using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence.Interceptors;

public sealed class VersionConcurrencyInterceptor : SaveChangesInterceptor
{
    public static VersionConcurrencyInterceptor Instance { get; } = new();

    private VersionConcurrencyInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        IncrementVersions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        IncrementVersions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void IncrementVersions(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        IncrementModified<Project>(context.ChangeTracker);
        IncrementModified<TaskItem>(context.ChangeTracker);
        IncrementModified<Tag>(context.ChangeTracker);
    }

    private static void IncrementModified<TEntity>(ChangeTracker changeTracker)
        where TEntity : class
    {
        foreach (EntityEntry<TEntity> entry in changeTracker.Entries<TEntity>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            PropertyEntry version = entry.Property("Version");
            long originalVersion = (long)version.OriginalValue!;
            version.CurrentValue = checked(originalVersion + 1);
        }
    }
}
