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
