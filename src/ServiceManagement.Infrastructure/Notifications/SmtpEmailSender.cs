using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.Infrastructure.Notifications;

/// <summary>
/// Real e-mail delivery over SMTP with STARTTLS. Registered in place of the
/// logging sender whenever Notifications:Smtp:Host is configured.
/// <para>
/// The sender throws on failure: delivery errors are policy, not transport
/// detail, and the caller decides what best effort means (the SLA cycle isolates
/// each send so one broken mailbox never blocks the rest of the batch).
/// </para>
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        using var message = new MailMessage(
            new MailAddress(_options.FromAddress, _options.FromDisplayName),
            new MailAddress(to))
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Timeout = _options.TimeoutSeconds * 1000,
            Credentials = string.IsNullOrWhiteSpace(_options.UserName)
                ? null
                : new NetworkCredential(_options.UserName, _options.Password)
        };

        await client.SendMailAsync(message, cancellationToken);

        _logger.LogInformation("E-mail sent to {Recipient}: {Subject}", to, subject);
    }
}
