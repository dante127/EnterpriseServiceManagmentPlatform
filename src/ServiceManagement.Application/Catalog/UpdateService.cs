using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.ServiceCatalog;

namespace ServiceManagement.Application.Catalog;

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
