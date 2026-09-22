using Mapster;
using ServiceManagement.Application.Notifications;
using ServiceManagement.Application.Users;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Domain.Notifications;

namespace ServiceManagement.Application.Common.Mappings;

/// <summary>
/// Mapster registrations. Only shapes that would otherwise be written by hand
/// more than once are configured here; EF projections stay explicit so the
/// generated SQL remains predictable.
/// </summary>
public sealed class MappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<User, UserSummaryDto>()
            .Map(destination => destination.FullName, source => source.FirstName + " " + source.LastName);

        config.NewConfig<Notification, NotificationDto>()
            .Map(destination => destination.Type, source => source.Type.ToString());
    }
}
