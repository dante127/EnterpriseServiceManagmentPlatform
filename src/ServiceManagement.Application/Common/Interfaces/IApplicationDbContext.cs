using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the persistence store seen by the application layer.
/// Handlers depend on this, never on the concrete DbContext, which keeps the
/// application free of infrastructure concerns while still allowing EF-specific
/// query composition where it is genuinely useful.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<UserRole> UserRoles { get; }

    DbSet<RolePermission> RolePermissions { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<Department> Departments { get; }

    DbSet<Team> Teams { get; }

    DbSet<TeamMember> TeamMembers { get; }

    DbSet<ServiceCategory> ServiceCategories { get; }

    DbSet<Service> Services { get; }

    DbSet<ServicePriority> ServicePriorities { get; }

    DbSet<SlaPolicy> SlaPolicies { get; }

    DbSet<Ticket> Tickets { get; }

    DbSet<TicketComment> TicketComments { get; }

    DbSet<TicketAssignmentHistory> TicketAssignmentHistories { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<AuditLog> AuditLogs { get; }

    /// <summary>Database facade, used for raw commands such as the ticket number sequence.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
