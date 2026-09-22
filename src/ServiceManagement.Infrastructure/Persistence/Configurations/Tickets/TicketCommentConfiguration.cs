using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Tickets;

public sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("TicketComments");

        builder.HasKey(comment => comment.Id);

        builder.Property(comment => comment.Content)
            .HasMaxLength(4000)
            .IsRequired();

        builder.HasIndex(comment => new { comment.TicketId, comment.CreatedAt });

        builder.HasIndex(comment => new { comment.TicketId, comment.IsInternal });

        builder.HasOne(comment => comment.Author)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Matching filter so comments follow their ticket's visibility exactly.
        builder.HasQueryFilter(comment => !comment.Ticket.IsDeleted);
    }
}
