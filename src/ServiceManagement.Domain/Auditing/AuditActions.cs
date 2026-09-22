namespace ServiceManagement.Domain.Auditing;

/// <summary>Audit actions recorded for security-relevant and business-critical operations.</summary>
public static class AuditActions
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string TokenRefreshed = "TokenRefreshed";
    public const string PasswordChanged = "PasswordChanged";

    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserActivated = "UserActivated";
    public const string UserRoleChanged = "UserRoleChanged";

    public const string OrganizationCreated = "OrganizationCreated";
    public const string TeamCreated = "TeamCreated";
    public const string TeamMemberAdded = "TeamMemberAdded";
    public const string TeamMemberRemoved = "TeamMemberRemoved";

    public const string TicketCreated = "TicketCreated";
    public const string TicketUpdated = "TicketUpdated";
    public const string TicketAssigned = "TicketAssigned";
    public const string TicketStatusChanged = "TicketStatusChanged";
    public const string TicketCommentAdded = "TicketCommentAdded";
    public const string TicketDeleted = "TicketDeleted";
    public const string SlaPolicyChanged = "SlaPolicyChanged";
}
