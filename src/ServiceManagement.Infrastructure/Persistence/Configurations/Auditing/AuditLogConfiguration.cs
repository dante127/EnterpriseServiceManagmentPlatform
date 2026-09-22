using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Auditing;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Auditing;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(auditLog => auditLog.Id);

        builder.Property(auditLog => auditLog.Action)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(auditLog => auditLog.EntityName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(auditLog => auditLog.EntityId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(auditLog => auditLog.IpAddress)
            .HasMaxLength(64);

        builder.Property(auditLog => auditLog.CorrelationId)
            .HasMaxLength(64);

        // Old/new values are JSON snapshots.
        builder.Property(auditLog => auditLog.OldValues);
        builder.Property(auditLog => auditLog.NewValues);

        // Deliberately NO foreign key to Users: audit rows must survive user
        // deletion and remain immutable history.
        builder.HasIndex(auditLog => auditLog.OccurredAt);
        builder.HasIndex(auditLog => new { auditLog.EntityName, auditLog.EntityId });
        builder.HasIndex(auditLog => auditLog.Action);
        builder.HasIndex(auditLog => auditLog.UserId);

        builder.ToTable(options => options.HasCheckConstraint("CK_AuditLogs_Action_NotEmpty", "[Action] <> ''"));
    }
}
