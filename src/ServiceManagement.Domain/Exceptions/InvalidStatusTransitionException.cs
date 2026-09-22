namespace ServiceManagement.Domain.Exceptions;

/// <summary>
/// Raised when a ticket status change is not part of the controlled workflow.
/// Arbitrary status writes are impossible by design.
/// </summary>
public sealed class InvalidStatusTransitionException : DomainException
{
    public InvalidStatusTransitionException(string from, string to)
        : base($"Ticket status cannot change from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }

    public string From { get; }

    public string To { get; }
}
