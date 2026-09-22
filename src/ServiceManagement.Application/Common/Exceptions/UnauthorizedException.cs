namespace ServiceManagement.Application.Common.Exceptions;

/// <summary>Authentication failed (mapped to HTTP 401; never leaks which part was wrong).</summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message)
        : base(message)
    {
    }
}
