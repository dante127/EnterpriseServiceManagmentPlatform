using ServiceManagement.Application.Common.Exceptions;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Optimistic concurrency guard. The client echoes back the rowversion it read;
/// a mismatch means somebody else already changed the ticket, which is reported
/// as HTTP 409 instead of silently overwriting their work.
/// </summary>
internal static class TicketConcurrency
{
    public static void EnsureUnchanged(Domain.Tickets.Ticket ticket, string? expectedRowVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedRowVersion))
        {
            return;
        }

        byte[] expected;

        try
        {
            expected = Convert.FromBase64String(expectedRowVersion);
        }
        catch (FormatException)
        {
            throw new ConflictException("The supplied concurrency token is not a valid base64 value.");
        }

        if (!ticket.RowVersion.AsSpan().SequenceEqual(expected))
        {
            throw new ConflictException(
                $"Ticket '{ticket.TicketNumber}' was modified by another user. Reload it and try again.");
        }
    }
}
