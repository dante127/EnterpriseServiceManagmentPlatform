using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Infrastructure.Persistence;

/// <summary>
/// The single write model. Entity configuration lives in
/// <c>Persistence/Configurations</c> so this class never grows into a
/// thousand-line <c>OnModelCreating</c>.
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<ServicePriority> ServicePriorities => Set<ServicePriority>();

    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketComment> TicketComments => Set<TicketComment>();

    public DbSet<TicketAssignmentHistory> TicketAssignmentHistories => Set<TicketAssignmentHistory>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Atomic, gap-tolerant ticket numbering. A database sequence is the
        // cheapest way to hand out unique numbers without a locking counter table.
        modelBuilder.HasSequence<long>("TicketNumbers", "dbo")
            .StartsAt(1000)
            .IncrementsBy(1);

        base.OnModelCreating(modelBuilder);
    }
}
