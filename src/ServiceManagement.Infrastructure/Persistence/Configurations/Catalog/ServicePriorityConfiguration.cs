using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ServicePriorityConfiguration : IEntityTypeConfiguration<ServicePriority>
{
    public void Configure(EntityTypeBuilder<ServicePriority> builder)
    {
        builder.ToTable("ServicePriorities");

        builder.HasKey(priority => priority.Id);

        builder.Property(priority => priority.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(priority => priority.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(priority => priority.Code)
            .IsUnique();

        builder.Property(priority => priority.Level);

        builder.HasIndex(priority => priority.Level);

        builder.Property(priority => priority.ColorHex)
            .HasMaxLength(9);

        builder.HasMany(priority => priority.SlaPolicies)
            .WithOne(policy => policy.Priority)
            .HasForeignKey(policy => policy.PriorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(priority => priority.SlaPolicies)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
