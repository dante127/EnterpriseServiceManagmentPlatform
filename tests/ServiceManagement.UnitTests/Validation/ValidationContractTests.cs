using FluentAssertions;
using ServiceManagement.Application.Auth;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Application.Users;
using ServiceManagement.Domain.Constants;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.UnitTests.Validation;

/// <summary>
/// Guards the shape of the validation contract itself: field names must be
/// camelCase so they line up with the JSON properties clients send.
/// </summary>
public class ValidationContractTests
{
    [Fact]
    public void Validation_failures_use_camel_case_field_names()
    {
        var result = new CreateTicketCommandValidator().Validate(new CreateTicketCommand(
            Title: string.Empty,
            Description: string.Empty,
            ServiceId: Guid.Empty,
            CategoryId: Guid.Empty,
            PriorityId: null,
            RequesterId: null,
            AssignedTeamId: null,
            IdempotencyKey: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(failure => failure.PropertyName)
            .Should()
            .Contain(["title", "description", "serviceId", "categoryId"]);
    }

    [Fact]
    public void Login_validation_reports_the_email_field()
    {
        var result = new LoginCommandValidator().Validate(new LoginCommand("not-an-email", "secret"));

        result.Errors.Select(failure => failure.PropertyName).Should().Contain("email");
    }

    [Fact]
    public void Pagination_clamps_page_size_to_a_safe_range()
    {
        new PaginationRequest { PageSize = 5_000 }.PageSize.Should().Be(PaginationRequest.MaxPageSize);
        new PaginationRequest { PageSize = 0 }.PageSize.Should().Be(1);
        new PaginationRequest { PageSize = 25 }.PageSize.Should().Be(25);
    }

    [Fact]
    public void Pagination_computes_the_offset_from_the_page_number()
    {
        var request = new PaginationRequest { Page = 3, PageSize = 20 };

        request.Skip.Should().Be(40);
    }

    [Fact]
    public void Paged_result_reports_navigation_flags()
    {
        var firstPage = new PagedResult<string>(["a", "b"], page: 1, pageSize: 2, totalCount: 5);

        firstPage.TotalPages.Should().Be(3);
        firstPage.HasNextPage.Should().BeTrue();
        firstPage.HasPreviousPage.Should().BeFalse();

        var lastPage = new PagedResult<string>(["e"], page: 3, pageSize: 2, totalCount: 5);

        lastPage.HasNextPage.Should().BeFalse();
        lastPage.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Search_query_defaults_are_stable()
    {
        var query = new SearchTicketsQuery(
            null, null, null, null, null, null, null, null, null, null, null, null);

        query.Page.Should().Be(1);
        query.PageSize.Should().Be(PaginationRequest.DefaultPageSize);
        query.SortDescending.Should().BeTrue();
    }

    [Fact]
    public void Create_user_command_enforces_the_password_policy_and_role_requirement()
    {
        var result = new CreateUserCommandValidator().Validate(new CreateUserCommand(
            "person@example.com",
            "person",
            "Ada",
            "Lovelace",
            "weak",
            null,
            null,
            []));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(failure => failure.PropertyName)
            .Should()
            .Contain(["password", "roles"]);
    }
}
