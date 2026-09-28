using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Application.Catalog;

public sealed record CreateSlaPolicyCommand(
    string Name,
    Guid PriorityId,
    int FirstResponseMinutes,
    int ResolutionMinutes,
    int AtRiskThresholdPercent) : IRequest<SlaPolicyDto>;

public sealed class CreateSlaPolicyCommandValidator : AbstractValidator<CreateSlaPolicyCommand>
{
    public CreateSlaPolicyCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.PriorityId).NotEmpty();
        RuleFor(command => command.FirstResponseMinutes).GreaterThan(0);
        RuleFor(command => command.ResolutionMinutes).GreaterThan(0);
        RuleFor(command => command.AtRiskThresholdPercent).InclusiveBetween(1, 99);

        RuleFor(command => command.ResolutionMinutes)
            .GreaterThanOrEqualTo(command => command.FirstResponseMinutes)
            .WithMessage("Resolution target must not be shorter than the first response target.");
    }
}

public sealed class CreateSlaPolicyCommandHandler : IRequestHandler<CreateSlaPolicyCommand, SlaPolicyDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;

    public CreateSlaPolicyCommandHandler(IApplicationDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<SlaPolicyDto> Handle(CreateSlaPolicyCommand request, CancellationToken cancellationToken)
    {
        var priority = await _dbContext.ServicePriorities
            .FirstOrDefaultAsync(candidate => candidate.Id == request.PriorityId, cancellationToken)
            ?? throw new NotFoundException(nameof(ServicePriority), request.PriorityId);

        var policy = new SlaPolicy(
            request.Name,
            priority.Id,
            request.FirstResponseMinutes,
            request.ResolutionMinutes,
            request.AtRiskThresholdPercent);

        _dbContext.SlaPolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.SlaPolicies, cancellationToken);

        return new SlaPolicyDto(
            policy.Id,
            policy.Name,
            policy.PriorityId,
            priority.Name,
            policy.FirstResponseMinutes,
            policy.ResolutionMinutes,
            policy.AtRiskThresholdPercent,
            policy.IsActive);
    }
}
