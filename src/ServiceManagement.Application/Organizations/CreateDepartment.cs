using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Application.Organizations;

public sealed record CreateDepartmentCommand(Guid OrganizationId, string Name, string Code, string? Description)
    : IRequest<DepartmentDto>;

public sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(command => command.OrganizationId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
    }
}

public sealed class CreateDepartmentCommandHandler : IRequestHandler<CreateDepartmentCommand, DepartmentDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public CreateDepartmentCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<DepartmentDto> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        // Sibling conventions: existence is checked with a query rather than an
        // Include of the whole department collection, a duplicate code is a 409
        // (the unique index would otherwise turn it into a 500), and the change
        // is audited like every other organizational change.
        var organizationExists = await _dbContext.Organizations
            .AnyAsync(candidate => candidate.Id == request.OrganizationId, cancellationToken);

        if (!organizationExists)
        {
            throw new NotFoundException(nameof(Organization), request.OrganizationId);
        }

        var code = request.Code.Trim().ToUpperInvariant();

        var codeTaken = await _dbContext.Departments
            .AnyAsync(department =>
                department.OrganizationId == request.OrganizationId && department.Code == code,
                cancellationToken);

        if (codeTaken)
        {
            throw new ConflictException($"A department with code '{code}' already exists in this organization.");
        }

        var department = new Department(request.OrganizationId, request.Name, code, request.Description);
        _dbContext.Departments.Add(department);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.DepartmentCreated,
                nameof(Department),
                department.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DepartmentDto(
            department.Id,
            department.OrganizationId,
            department.Name,
            department.Code,
            department.Description);
    }
}
