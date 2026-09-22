namespace ServiceManagement.Domain.Exceptions;

/// <summary>Raised when an invariant would be broken (required value missing, invalid range, and so on).</summary>
public sealed class DomainRuleViolationException : DomainException
{
    public DomainRuleViolationException(string message)
        : base(message)
    {
    }
}
