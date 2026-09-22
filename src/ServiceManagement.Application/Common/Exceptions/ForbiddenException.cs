namespace ServiceManagement.Application.Common.Exceptions;

/// <summary>Raised when the caller is authenticated but lacks the required permission (HTTP 403).</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message)
        : base(message)
    {
    }
}
