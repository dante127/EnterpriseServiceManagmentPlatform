namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>
/// Produces the human-readable ticket number. Kept behind an abstraction so the
/// format (sequence based, date based, per-tenant, ...) can change without
/// touching the ticket module.
/// </summary>
public interface ITicketNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
