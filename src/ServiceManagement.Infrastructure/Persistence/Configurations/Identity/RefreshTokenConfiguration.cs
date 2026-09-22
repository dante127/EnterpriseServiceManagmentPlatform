using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Identity;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.TokenHash)
            .HasMaxLength(512)
            .IsRequired();

        // Lookup by hash is the hot path of /auth/refresh.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique();

        builder.Property(token => token.CreatedByIp)
            .HasMaxLength(64);

        builder.Property(token => token.RevokedByIp)
            .HasMaxLength(64);

        builder.Property(token => token.ReplacedByTokenHash)
            .HasMaxLength(512);

        builder.Property(token => token.ReasonRevoked)
            .HasMaxLength(100);

        builder.HasIndex(token => token.UserId);

        builder.HasIndex(token => token.ExpiresAt);
    }
}
