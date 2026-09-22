using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Identity;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");

        builder.HasKey(permission => permission.Id);

        builder.Property(permission => permission.Name)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(permission => permission.NormalizedName)
            .IsUnique();

        builder.Property(permission => permission.Group)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(permission => permission.Description)
            .HasMaxLength(500);

        builder.HasIndex(permission => permission.Group);
    }
}
