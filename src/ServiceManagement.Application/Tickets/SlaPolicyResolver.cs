using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Resolves the SLA targets a ticket should carry and applies them.
/// <para>
/// This is the single place that answers "what deadlines does this ticket get?",
/// shared by ticket creation and ticket update. Resolution order is:
/// the service's preferred policy, then the active policy for the priority,
/// then the configured fallback targets.
/// </para>
/// <para>
/// A ticket is never left without deadlines. Deadlines are what make it visible
/// to the SLA monitor, so an unresolved policy must degrade to the documented
/// fallback rather than silently returning and leaving the ticket unmeasurable.
/// </para>
/// </summary>
public sealed class SlaPolicyResolver
{
    private readonly IApplicationDbContext _dbContext;
    private readonly SlaOptions _options;

    public SlaPolicyResolver(IApplicationDbContext dbContext, IOptions<SlaOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task ApplyToAsync(
        Ticket ticket,
        Guid priorityId,
        Guid? preferredPolicyId,
        CancellationToken cancellationToken)
    {
        var policy = await ResolveAsync(preferredPolicyId, priorityId, cancellationToken);

        if (policy is not null)
        {
            ticket.ApplySlaPolicy(
                policy.FirstResponseMinutes,
                policy.ResolutionMinutes,
                policy.AtRiskThresholdPercent);
            return;
        }

        ticket.ApplySlaPolicy(
            _options.FallbackFirstResponseMinutes,
            _options.FallbackResolutionMinutes,
            _options.AtRiskThresholdPercent);
    }

    private async Task<SlaPolicy?> ResolveAsync(
        Guid? preferredPolicyId,
        Guid priorityId,
        CancellationToken cancellationToken)
    {
        if (preferredPolicyId is not null)
        {
            var preferred = await _dbContext.SlaPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    policy => policy.Id == preferredPolicyId && policy.IsActive,
                    cancellationToken);

            if (preferred is not null)
            {
                return preferred;
            }
        }

        return await _dbContext.SlaPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                policy => policy.PriorityId == priorityId && policy.IsActive,
                cancellationToken);
    }
}
