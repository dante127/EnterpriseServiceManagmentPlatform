using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("ServiceCategories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(category => category.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(category => category.Code)
            .IsUnique();

        builder.Property(category => category.Description)
            .HasMaxLength(1000);

        builder.HasOne(category => category.Parent)
            .WithMany(parent => parent.Children)
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(category => category.Services)
            .WithOne(service => service.Category)
            .HasForeignKey(service => service.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(category => category.Children)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(category => category.Services)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
