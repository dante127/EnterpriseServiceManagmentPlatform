using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Constants;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.Infrastructure.Persistence.Seed;

/// <summary>
/// Development seed data. Credentials below are DEVELOPMENT-ONLY, are documented
/// in the README, and are never used to configure a deployment: production
/// accounts are created through the API.
/// </summary>
public static class DatabaseSeeder
{
    public const string AdminEmail = "admin@servicemanagement.local";
    public const string AdminPassword = "Admin#Dev12345!";
    public const string ManagerEmail = "manager@servicemanagement.local";
    public const string ManagerPassword = "Manager#Dev12345!";
    public const string AgentEmail = "agent@servicemanagement.local";
    public const string AgentPassword = "Agent#Dev12345!";
    public const string RequesterEmail = "requester@servicemanagement.local";
    public const string RequesterPassword = "Requester#Dev12345!";

    public static async Task SeedAsync(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IDateTimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(clock);

        if (await context.Users.AnyAsync(cancellationToken))
        {
            // Seeding is a one-time bootstrap; existing data is never touched.
            return;
        }

        var permissions = await SeedPermissionsAsync(context, cancellationToken);
        var roles = await SeedRolesAsync(context, permissions, cancellationToken);
        var organization = await SeedOrganizationAsync(context, cancellationToken);
        var catalog = await SeedCatalogAsync(context, cancellationToken);
        await SeedUsersAsync(context, passwordHasher, roles, organization, cancellationToken);
        await SeedSampleTicketsAsync(context, catalog, clock, cancellationToken);
    }

    private static async Task<Dictionary<string, Permission>> SeedPermissionsAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var permissions = Permissions.All
            .Select(name => new Permission(name, name.Split('.')[0]))
            .ToList();

        context.Permissions.AddRange(permissions);
        await context.SaveChangesAsync(cancellationToken);

