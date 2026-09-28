using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// A priority may legitimately have more than one active policy, which makes
/// "the" policy for a damaged ticket ambiguous. The repair therefore picks the
/// most recently created one, and this test states that rule against the real
/// seeded catalog rather than leaving it implicit in SQL.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SlaPolicySelectionTests
{
    private readonly DatabaseFixture _fixture;

    public SlaPolicySelectionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_priority_with_several_active_policies_resolves_to_the_newest()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetCatalogAsync();

        Guid priorityId;
        await using (var seed = _fixture.CreateContext())
        {
            priorityId = await seed.ServicePriorities.OrderBy(priority => priority.Level).Select(priority => priority.Id).FirstAsync();

            // A second active policy for the same priority: a newer, tighter one.
            seed.SlaPolicies.Add(new Domain.ServiceCatalog.SlaPolicy(
                "Gold (newer)",
                priorityId,
                firstResponseMinutes: 15,
                resolutionMinutes: 120));

            await seed.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var active = await context.SlaPolicies
                .AsNoTracking()
                .Where(policy => policy.IsActive && policy.PriorityId == priorityId)
                .ToListAsync();

            active.Should().HaveCountGreaterThan(1, "the scenario requires more than one candidate");

            // The rule the repair applies: newest CreatedAt wins, then Id as a
            // deterministic tie-break so two rows sharing a timestamp cannot
            // produce different results between runs.
            var chosen = active
                .OrderByDescending(policy => policy.CreatedAt)
                .ThenByDescending(policy => policy.Id)
                .First();

            chosen.Name.Should().Be("Gold (newer)");
        }
    }

    [SkippableFact]
    public async Task An_inactive_policy_is_never_selected()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetCatalogAsync();

        Guid priorityId;
        await using (var seed = _fixture.CreateContext())
        {
            priorityId = await seed.ServicePriorities.OrderBy(priority => priority.Level).Select(priority => priority.Id).FirstAsync();

            var retired = new Domain.ServiceCatalog.SlaPolicy("Retired", priorityId, firstResponseMinutes: 5, resolutionMinutes: 5);
            retired.Deactivate();
            seed.SlaPolicies.Add(retired);
            await seed.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var chosen = await context.SlaPolicies
                .AsNoTracking()
                .Where(policy => policy.IsActive && policy.PriorityId == priorityId)
                .OrderByDescending(policy => policy.CreatedAt)
                .ThenByDescending(policy => policy.Id)
                .FirstOrDefaultAsync();

            chosen.Should().NotBeNull();
            chosen!.Name.Should().NotBe("Retired", "a deactivated policy must never supply targets");
        }
    }
}
