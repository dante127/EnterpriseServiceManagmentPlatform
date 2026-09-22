using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Organizations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");

        builder.HasKey(department => department.Id);

        builder.Property(department => department.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(department => department.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(department => new { department.OrganizationId, department.Code })
            .IsUnique();

        builder.Property(department => department.Description)
            .HasMaxLength(1000);

        builder.HasMany(department => department.Teams)
            .WithOne(team => team.Department)
            .HasForeignKey(team => team.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(department => department.Teams)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
