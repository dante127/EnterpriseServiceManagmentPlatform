namespace ServiceManagement.Domain.Exceptions;

/// <summary>
/// Root of every error raised because a business rule was violated.
/// The API layer translates these into 400/409 responses.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
