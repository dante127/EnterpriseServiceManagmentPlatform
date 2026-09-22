using FluentAssertions;
using Mapster;
using ServiceManagement.Application.Common.Mappings;
using ServiceManagement.Application.Notifications;
using ServiceManagement.Application.Users;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Domain.Notifications;

namespace ServiceManagement.UnitTests.Mapping;

public class MappingConfigTests
{
    public MappingConfigTests()
    {
        // Mirrors the registration performed in Application/DependencyInjection.cs.
        TypeAdapterConfig.GlobalSettings.Scan(typeof(MappingConfig).Assembly);
    }

    [Fact]
    public void Notification_type_is_projected_as_a_readable_string()
    {
        var notification = new Notification(
            Guid.CreateVersion7(),
            NotificationType.SlaNearBreach,
            "SLA at risk",
            "Ticket TKT-000001 is approaching its SLA deadline.");

        var dto = notification.Adapt<NotificationDto>();

        dto.Type.Should().Be("SlaNearBreach");
        dto.Title.Should().Be("SLA at risk");
        dto.IsRead.Should().BeFalse();
        dto.ReadAt.Should().BeNull();
    }

    [Fact]
    public void User_summary_composes_the_full_name()
    {
        var user = new User(
            "ada@example.com",
            "ada",
            "Ada",
            "Lovelace",
            "hash");

        var dto = user.Adapt<UserSummaryDto>();

        dto.FullName.Should().Be("Ada Lovelace");
        dto.Email.Should().Be("ada@example.com");
        dto.IsActive.Should().BeTrue();
    }
}
