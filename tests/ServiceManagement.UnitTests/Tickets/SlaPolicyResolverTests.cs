using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.UnitTests.TestData;

namespace ServiceManagement.UnitTests.Tickets;

/// <summary>
/// The resolver is the single place that decides which SLA targets a ticket gets.
/// Both create and update must end up with usable deadlines, so these tests pin
/// the resolution order: explicit policy, then priority policy, then fallback.
/// </summary>
public class SlaPolicyResolverTests
{
    [Fact]
    public async Task With_an_active_priority_policy_the_policy_targets_are_applied()
    {
        var priorityId = Guid.CreateVersion7();
        await using var db = await PolicyStore.CreateAsync(
            new SlaPolicy("Standard", priorityId, firstResponseMinutes: 45, resolutionMinutes: 480));

        var ticket = TicketFactory.Create();
        var resolver = new SlaPolicyResolver(db, FallbackOptions());

        await resolver.ApplyToAsync(ticket, priorityId, preferredPolicyId: null, CancellationToken.None);

        ticket.FirstResponseDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(45));
        ticket.ResolutionDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(480));
    }

    [Fact]
    public async Task A_preferred_service_policy_wins_over_the_priority_policy()
    {
        var priorityId = Guid.CreateVersion7();
        var gold = new SlaPolicy("Gold", priorityId, firstResponseMinutes: 15, resolutionMinutes: 240);
        var standard = new SlaPolicy("Standard", priorityId, firstResponseMinutes: 45, resolutionMinutes: 480);
        await using var db = await PolicyStore.CreateAsync(gold, standard);

        var ticket = TicketFactory.Create();
        var resolver = new SlaPolicyResolver(db, FallbackOptions());

        await resolver.ApplyToAsync(ticket, priorityId, preferredPolicyId: gold.Id, CancellationToken.None);

        ticket.FirstResponseDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(15));
        ticket.ResolutionDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(240));
    }

    [Fact]
    public async Task With_no_active_policy_the_configured_fallback_is_used()
    {
        // Regression guard: the update path used to return early here, leaving the
        // ticket with null deadlines and therefore outside SLA monitoring.
        var priorityId = Guid.CreateVersion7();
        await using var db = await PolicyStore.CreateAsync();

        var ticket = TicketFactory.Create();
        var resolver = new SlaPolicyResolver(db, FallbackOptions(firstResponseMinutes: 90, resolutionMinutes: 600));

        await resolver.ApplyToAsync(ticket, priorityId, preferredPolicyId: null, CancellationToken.None);

        ticket.FirstResponseDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(90));
        ticket.ResolutionDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(600));
    }

    [Fact]
    public async Task An_inactive_preferred_policy_falls_through_to_the_priority_policy()
    {
        var priorityId = Guid.CreateVersion7();
        var deactivated = new SlaPolicy("Retired", priorityId, firstResponseMinutes: 15, resolutionMinutes: 240);
        deactivated.Deactivate();
        var standard = new SlaPolicy("Standard", priorityId, firstResponseMinutes: 45, resolutionMinutes: 480);
        await using var db = await PolicyStore.CreateAsync(deactivated, standard);

        var ticket = TicketFactory.Create();
        var resolver = new SlaPolicyResolver(db, FallbackOptions());

        await resolver.ApplyToAsync(ticket, priorityId, preferredPolicyId: deactivated.Id, CancellationToken.None);

        ticket.ResolutionDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(480));
    }

    private static IOptions<SlaOptions> FallbackOptions(int firstResponseMinutes = 120, int resolutionMinutes = 1440)
        => Options.Create(new SlaOptions
        {
            FallbackFirstResponseMinutes = firstResponseMinutes,
            FallbackResolutionMinutes = resolutionMinutes,
            AtRiskThresholdPercent = 20
        });

    /// <summary>
    /// Builds a real context so the resolver's LINQ is executed by EF rather than
    /// by a substitute. A substitute cannot return a <c>DbSet</c>, and mocking the
    /// query away would not prove the lookup works.
    /// </summary>
    private static class PolicyStore
    {
        public static async Task<ApplicationDbContext> CreateAsync(params SlaPolicy[] policies)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"sla-resolver-{Guid.CreateVersion7()}")
                .Options;

            await using var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();

            if (policies.Length > 0)
            {
                db.SlaPolicies.AddRange(policies);
                await db.SaveChangesAsync();
            }

            // The context is handed to the caller, so it must not be disposed here.
            return await Task.FromResult(new ApplicationDbContext(options));
        }
    }
}
