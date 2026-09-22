namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed notification configuration.</summary>
public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// When false (the default) notifications are only stored in the database.
    /// The e-mail channel is an abstraction with a logging implementation.
    /// </summary>
    public bool EmailEnabled { get; set; }

    /// <summary>Retention window for read notifications before they are purged.</summary>
    public int ReadNotificationRetentionDays { get; set; } = 30;
}
