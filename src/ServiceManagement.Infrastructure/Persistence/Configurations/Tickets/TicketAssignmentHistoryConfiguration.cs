using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Tickets;

public sealed class TicketAssignmentHistoryConfiguration : IEntityTypeConfiguration<TicketAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<TicketAssignmentHistory> builder)
    {
        builder.ToTable("TicketAssignmentHistory");

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Reason)
            .HasMaxLength(500);

        // Append-only trail: ordered reads by ticket are the only access pattern.
        builder.HasIndex(history => new { history.TicketId, history.ChangedAt });

        builder.HasIndex(history => history.ChangedBy);

        // Matching filter so the trail follows its ticket's visibility exactly.
        builder.HasQueryFilter(history => !history.Ticket.IsDeleted);
    }
}
