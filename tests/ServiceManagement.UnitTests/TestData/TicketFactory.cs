using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.UnitTests.TestData;

/// <summary>Builds domain tickets directly, with no database involved.</summary>
internal static class TicketFactory
{
    public static readonly DateTimeOffset DefaultReportedAt = new(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);

    public static Ticket Create(
        string ticketNumber = "TKT-000001",
        DateTimeOffset? reportedAt = null,
        Guid? requesterId = null)
        => new(
            ticketNumber,
            "Laptop will not boot",
            "The device shows a black screen after the last update.",
            serviceId: Guid.CreateVersion7(),
            categoryId: Guid.CreateVersion7(),
            priorityId: Guid.CreateVersion7(),
            requesterId: requesterId ?? Guid.CreateVersion7(),
            reportedAt: reportedAt ?? DefaultReportedAt);

    public static Ticket CreateWithSla(
        int firstResponseMinutes = 30,
        int resolutionMinutes = 240,
        int atRiskThresholdPercent = 20,
        DateTimeOffset? reportedAt = null)
    {
        var ticket = Create(reportedAt: reportedAt);
        ticket.ApplySlaPolicy(firstResponseMinutes, resolutionMinutes, atRiskThresholdPercent);
        return ticket;
    }
}
