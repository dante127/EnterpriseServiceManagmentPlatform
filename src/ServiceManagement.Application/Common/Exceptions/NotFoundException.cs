namespace ServiceManagement.Application.Common.Exceptions;

/// <summary>Raised when a requested resource does not exist (mapped to HTTP 404).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string name, object key)
        : base($"{name} with identifier '{key}' was not found.")
    {
        Name = name;
        Key = key;
    }

    public NotFoundException(string message)
        : base(message)
    {
        Name = "Resource";
        Key = string.Empty;
    }

    public string Name { get; }

    public object Key { get; }
}