        return permissions.ToDictionary(permission => permission.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, Role>> SeedRolesAsync(
        ApplicationDbContext context,
        IReadOnlyDictionary<string, Permission> permissions,
        CancellationToken cancellationToken)
    {
        var administrator = new Role(RoleNames.Administrator, "Unrestricted access.", isSystemRole: true);
        var manager = new Role(RoleNames.Manager, "Owns teams and service levels.", isSystemRole: true);
        var agent = new Role(RoleNames.Agent, "Resolves tickets.", isSystemRole: true);
        var requester = new Role(RoleNames.Requester, "Raises tickets.", isSystemRole: true);

        Grant(administrator, permissions, [.. Permissions.All]);

        Grant(manager, permissions,
        [
            Permissions.TicketsRead, Permissions.TicketsCreate, Permissions.TicketsUpdate, Permissions.TicketsAssign,
            Permissions.TicketsComment, Permissions.TicketsCommentInternal, Permissions.TicketsManage,
            Permissions.UsersRead, Permissions.TeamsManage, Permissions.CatalogRead, Permissions.CatalogManage,
            Permissions.ReportsRead, Permissions.AuditRead, Permissions.NotificationsRead
        ]);

        Grant(agent, permissions,
        [
            Permissions.TicketsRead, Permissions.TicketsCreate, Permissions.TicketsUpdate, Permissions.TicketsAssign,
            Permissions.TicketsComment, Permissions.TicketsCommentInternal,
            Permissions.CatalogRead, Permissions.ReportsRead, Permissions.NotificationsRead
        ]);

        Grant(requester, permissions,
        [
            Permissions.TicketsRead, Permissions.TicketsCreate, Permissions.TicketsComment,
            Permissions.CatalogRead, Permissions.NotificationsRead
        ]);

        var roles = new[] { administrator, manager, agent, requester };

        context.Roles.AddRange(roles);
        await context.SaveChangesAsync(cancellationToken);

        return roles.ToDictionary(role => role.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static void Grant(
        Role role,
        IReadOnlyDictionary<string, Permission> permissions,
        IEnumerable<string> permissionNames)
    {
        foreach (var name in permissionNames)
        {
            if (permissions.TryGetValue(name, out var permission))
            {
                role.GrantPermission(permission.Id);
            }
        }
    }

    private static async Task<Organization> SeedOrganizationAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var organization = new Organization("Northwind Services", "NW", "Reference tenant for the development environment.");
        var department = organization.AddDepartment("IT Operations", "ITOPS", "Runs the service desk.");

        context.Organizations.Add(organization);
        await context.SaveChangesAsync(cancellationToken);

        var team = new Team(organization.Id, department.Id, "Service Desk", "SD", "First line support team.");
        context.Teams.Add(team);
        await context.SaveChangesAsync(cancellationToken);

        return organization;
    }

    private static async Task<CatalogSeed> SeedCatalogAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var hardware = new ServiceCategory("Hardware", "HW", "Physical equipment.");
        var software = new ServiceCategory("Software", "SW", "Applications and licences.");
        var network = new ServiceCategory("Network", "NET", "Connectivity and access.");

        context.ServiceCategories.AddRange(hardware, software, network);

        var critical = new ServicePriority("Critical", "P1", 1, "#B91C1C");
        var high = new ServicePriority("High", "P2", 2, "#EA580C");
        var medium = new ServicePriority("Medium", "P3", 3, "#CA8A04");
        var low = new ServicePriority("Low", "P4", 4, "#15803D");

        context.ServicePriorities.AddRange(critical, high, medium, low);
        await context.SaveChangesAsync(cancellationToken);

        var criticalPolicy = new SlaPolicy("Critical SLA", critical.Id, firstResponseMinutes: 30, resolutionMinutes: 240, atRiskThresholdPercent: 20);
        var highPolicy = new SlaPolicy("High SLA", high.Id, firstResponseMinutes: 60, resolutionMinutes: 480, atRiskThresholdPercent: 20);
        var mediumPolicy = new SlaPolicy("Medium SLA", medium.Id, firstResponseMinutes: 240, resolutionMinutes: 1440, atRiskThresholdPercent: 25);
        var lowPolicy = new SlaPolicy("Low SLA", low.Id, firstResponseMinutes: 480, resolutionMinutes: 2880, atRiskThresholdPercent: 25);

        context.SlaPolicies.AddRange(criticalPolicy, highPolicy, mediumPolicy, lowPolicy);        await context.SaveChangesAsync(cancellationToken);

        var itSupport = new Service(
            hardware.Id,
            "IT Support",
            "IT-SUP",
            "Hardware incidents and requests.",
            critical.Id,
            criticalPolicy.Id);

        var emailSupport = new Service(
            software.Id,
            "Email Support",
            "EMAIL-SUP",
            "Mailbox and collaboration issues.",
            high.Id,
            highPolicy.Id);

        var vpnAccess = new Service(
            network.Id,
            "VPN Access",
            "VPN-ACCESS",
            "Remote connectivity requests.",
            medium.Id,
            mediumPolicy.Id);

        context.Services.AddRange(itSupport, emailSupport, vpnAccess);
        await context.SaveChangesAsync(cancellationToken);

        return new CatalogSeed(
            itSupport.Id, hardware.Id, critical.Id, criticalPolicy,
            emailSupport.Id, software.Id, high.Id, highPolicy,
            vpnAccess.Id, network.Id, medium.Id, mediumPolicy,
            low.Id, lowPolicy);
    }

    private static async Task SeedUsersAsync(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IReadOnlyDictionary<string, Role> roles,
        Organization organization,
        CancellationToken cancellationToken)
    {
        var departmentId = await context.Departments
            .Where(department => department.OrganizationId == organization.Id)
            .Select(department => department.Id)
            .FirstAsync(cancellationToken);

        var admin = new User(AdminEmail, "admin", "Ada", "Admin", passwordHasher.Hash(AdminPassword));
        admin.AddRole(roles[RoleNames.Administrator].Id);
        admin.AssignToOrganization(organization.Id, departmentId);

        var manager = new User(ManagerEmail, "manager", "Mo", "Manager", passwordHasher.Hash(ManagerPassword));
        manager.AddRole(roles[RoleNames.Manager].Id);
        manager.AssignToOrganization(organization.Id, departmentId);

        var agent = new User(AgentEmail, "agent", "Ava", "Agent", passwordHasher.Hash(AgentPassword));
        agent.AddRole(roles[RoleNames.Agent].Id);
        agent.AssignToOrganization(organization.Id, departmentId);

        var requester = new User(RequesterEmail, "requester", "Rita", "Requester", passwordHasher.Hash(RequesterPassword));
        requester.AddRole(roles[RoleNames.Requester].Id);
        requester.AssignToOrganization(organization.Id, departmentId);

        context.Users.AddRange(admin, manager, agent, requester);
        await context.SaveChangesAsync(cancellationToken);

        var team = await context.Teams
            .FirstAsync(candidate => candidate.OrganizationId == organization.Id, cancellationToken);

        team.SetManager(manager.Id);
        team.AddMember(manager.Id, isManager: true);
        team.AddMember(agent.Id);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSampleTicketsAsync(
        ApplicationDbContext context,
        CatalogSeed catalog,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        var agentId = await context.Users
            .Where(user => user.Email == AgentEmail)
            .Select(user => user.Id)
            .FirstAsync(cancellationToken);

        var requesterId = await context.Users
            .Where(user => user.Email == RequesterEmail)
            .Select(user => user.Id)
            .FirstAsync(cancellationToken);

        var teamId = await context.Teams
            .Select(team => team.Id)
            .FirstAsync(cancellationToken);

        var now = clock.UtcNow;

        // Deliberately spread across the workflow and the SLA clock so the
        // dashboard and the SLA monitor have meaningful data to work with.
        var laptop = CreateTicket("TKT-000001", "Laptop will not boot", "The laptop shows a black screen after the vendor update.", catalog.ItSupportServiceId, catalog.HardwareCategoryId, catalog.CriticalPriorityId, requesterId, now.AddHours(-6));
        laptop.ApplySlaPolicy(catalog.CriticalPolicy.FirstResponseMinutes, catalog.CriticalPolicy.ResolutionMinutes, catalog.CriticalPolicy.AtRiskThresholdPercent);
        laptop.AssignToUser(agentId, agentId, "First line owns hardware incidents.", now.AddHours(-5));

        var mailbox = CreateTicket("TKT-000002", "Mailbox quota exceeded", "Cannot receive mail since this morning.", catalog.EmailSupportServiceId, catalog.SoftwareCategoryId, catalog.HighPriorityId, requesterId, now.AddHours(-3));
        mailbox.ApplySlaPolicy(catalog.HighPolicy.FirstResponseMinutes, catalog.HighPolicy.ResolutionMinutes, catalog.HighPolicy.AtRiskThresholdPercent);
        mailbox.AssignToTeam(teamId, agentId, "Queued for the service desk.", now.AddHours(-2));
        mailbox.ChangeStatus(TicketStatus.InProgress, agentId, now.AddHours(-2));
        mailbox.AddComment("Investigating the mailbox limits.", agentId, isInternal: true, now.AddHours(-1));

        var vpn = CreateTicket("TKT-000003", "VPN client cannot connect", "Remote workers report authentication failures.", catalog.VpnAccessServiceId, catalog.NetworkCategoryId, catalog.MediumPriorityId, requesterId, now.AddDays(-2));
        vpn.ApplySlaPolicy(catalog.MediumPolicy.FirstResponseMinutes, catalog.MediumPolicy.ResolutionMinutes, catalog.MediumPolicy.AtRiskThresholdPercent);
        vpn.AssignToUser(agentId, agentId, "Assigned to network duty agent.", now.AddDays(-2).AddMinutes(30));
        vpn.ChangeStatus(TicketStatus.InProgress, agentId, now.AddDays(-2).AddMinutes(45));
        vpn.ChangeStatus(TicketStatus.Resolved, agentId, now.AddDays(-2).AddHours(4));
        vpn.ChangeStatus(TicketStatus.Closed, agentId, now.AddDays(-1));

        var keyboard = CreateTicket("TKT-000004", "Replacement keyboard request", "Keys are sticking on several devices.", catalog.ItSupportServiceId, catalog.HardwareCategoryId, catalog.LowPriorityId, requesterId, now.AddDays(-5));
        keyboard.ApplySlaPolicy(catalog.LowPolicy.FirstResponseMinutes, catalog.LowPolicy.ResolutionMinutes, catalog.LowPolicy.AtRiskThresholdPercent);

        context.Tickets.AddRange(laptop, mailbox, vpn, keyboard);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Ticket CreateTicket(
        string ticketNumber,
        string title,
        string description,
        Guid serviceId,
        Guid categoryId,
        Guid priorityId,
        Guid requesterId,
        DateTimeOffset reportedAt)
        => new(ticketNumber, title, description, serviceId, categoryId, priorityId, requesterId, reportedAt);

    private sealed record CatalogSeed(
        Guid ItSupportServiceId,
        Guid HardwareCategoryId,
        Guid CriticalPriorityId,
        SlaPolicy CriticalPolicy,
        Guid EmailSupportServiceId,
        Guid SoftwareCategoryId,
        Guid HighPriorityId,
        SlaPolicy HighPolicy,
        Guid VpnAccessServiceId,
        Guid NetworkCategoryId,
        Guid MediumPriorityId,
        SlaPolicy MediumPolicy,
        Guid LowPriorityId,
        SlaPolicy LowPolicy);
}
