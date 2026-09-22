using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.UnitTests.Persistence;

/// <summary>
/// Builds the real EF model against the SQL Server provider without opening a
/// connection. This catches configuration regressions (shadow foreign keys,
/// missing concurrency tokens, wrong index uniqueness) without needing a server.
/// </summary>
public class ModelConfigurationTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ServiceManagementModelOnly;Trusted_Connection=True;")
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public void The_model_builds_without_errors()
    {
        using var context = CreateContext();

        var model = context.Model;

        model.Should().NotBeNull();
        model.GetEntityTypes().Should().HaveCountGreaterThanOrEqualTo(19);
    }

    [Fact]
    public void Every_aggregate_is_mapped_to_an_expected_table()
    {
        using var context = CreateContext();

        var tables = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(name => name is not null)
            .ToList();

        tables.Should().Contain(
        [
            "Users", "Roles", "Permissions", "RefreshTokens", "UserRoles", "RolePermissions",
            "Organizations", "Departments", "Teams", "TeamMembers",
            "ServiceCategories", "Services", "ServicePriorities", "SlaPolicies",
            "Tickets", "TicketComments", "TicketAssignmentHistory",
            "Notifications", "AuditLogs"
        ]);
    }

    [Fact]
    public void Ticket_rowversion_is_the_concurrency_token()
    {
        using var context = CreateContext();

        var rowVersion = context.Model.FindEntityType(typeof(Ticket))!
            .FindProperty(nameof(Ticket.RowVersion))!;

        rowVersion.IsConcurrencyToken.Should().BeTrue();
        rowVersion.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
    }

    [Fact]
    public void Ticket_number_and_user_email_are_unique()
    {
        using var context = CreateContext();

        var ticketNumberIndex = context.Model.FindEntityType(typeof(Ticket))!
            .GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(Ticket.TicketNumber)));

        ticketNumberIndex.IsUnique.Should().BeTrue();

        var emailIndex = context.Model.FindEntityType(typeof(User))!
            .GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(User.Email)));

        emailIndex.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Idempotency_key_index_is_unique_but_only_for_non_null_values()
    {
        using var context = CreateContext();

        var index = context.Model.FindEntityType(typeof(Ticket))!
            .GetIndexes()
            .Single(candidate => candidate.Properties.Any(property => property.Name == nameof(Ticket.IdempotencyKey)));

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_ticket_number_sequence_is_declared()
    {
        using var context = CreateContext();

        var sequence = context.Model.FindSequence("TicketNumbers", "dbo");

        sequence.Should().NotBeNull();
    }

    [Fact]
    public void Soft_delete_query_filter_is_declared_on_tickets()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(Ticket))!;

        entityType.GetDeclaredQueryFilters().Should().NotBeEmpty();
    }

    [Fact]
    public void No_shadow_foreign_key_was_created_for_departments()
    {
        using var context = CreateContext();

        var teamEntity = context.Model.FindEntityType(typeof(Team))!;

        teamEntity.FindProperty("DepartmentId1").Should().BeNull();
        teamEntity.FindProperty(nameof(Team.DepartmentId)).Should().NotBeNull();
    }

    [Fact]
    public void Audit_logs_are_not_coupled_to_users_by_a_foreign_key()
    {
        using var context = CreateContext();

        var foreignKeys = context.Model.FindEntityType(typeof(AuditLog))!
            .GetForeignKeys()
            .ToList();

        // History must survive user deletion, so the reference stays loose.
        foreignKeys.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_tokens_only_store_a_hash_column()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(RefreshToken))!;

        entityType.FindProperty(nameof(RefreshToken.TokenHash)).Should().NotBeNull();
        entityType.GetProperties().Should().NotContain(property => property.Name == "Token");
    }
}
