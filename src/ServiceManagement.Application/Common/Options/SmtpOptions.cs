using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>
/// Real e-mail delivery. The section is empty by default: with no host configured
/// the e-mail channel falls back to the logging sender, and notifications exist
/// only in the database.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Notifications:Smtp";

    /// <summary>Empty disables real SMTP delivery.</summary>
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string FromAddress { get; set; } = string.Empty;

    public string FromDisplayName { get; set; } = "Service Management";

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
