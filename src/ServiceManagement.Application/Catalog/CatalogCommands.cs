using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Application.Catalog;

public sealed record CreateServiceCategoryCommand(string Name, string Code, string? Description, Guid? ParentId)
    : IRequest<ServiceCategoryDto>;

public sealed class CreateServiceCategoryCommandValidator : AbstractValidator<CreateServiceCategoryCommand>
{
    public CreateServiceCategoryCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
    }
}

public sealed class CreateServiceCategoryCommandHandler
    : IRequestHandler<CreateServiceCategoryCommand, ServiceCategoryDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;

    public CreateServiceCategoryCommandHandler(IApplicationDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<ServiceCategoryDto> Handle(
        CreateServiceCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _dbContext.ServiceCategories.AnyAsync(category => category.Code == code, cancellationToken))
        {
            throw new ConflictException($"A category with code '{code}' already exists.");
        }

        if (request.ParentId is not null)
        {
            var parentExists = await _dbContext.ServiceCategories
                .AnyAsync(category => category.Id == request.ParentId, cancellationToken);

            if (!parentExists)
            {
                throw new NotFoundException(nameof(ServiceCategory), request.ParentId);
            }
        }

        var category = new ServiceCategory(request.Name, code, request.Description, request.ParentId);
        _dbContext.ServiceCategories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.ServiceCategories, cancellationToken);

        return new ServiceCategoryDto(
            category.Id,
            category.Name,
            category.Code,
            category.Description,
            category.ParentId,
            category.IsActive);
    }
}

public sealed record CreateServicePriorityCommand(string Name, string Code, int Level, string? ColorHex)
    : IRequest<ServicePriorityDto>;

public sealed class CreateServicePriorityCommandValidator : AbstractValidator<CreateServicePriorityCommand>
{
    public CreateServicePriorityCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Level).InclusiveBetween(1, 99);
    }
}

public sealed class CreateServicePriorityCommandHandler
    : IRequestHandler<CreateServicePriorityCommand, ServicePriorityDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;

    public CreateServicePriorityCommandHandler(IApplicationDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<ServicePriorityDto> Handle(
        CreateServicePriorityCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _dbContext.ServicePriorities.AnyAsync(priority => priority.Code == code, cancellationToken))
        {
            throw new ConflictException($"A priority with code '{code}' already exists.");
        }

        var priority = new ServicePriority(request.Name, code, request.Level, request.ColorHex);
        _dbContext.ServicePriorities.Add(priority);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.ServicePriorities, cancellationToken);

        return new ServicePriorityDto(
            priority.Id,
            priority.Name,
            priority.Code,
            priority.Level,
            priority.ColorHex,
            priority.IsActive);
    }
}

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
    }
}

public sealed class UpdateSlaPolicyCommandHandler : IRequestHandler<UpdateSlaPolicyCommand, SlaPolicyDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly Common.Interfaces.IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public UpdateSlaPolicyCommandHandler(
        IApplicationDbContext dbContext,
        ICacheService cache,
        Common.Interfaces.IAuditService auditService,
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

        await _dbContext.SaveChangesAsync(cancellationToken);

        // SLA policies are cached; invalidate so new tickets pick up the change.
        await _cache.RemoveAsync(CacheKeys.SlaPolicies, cancellationToken);

        await _auditService.RecordAsync(
            new Common.Interfaces.AuditEntry(
                Domain.Auditing.AuditActions.SlaPolicyChanged,
                nameof(SlaPolicy),
                policy.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                Common.Auditing.AuditJson.Serialize(previous),
                Common.Auditing.AuditJson.Serialize(new
                {
                    policy.FirstResponseMinutes,
                    policy.ResolutionMinutes,
                    policy.AtRiskThresholdPercent
                })),
            cancellationToken);

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
