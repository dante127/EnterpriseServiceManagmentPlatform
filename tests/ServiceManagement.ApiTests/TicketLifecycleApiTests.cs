using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceManagement.Application.Auth;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.ApiTests.Infrastructure;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.ApiTests;

/// <summary>
/// Full HTTP lifecycle: login, create, assign, progress, resolve, close. Requires
/// the SQL Server container, so it is skipped unless
/// <see cref="DatabaseFixture.EnvironmentVariableName"/> is enabled.
/// </summary>
public class TicketLifecycleApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public TicketLifecycleApiTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_ticket_can_be_raised_assigned_progressed_and_closed_over_http()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetAsync();

        await using (var context = _fixture.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(context, new IdentityPasswordHasher(), DatabaseFixture.Clock);
        }

        var factory = new ApiFactory { DatabaseConnectionString = _fixture.ConnectionString };
        using var client = factory.CreateClient();

        var token = await LoginAsRequesterAsync(factory, client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        // 1. Catalog lookup (cache-aside read).
        var catalog = await client.GetStringAsync("/api/v1/catalog/services");
        var serviceId = JsonNode.Parse(catalog)!["data"]!.AsArray()[0]!["id"]!.GetValue<Guid>();

        var categories = await client.GetStringAsync("/api/v1/catalog/categories");
        var categoryId = JsonNode.Parse(categories)!["data"]!.AsArray()[0]!["id"]!.GetValue<Guid>();

        // 2. Create the ticket.
        var created = await client.PostAsJsonAsync("/api/v1/tickets", new
        {
            title = "Laptop will not boot",
            description = "Black screen after the vendor update.",
            serviceId,
            categoryId,
            idempotencyKey = "lifecycle-1"
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        var ticketId = ticket["data"]!["id"]!.GetValue<Guid>();

        // 3. The same idempotency key must not create a second ticket.
        var replay = await client.PostAsJsonAsync("/api/v1/tickets", new
        {
            title = "Laptop will not boot",
            description = "Black screen after the vendor update.",
            serviceId,
            categoryId,
            idempotencyKey = "lifecycle-1"
        });

        var replayTicket = JsonNode.Parse(await replay.Content.ReadAsStringAsync())!;
        replayTicket["data"]!["id"]!.GetValue<Guid>().Should().Be(ticketId);

        // 4. Assign it (as the manager).
        client.DefaultRequestHeaders.Authorization = new("Bearer", await LoginAsManagerAsync(factory, client));
        var assign = await client.PostAsJsonAsync($"/api/v1/tickets/{ticketId}/assign", new
        {
            teamId = await FirstTeamIdAsync(factory),
            reason = "First line owns hardware incidents."
        });

        assign.IsSuccessStatusCode.Should().BeTrue();

        // 5. Progress the workflow.
        var rowVersion = JsonNode.Parse(await (await client.GetAsync($"/api/v1/tickets/{ticketId}")).Content.ReadAsStringAsync())!
            ["data"]!["rowVersion"]!.ToString();

        foreach (var status in new[] { TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed })
        {
            var moved = await client.PostAsJsonAsync($"/api/v1/tickets/{ticketId}/status", new
            {
                status = status.ToString(),
                rowVersion = rowVersion
            });

            moved.IsSuccessStatusCode.Should().BeTrue();

            rowVersion = JsonNode.Parse(await moved.Content.ReadAsStringAsync())!["data"]!["rowVersion"]!.ToString();
        }

        // 6. An invalid transition must be rejected.
        var rejected = await client.PostAsJsonAsync($"/api/v1/tickets/{ticketId}/status", new
        {
            status = TicketStatus.InProgress.ToString(),
            rowVersion = rowVersion
        });

        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 7. The requester receives a notification about the flow.
        client.DefaultRequestHeaders.Authorization = new("Bearer", await LoginAsRequesterAsync(factory, client));
        var notifications = await client.GetStringAsync("/api/v1/notifications");
        JsonNode.Parse(notifications)!["data"]!.AsArray().Should().NotBeEmpty();

        factory.Dispose();
    }

    private static async Task<string> LoginAsync(
        ApiFactory factory,
        HttpClient client,
        string email,
        string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return payload["data"]!["accessToken"]!.GetValue<string>();
    }

    private static Task<string> LoginAsRequesterAsync(ApiFactory factory, HttpClient client)
        => LoginAsync(factory, client, DatabaseSeeder.RequesterEmail, DatabaseSeeder.RequesterPassword);

    private static Task<string> LoginAsManagerAsync(ApiFactory factory, HttpClient client)
        => LoginAsync(factory, client, DatabaseSeeder.ManagerEmail, DatabaseSeeder.ManagerPassword);

    private static async Task<Guid> FirstTeamIdAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Teams.Select(team => team.Id).FirstAsync();
    }
}
