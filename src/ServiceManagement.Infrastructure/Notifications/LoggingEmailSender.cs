using Microsoft.Extensions.Logging;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Infrastructure.Notifications;

/// <summary>
/// Development e-mail provider. It logs the message instead of contacting an
/// SMTP server, which is exactly what the portfolio build needs: the abstraction
/// and its call sites are real, the transport is swappable.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[email-mock] To: {Recipient} | Subject: {Subject} | Body: {Body}",
            to,
            subject,
            body);

        return Task.CompletedTask;
    }
}
