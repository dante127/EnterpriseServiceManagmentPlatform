using System.Reflection;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ServiceManagement.Application.Common.Behaviors;

namespace ServiceManagement.Application;

/// <summary>Registers the application layer: handlers, validators, mapping and pipeline behaviours.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // Order matters: validation must run before logging so rejected
            // requests are not logged as handled work.
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        TypeAdapterConfig.GlobalSettings.Scan(assembly);
        services.AddMapster();

        return services;
    }
}
