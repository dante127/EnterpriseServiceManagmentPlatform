using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ServiceManagement.Application;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Infrastructure.Auditing;
using ServiceManagement.Infrastructure.BackgroundJobs;
using ServiceManagement.Infrastructure.Caching;
using ServiceManagement.Infrastructure.Notifications;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Interceptors;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.Infrastructure.Time;

namespace ServiceManagement.Infrastructure;

/// <summary>Composition root for persistence, security, caching and background processing.</summary>
public static class DependencyInjection
{
    public const string DatabaseConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddApplication();

        services.BindOptions(configuration);
        services.AddPersistence(configuration);
        services.AddCaching(configuration);
        services.AddSecurity(configuration);
        services.AddMessaging();
        services.AddBackgroundProcessing();

        return services;
    }

    private static void BindOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Strongly typed options: configuration keys are bound exactly once.
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<SlaOptions>(configuration.GetSection(SlaOptions.SectionName));
        services.Configure<BackgroundJobOptions>(configuration.GetSection(BackgroundJobOptions.SectionName));
        services.Configure<NotificationsOptions>(configuration.GetSection(NotificationsOptions.SectionName));
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            var connectionString = configuration.GetConnectionString(DatabaseConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{DatabaseConnectionStringName}' is not configured. " +
                    "Set ConnectionStrings__Default (environment variable) or user secrets.");
            }

            options.UseSqlServer(connectionString, sqlServer =>
            {
                // Transient SQL faults are retried with backoff instead of failing the request.
                sqlServer.EnableRetryOnFailure(
                    databaseOptions.MaxRetryCount,
                    TimeSpan.FromSeconds(databaseOptions.MaxRetryDelaySeconds),
                    errorNumbersToAdd: null);

                sqlServer.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
            });

            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());

            if (databaseOptions.EnableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();
    }

    private static void AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();

        if (redisOptions.Enabled && !string.IsNullOrWhiteSpace(redisOptions.Configuration))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisOptions.Configuration;
                options.InstanceName = "servicemanagement:";
            });
        }
        else
        {
            // Without Redis the API still works: the cache degrades to per-process memory.
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, DistributedCacheService>();
    }

    private static void AddSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.SaveToken = false;
                options.RequireHttpsMetadata = true;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
                        ? null
                        : new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    // Tokens are short lived; a small skew keeps clock drift from rejecting valid ones.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // 401/403 responses use the same envelope as every other endpoint.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";

                        await context.Response.WriteAsJsonAsync(
                            new
                            {
                                success = false,
                                data = (object?)null,
                                message = "Authentication is required to access this resource.",
                                errors = Array.Empty<object>()
                            },
                            context.HttpContext.RequestAborted);
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";

                        await context.Response.WriteAsJsonAsync(
                            new
                            {
                                success = false,
                                data = (object?)null,
                                message = "You do not have permission to perform this operation.",
                                errors = Array.Empty<object>()
                            },
                            context.HttpContext.RequestAborted);
                    }
                };
            });

        services.AddAuthorization();
    }

    private static void AddMessaging(this IServiceCollection services)
    {
        services.AddScoped<INotificationService, DatabaseNotificationService>();
        services.AddScoped<IEmailSender, LoggingEmailSender>();
        services.AddScoped<IAuditService, AuditService>();
    }

    private static void AddBackgroundProcessing(this IServiceCollection services)
    {
        services.AddHostedService<SlaMonitorWorker>();
        services.AddHostedService<MaintenanceWorker>();
    }
}
