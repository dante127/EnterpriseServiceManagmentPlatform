using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Infrastructure.Persistence.Configurations.Organizations;

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");

        builder.HasKey(team => team.Id);

        builder.Property(team => team.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(team => team.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(team => new { team.DepartmentId, team.Code })
            .IsUnique();

        builder.Property(team => team.Description)
            .HasMaxLength(1000);

        builder.HasOne(team => team.Manager)
            .WithMany()
            .HasForeignKey(team => team.ManagerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(team => team.Members)
            .WithOne(member => member.Team)
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(team => team.Members)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
