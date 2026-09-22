namespace ServiceManagement.Domain.Notifications;

/// <summary>Categories of notification the platform can raise.</summary>
public enum NotificationType
{
    TicketAssigned = 1,
    TicketReassigned = 2,
    TicketCommentAdded = 3,
    TicketStatusChanged = 4,
    SlaNearBreach = 5,
    SlaBreached = 6
}
