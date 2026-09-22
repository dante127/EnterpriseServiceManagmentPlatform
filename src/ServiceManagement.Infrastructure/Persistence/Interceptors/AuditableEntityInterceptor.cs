using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Common;

namespace ServiceManagement.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit metadata and turns physical deletes of soft-deletable entities
/// into logical ones. Centralising this here is what allows the domain to keep
/// plain, dependency-free entities.
/// </summary>
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuditableEntityInterceptor(ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void ApplyAuditInformation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = now;
                    }

                    entry.Entity.CreatedBy ??= userId;
                    break;

                case EntityState.Modified:
                    if (entry.Properties.Any(property =>
                            property.IsModified &&
                            property.Metadata.Name != nameof(AuditableEntity.UpdatedAt) &&
                            property.Metadata.Name != nameof(AuditableEntity.UpdatedBy)))
                    {
                        entry.Entity.UpdatedAt = now;
                        entry.Entity.UpdatedBy = userId;
                    }

                    break;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            // Physical deletes become logical ones; the row stays for auditing.
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
        }
    }
}
