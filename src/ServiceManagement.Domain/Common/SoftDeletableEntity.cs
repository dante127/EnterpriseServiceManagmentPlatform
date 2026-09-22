namespace ServiceManagement.Domain.Common;

/// <summary>Marker for entities that are removed logically rather than physically.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Auditable entity that supports soft deletion (used by tickets).</summary>
public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
