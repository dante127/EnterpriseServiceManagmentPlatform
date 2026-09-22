using FluentAssertions;
using ServiceManagement.Application.Auth;
using ServiceManagement.Application.Common.Security;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.UnitTests.Validation;

public class ValidatorTests
{
    [Theory]
    [InlineData("", "Password1!")]
    [InlineData("not-an-email", "Password1!")]
    [InlineData("user@example.com", "")]
    public void Login_requires_a_valid_email_and_a_password(string email, string password)
    {
        var result = new LoginCommandValidator().Validate(new LoginCommand(email, password));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Login_accepts_well_formed_credentials()
    {
        var result = new LoginCommandValidator()
            .Validate(new LoginCommand("person@example.com", "anything"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_ticket_requires_title_description_service_and_category()
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
    public void Create_ticket_accepts_a_minimal_valid_payload()
    {
        var result = new CreateTicketCommandValidator().Validate(new CreateTicketCommand(
            "Printer jam",
            "The printer reports a paper jam that cannot be cleared.",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            null,
            null,
            null));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("short1!A")]
    [InlineData("alllowercase123!")]
    [InlineData("ALLUPPERCASE123!")]
    [InlineData("NoDigitsOrSymbols")]
    [InlineData("MissingSymbol123456")]
    public void Change_password_enforces_the_password_policy(string newPassword)
    {
        var result = new ChangePasswordCommandValidator()
            .Validate(new ChangePasswordCommand("Current#Password1", newPassword));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Change_password_accepts_a_strong_new_password()
    {
        var result = new ChangePasswordCommandValidator()
            .Validate(new ChangePasswordCommand("Current#Password1", "Brand#NewPassword2026"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Change_password_rejects_reusing_the_current_password()
    {
        var result = new ChangePasswordCommandValidator()
            .Validate(new ChangePasswordCommand("Same#Password123", "Same#Password123"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Assign_ticket_requires_an_assignee_or_a_team()
    {
        var result = new AssignTicketCommandValidator()
            .Validate(new AssignTicketCommand(Guid.CreateVersion7(), null, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.ErrorMessage.Contains("assignee"));
    }
}

public class PermissionEvaluatorTests
{
    [Fact]
    public void Permission_comparison_is_case_insensitive()
    {
        string[] granted = ["Tickets.Read", Permissions.TicketsAssign];

        PermissionEvaluator.HasPermission(granted, Permissions.TicketsRead).Should().BeTrue();
        PermissionEvaluator.HasPermission(granted, Permissions.TicketsAssign).Should().BeTrue();
    }

    [Fact]
    public void Missing_permission_is_not_granted()
    {
        string[] granted = [Permissions.TicketsRead];

        PermissionEvaluator.HasPermission(granted, Permissions.AuditRead).Should().BeFalse();
    }

    [Fact]
    public void Empty_grant_set_never_satisfies_a_requirement()
    {
        PermissionEvaluator.HasPermission([], Permissions.TicketsRead).Should().BeFalse();
    }

    [Fact]
    public void Has_all_permissions_requires_every_entry()
    {
        string[] granted = [Permissions.TicketsRead, Permissions.TicketsAssign];

        PermissionEvaluator
            .HasAllPermissions(granted, [Permissions.TicketsRead, Permissions.TicketsAssign])
            .Should()
            .BeTrue();

        PermissionEvaluator
            .HasAllPermissions(granted, [Permissions.TicketsRead, Permissions.AuditRead])
            .Should()
            .BeFalse();
    }
}
