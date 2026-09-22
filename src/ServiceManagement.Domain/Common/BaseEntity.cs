namespace ServiceManagement.Domain.Common;

/// <summary>
/// Base type for every persisted entity.
/// Identifiers are time-ordered UUID v7 values so the clustered SQL Server
/// indexes remain fragmentation-friendly without falling back to IDENTITY.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
