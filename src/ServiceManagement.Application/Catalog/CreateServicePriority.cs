using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Application.Catalog;

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
