using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Catalog;

public sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("SlaPolicies");

        builder.HasKey(policy => policy.Id);

        builder.Property(policy => policy.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(policy => policy.FirstResponseMinutes)
            .IsRequired();

        builder.Property(policy => policy.ResolutionMinutes)
            .IsRequired();

        builder.Property(policy => policy.AtRiskThresholdPercent)
            .IsRequired();

        builder.HasIndex(policy => policy.PriorityId);

        builder.HasIndex(policy => policy.IsActive);
    }
}
