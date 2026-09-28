using System.Text;
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
using ServiceManagement.Infrastructure.Diagnostics;
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
        // Strongly typed options, validated at startup. Without ValidateOnStart a
        // blank JWT signing key is accepted here and only fails on the first
        // authenticated request, long after the container has reported itself healthy.
        services.AddValidatedOptions<JwtOptions>(configuration, JwtOptions.SectionName);
        services.AddValidatedOptions<AuthOptions>(configuration, AuthOptions.SectionName);
        services.AddValidatedOptions<RedisOptions>(configuration, RedisOptions.SectionName);
        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName);
        services.AddValidatedOptions<SlaOptions>(configuration, SlaOptions.SectionName);
        services.AddValidatedOptions<BackgroundJobOptions>(configuration, BackgroundJobOptions.SectionName);
        services.AddValidatedOptions<NotificationsOptions>(configuration, NotificationsOptions.SectionName);
        services.AddValidatedOptions<ProxyOptions>(configuration, ProxyOptions.SectionName);
    }

    /// <summary>
    /// Binds one options class to its configuration section and validates it on start.
    /// Exposed to the test assembly so the validation rules themselves are covered.
    /// </summary>
    internal static void AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
        => services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

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

        // JwtBearer is configured from the options pattern rather than by reading
        // configuration here. Reading it eagerly captured the values present at
        // registration time, so any later override (test hosts, layered config)
        // left the validator with the wrong issuer/audience/key and rejected every
        // token.
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.SaveToken = false;
                bearer.RequireHttpsMetadata = true;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = string.IsNullOrWhiteSpace(jwt.Value.SigningKey)
                        ? null
                        : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                    ValidateLifetime = true,
                    // Tokens are short lived; a small skew keeps clock drift from rejecting valid ones.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // 401/403 responses use the same envelope as every other endpoint.
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        return ApiResponseWriter.WriteFailureAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "Authentication is required to access this resource.",
                            context.HttpContext.RequestAborted);
                    },
                    OnForbidden = context => ApiResponseWriter.WriteFailureAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "You do not have permission to perform this operation.",
                        context.HttpContext.RequestAborted)
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
        // The workers own only the schedule and the cross-replica lock; each tick
        // resolves one of these from a fresh scope. Registering them as ordinary
        // scoped services is what makes the monitoring and retention logic
        // testable without standing up a hosted service.
        services.AddScoped<SlaCycle>();
        services.AddScoped<MaintenanceCycle>();

        services.AddHostedService<SlaMonitorWorker>();
        services.AddHostedService<MaintenanceWorker>();
    }
}
