using Microsoft.Extensions.Options;

namespace ServiceManagement.Application.Common.Options;

/// <summary>
/// Conditional SMTP rules: an unconfigured section is valid (delivery logs
/// instead), but a host without a sender address is a misconfiguration that
/// would surface only as a failed send after an SLA breach - exactly when the
/// message matters most. Fail at startup instead.
/// </summary>
public sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            failures.Add("Notifications:Smtp:FromAddress is required when Notifications:Smtp:Host is set.");
        }
        else if (!options.FromAddress.Contains('@'))
        {
            failures.Add("Notifications:Smtp:FromAddress must be a valid e-mail address.");
        }

        if (!string.IsNullOrWhiteSpace(options.UserName) && string.IsNullOrEmpty(options.Password))
        {
            failures.Add("Notifications:Smtp:Password is required when Notifications:Smtp:UserName is set.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
