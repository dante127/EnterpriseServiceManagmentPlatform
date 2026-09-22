using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Notifications;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Notifications;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(notification => notification.Message)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(notification => notification.EntityName)
            .HasMaxLength(200);

        builder.Property(notification => notification.EntityId)
            .HasMaxLength(128);

        builder.Property(notification => notification.CorrelationId)
            .HasMaxLength(64);

        builder.HasIndex(notification => new { notification.UserId, notification.IsRead });

        builder.HasIndex(notification => notification.CreatedAt);

        builder.HasOne(notification => notification.User)
            .WithMany()
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
