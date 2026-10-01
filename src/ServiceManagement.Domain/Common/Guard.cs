using ServiceManagement.Domain.Exceptions;

namespace ServiceManagement.Domain.Common;

/// <summary>
/// Small set of invariant checks used by domain constructors and behaviours.
/// Throwing here keeps aggregates valid by construction.
/// </summary>
public static class Guard
{
    public static string NotBlank(string? value, string name, int maxLength = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException($"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainRuleViolationException($"{name} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    public static Guid NotEmpty(Guid value, string name)
        => value == Guid.Empty
            ? throw new DomainRuleViolationException($"{name} is required.")
            : value;

    public static int Positive(int value, string name)
        => value <= 0
            ? throw new DomainRuleViolationException($"{name} must be greater than zero.")
            : value;

    public static int InRange(int value, int min, int max, string name)
        => value < min || value > max
            ? throw new DomainRuleViolationException($"{name} must be between {min} and {max}.")
            : value;
}
