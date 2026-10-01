using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Application.Catalog;

public sealed record UpdateSlaPolicyCommand(
    Guid SlaPolicyId,
    int FirstResponseMinutes,
    int ResolutionMinutes,
    int AtRiskThresholdPercent) : IRequest<SlaPolicyDto>;

public sealed class UpdateSlaPolicyCommandValidator : AbstractValidator<UpdateSlaPolicyCommand>
{
    public UpdateSlaPolicyCommandValidator()
    {
        RuleFor(command => command.SlaPolicyId).NotEmpty();
        RuleFor(command => command.FirstResponseMinutes).GreaterThan(0);
        RuleFor(command => command.ResolutionMinutes).GreaterThan(0);
        RuleFor(command => command.AtRiskThresholdPercent).InclusiveBetween(1, 99);

        // Same invariant creation enforces: an updated policy must not be able to
        // promise a resolution before the first response is even due.
        RuleFor(command => command.ResolutionMinutes)
            .GreaterThanOrEqualTo(command => command.FirstResponseMinutes)
            .WithMessage("The resolution target must not be shorter than the first response target.");
    }
}

public sealed class UpdateSlaPolicyCommandHandler : IRequestHandler<UpdateSlaPolicyCommand, SlaPolicyDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public UpdateSlaPolicyCommandHandler(
        IApplicationDbContext dbContext,
        ICacheService cache,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _cache = cache;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<SlaPolicyDto> Handle(UpdateSlaPolicyCommand request, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.SlaPolicies
            .Include(candidate => candidate.Priority)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.SlaPolicyId, cancellationToken)
            ?? throw new NotFoundException(nameof(SlaPolicy), request.SlaPolicyId);

        var previous = new
        {
            policy.FirstResponseMinutes,
            policy.ResolutionMinutes,
            policy.AtRiskThresholdPercent
        };

        policy.UpdateTargets(
            request.FirstResponseMinutes,
            request.ResolutionMinutes,
            request.AtRiskThresholdPercent);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.SlaPolicyChanged,
                nameof(SlaPolicy),
                policy.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                AuditJson.Serialize(previous),
                AuditJson.Serialize(new
                {
                    policy.FirstResponseMinutes,
                    policy.ResolutionMinutes,
                    policy.AtRiskThresholdPercent
                })),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // SLA policies are cached; invalidate so new tickets pick up the change.
        await _cache.RemoveAsync(CacheKeys.SlaPolicies, cancellationToken);

        return new SlaPolicyDto(
            policy.Id,
            policy.Name,
            policy.PriorityId,
            policy.Priority.Name,
            policy.FirstResponseMinutes,
            policy.ResolutionMinutes,
            policy.AtRiskThresholdPercent,
            policy.IsActive);
    }
}
