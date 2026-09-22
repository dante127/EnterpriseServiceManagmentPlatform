using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Identity;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique();

        builder.Property(user => user.UserName)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(user => user.UserName)
            .IsUnique();

        builder.Property(user => user.FirstName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(user => user.LastName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(user => user.SecurityStamp)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(user => user.OrganizationId);

        builder.HasIndex(user => user.OrganizationId);

        builder.HasIndex(user => user.IsActive);

        builder.HasMany(user => user.UserRoles)
            .WithOne(userRole => userRole.User)
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(user => user.RefreshTokens)
            .WithOne(token => token.User)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The backing field (not the read-only wrapper) is what EF materialises.
        builder.Navigation(user => user.TeamMemberships)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
