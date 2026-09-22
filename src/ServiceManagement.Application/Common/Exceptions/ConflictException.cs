namespace ServiceManagement.Application.Common.Exceptions;

/// <summary>Raised on state conflicts, including optimistic concurrency clashes (HTTP 409).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
