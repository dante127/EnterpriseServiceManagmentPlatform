using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The ticket-number generator talks to a real SQL Server sequence, which cannot
/// be faked. The previous implementation composed the call through
/// <c>SqlQueryRaw</c>, and EF wraps that SQL in a derived table - a construct
/// SQL Server rejects for <c>NEXT VALUE FOR</c>, so every ticket creation failed
/// against a real database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketNumberGeneratorTests
{
    private readonly DatabaseFixture _fixture;

    public TicketNumberGeneratorTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Numbers_are_handed_out_in_strictly_increasing_order()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await using var context = _fixture.CreateContext();
        var generator = new TicketNumberGenerator(context);

        var first = await generator.NextAsync();
        var second = await generator.NextAsync();
        var third = await generator.NextAsync();

        var values = new[] { first, second, third }
            .Select(number => long.Parse(number["TKT-".Length..], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        values.Should().BeInAscendingOrder();
        values.Should().OnlyHaveUniqueItems();
    }

    [SkippableFact]
    public async Task Numbers_use_the_expected_zero_padded_shape()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await using var context = _fixture.CreateContext();
        var generator = new TicketNumberGenerator(context);

        var number = await generator.NextAsync();

        number.Should().StartWith("TKT-");
        number.Should().HaveLength(10);
        number["TKT-".Length..].Should().MatchRegex("^[0-9]{6}$");
    }

    [SkippableFact]
    public async Task Concurrent_callers_all_receive_distinct_numbers()
    {
        // The sequence must stay atomic under parallel load; a locking counter
        // table would be the usual alternative, and this proves it is unnecessary.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        var numbers = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await new TicketNumberGenerator(context).NextAsync();
        }));

        numbers.Should().OnlyHaveUniqueItems();
    }
}
