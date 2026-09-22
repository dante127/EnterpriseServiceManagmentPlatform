using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("Services");

        builder.HasKey(service => service.Id);

        builder.Property(service => service.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(service => service.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(service => service.Code)
            .IsUnique();

        builder.Property(service => service.Description)
            .HasMaxLength(1000);

        builder.HasIndex(service => service.CategoryId);

        builder.HasIndex(service => service.IsActive);

        builder.Navigation(service => service.Tickets)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
