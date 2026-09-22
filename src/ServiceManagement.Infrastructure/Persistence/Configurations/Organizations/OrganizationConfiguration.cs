using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Organizations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");

        builder.HasKey(organization => organization.Id);

        builder.Property(organization => organization.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(organization => organization.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(organization => organization.Code)
            .IsUnique();

        builder.Property(organization => organization.Description)
            .HasMaxLength(1000);

        builder.HasMany(organization => organization.Departments)
            .WithOne(department => department.Organization)
            .HasForeignKey(department => department.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(organization => organization.Teams)
            .WithOne()
            .HasForeignKey(team => team.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(organization => organization.Departments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(organization => organization.Teams)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
