using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Configuration;
using ServiceManagement.Api.ErrorHandling;
using ServiceManagement.Api.HealthChecks;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Infrastructure.Diagnostics;

namespace ServiceManagement.Api.Extensions;

/// <summary>Everything the HTTP layer needs: routing, versioning, docs, auth, limits and probes.</summary>
public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "Default";

    public static IServiceCollection AddApiLayer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                // Enums travel as readable strings ("InProgress"), not magic numbers.
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        services.AddEndpointsApiExplorer();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddObservability(configuration);

        // Environment-aware secret rules live in the composition root: the
        // DataAnnotations validator in Application cannot know the environment.
        services.AddSingleton<IValidateOptions<JwtOptions>>(
            new ProductionSigningKeyValidator(environment));

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
            options.ReportApiVersions = true;
            // Every route carries an explicit version segment, so a default is not needed.
            options.AssumeDefaultVersionWhenUnspecified = false;
            options.ApiVersionReader = new Asp.Versioning.UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(options =>
        {
            // Group Swagger operations by version, e.g. "v1".
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        })
        .AddMvc();

        services.AddSwaggerLayer();

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var rateLimits = configuration
            .GetSection(RateLimitingOptions.SectionName)
            .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // A rejected request still answers with the shared envelope, so
            // clients do not have to special-case throttling.
            options.OnRejected = static (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return new ValueTask(ApiResponseWriter.WriteFailureAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests. Slow down and retry shortly.",
                    cancellationToken));
            };

            options.AddPolicy(RateLimitPolicies.Authentication, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(rateLimits.AuthenticationWindowSeconds),
                        PermitLimit = rateLimits.AuthenticationPermitLimit,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            // The login limit alone leaves every other endpoint - including the
            // write endpoints - unbounded; the global partition bounds them all.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(rateLimits.GlobalWindowSeconds),
                        PermitLimit = rateLimits.GlobalPermitLimit,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (allowedOrigins.Length == 0)
                {
                    // Fail closed: no browser origin is allowed unless it is configured.
                    policy.WithOrigins(Array.Empty<string>())
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                    return;
                }

                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services
            .AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("sql-server", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        return services;
    }

    private static IServiceCollection AddSwaggerLayer(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Enterprise Service Management Platform API",
                Version = "v1",
                Description =
                    "Backend for organizational service management: identity, service catalog, ticket "
                    + "lifecycle, SLA tracking, notifications and audit logging.",
                Contact = new OpenApiContact
                {
                    Name = "Service Management Platform"
                },
                License = new OpenApiLicense
                {
                    Name = "MIT"
                }
            });

            var xmlDocumentation = Path.Combine(
                AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

            if (File.Exists(xmlDocumentation))
            {
                options.IncludeXmlComments(xmlDocumentation, includeControllerXmlComments: true);
            }

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the accessToken returned by POST /api/v1/auth/login."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });

        return services;
    }
}
