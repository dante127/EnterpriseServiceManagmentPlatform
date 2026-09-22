using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Tickets;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");

        builder.HasKey(ticket => ticket.Id);

        builder.Property(ticket => ticket.TicketNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(ticket => ticket.TicketNumber)
            .IsUnique();

        builder.Property(ticket => ticket.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.HasIndex(ticket => ticket.Title);

        // Description is free text: nvarchar(max) keeps long incidents supported.
        builder.Property(ticket => ticket.Description)
            .HasMaxLength(8000)
            .IsRequired();

        // Rowversion drives optimistic concurrency: conflicting saves surface as
        // DbUpdateConcurrencyException and are reported as HTTP 409.
        builder.Property(ticket => ticket.RowVersion)
            .IsRowVersion();

        // A repeated create with the same idempotency key must not duplicate a ticket.
        builder.HasIndex(ticket => ticket.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        // Query-pattern indexes: ticket search filters and sorts on these columns.
        builder.HasIndex(ticket => ticket.Status);
        builder.HasIndex(ticket => ticket.SlaStatus);
        builder.HasIndex(ticket => ticket.ReportedAt);
        builder.HasIndex(ticket => ticket.ResolutionDueAt);
        builder.HasIndex(ticket => ticket.PriorityId);
        builder.HasIndex(ticket => ticket.ServiceId);
        builder.HasIndex(ticket => ticket.CategoryId);
        builder.HasIndex(ticket => ticket.RequesterId);
        builder.HasIndex(ticket => ticket.AssignedToId);
        builder.HasIndex(ticket => ticket.AssignedTeamId);

        builder.HasOne(ticket => ticket.Service)
            .WithMany(service => service.Tickets)
            .HasForeignKey(ticket => ticket.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ticket => ticket.Category)
            .WithMany()
            .HasForeignKey(ticket => ticket.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ticket => ticket.Priority)
            .WithMany()
            .HasForeignKey(ticket => ticket.PriorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ticket => ticket.Requester)
            .WithMany()
            .HasForeignKey(ticket => ticket.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ticket => ticket.AssignedTo)
            .WithMany()
            .HasForeignKey(ticket => ticket.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ticket => ticket.AssignedTeam)
            .WithMany()
            .HasForeignKey(ticket => ticket.AssignedTeamId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(ticket => ticket.Comments)
            .WithOne(comment => comment.Ticket)
            .HasForeignKey(comment => comment.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ticket => ticket.AssignmentHistory)
            .WithOne(history => history.Ticket)
            .HasForeignKey(history => history.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // Soft-deleted tickets are invisible to every query.
        builder.HasQueryFilter(ticket => !ticket.IsDeleted);

        builder.Navigation(ticket => ticket.Comments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(ticket => ticket.AssignmentHistory)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
