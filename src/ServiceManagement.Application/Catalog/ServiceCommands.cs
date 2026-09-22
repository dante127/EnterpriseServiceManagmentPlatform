using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Application.Catalog;

public sealed record CreateServiceCommand(
    Guid CategoryId,
    string Name,
    string Code,
    string? Description,
    Guid? DefaultPriorityId,
    Guid? DefaultSlaPolicyId) : IRequest<ServiceDto>;

public sealed class CreateServiceCommandValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceCommandValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
    }
}

public sealed class CreateServiceCommandHandler : IRequestHandler<CreateServiceCommand, ServiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;

    public CreateServiceCommandHandler(IApplicationDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<ServiceDto> Handle(CreateServiceCommand request, CancellationToken cancellationToken)
    {
        var category = await _dbContext.ServiceCategories
            .FirstOrDefaultAsync(candidate => candidate.Id == request.CategoryId, cancellationToken)
            ?? throw new NotFoundException(nameof(ServiceCategory), request.CategoryId);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await _dbContext.Services.AnyAsync(service => service.Code == code, cancellationToken))
        {
            throw new ConflictException($"A service with code '{code}' already exists.");
        }

        var service = new Service(
            category.Id,
            request.Name,
            code,
            request.Description,
            request.DefaultPriorityId,
            request.DefaultSlaPolicyId);

        _dbContext.Services.Add(service);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.Services, cancellationToken);

        return new ServiceDto(
            service.Id,
            service.Name,
            service.Code,
            service.Description,
            service.CategoryId,
            category.Name,
            service.DefaultPriorityId,
            service.DefaultSlaPolicyId,
            service.IsActive);
    }
}

public sealed record UpdateServiceCommand(Guid ServiceId, string Name, string? Description, Guid? DefaultPriorityId, Guid? DefaultSlaPolicyId)
    : IRequest<ServiceDto>;

public sealed class UpdateServiceCommandValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceCommandValidator()
    {
        RuleFor(command => command.ServiceId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateServiceCommandHandler : IRequestHandler<UpdateServiceCommand, ServiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;

    public UpdateServiceCommandHandler(IApplicationDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<ServiceDto> Handle(UpdateServiceCommand request, CancellationToken cancellationToken)
    {
        var service = await _dbContext.Services
            .Include(candidate => candidate.Category)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.ServiceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Service), request.ServiceId);

        service.UpdateDetails(request.Name, request.Description);
        service.SetDefaults(request.DefaultPriorityId, request.DefaultSlaPolicyId);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.Services, cancellationToken);

        return new ServiceDto(
            service.Id,
            service.Name,
            service.Code,
            service.Description,
            service.CategoryId,
            service.Category.Name,
            service.DefaultPriorityId,
            service.DefaultSlaPolicyId,
            service.IsActive);
    }
}
