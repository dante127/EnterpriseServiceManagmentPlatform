using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The ticket-number generator reads a sequence through a raw ADO command
/// rather than EF, because EF wraps composed SQL in a derived table and SQL Server
/// rejects <c>NEXT VALUE FOR</c> there.
/// <para>
/// A raw command, however, does not automatically join a transaction that is
/// already open on the connection. Once handlers run inside a unit of work, the
/// generator has to enlist explicitly, or the sequence read fails with
/// "BeginExecuteReader requires the command to have a transaction".
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketNumberGeneratorTransactionTests
{
    private readonly DatabaseFixture _fixture;

    public TicketNumberGeneratorTransactionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task The_generator_enlists_in_an_open_transaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await using var context = _fixture.CreateContext();
        var generator = new TicketNumberGenerator(context);

        string? insideTransaction;

        // A transaction opened the same way UnitOfWork opens it: the generator has
        // to cope with any ambient transaction, not just this helper's.
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            insideTransaction = await generator.NextAsync();
            await transaction.CommitAsync();
        }

        insideTransaction.Should().StartWith("TKT-", "the raw command must run inside the ambient transaction");
    }

    [SkippableFact]
    public async Task The_generator_still_works_outside_any_transaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await using var context = _fixture.CreateContext();
        var generator = new TicketNumberGenerator(context);

        var number = await generator.NextAsync();

        number.Should().StartWith("TKT-");
    }

    [SkippableFact]
    public async Task Numbers_issued_inside_a_transaction_are_not_handed_out_twice_after_a_rollback()
    {
        // A sequence is gap-tolerant by design, so a rollback consuming a value is
        // acceptable. What must never happen is a *duplicate*, which is what
        // proves the read really happened inside the transaction rather than on a
        // separate connection outside it.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        string? firstAttempt = null;

        await using (var context = _fixture.CreateContext())
        {
            var transaction = await context.Database.BeginTransactionAsync();

            firstAttempt = await new TicketNumberGenerator(context).NextAsync();

            // Deliberately abandoned: the value is consumed but the row is not written.
            await transaction.RollbackAsync();
        }

        string? secondAttempt;
        await using (var context = _fixture.CreateContext())
        {
            secondAttempt = await new TicketNumberGenerator(context).NextAsync();
        }

        firstAttempt.Should().NotBeNull();
        secondAttempt.Should().NotBeNull();
        secondAttempt.Should().NotBe(firstAttempt, "the rolled-back read must not be reissued");
    }
}
