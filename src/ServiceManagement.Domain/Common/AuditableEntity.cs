namespace ServiceManagement.Domain.Common;

/// <summary>
/// Entity that carries creation/modification metadata. The values are populated
/// centrally by the persistence layer (auditing interceptor), never by hand.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
