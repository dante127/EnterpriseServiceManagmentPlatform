using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Configuration;
using ServiceManagement.Api.ErrorHandling;
using ServiceManagement.Api.HealthChecks;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.Api.Extensions;

/// <summary>Everything the HTTP layer needs: routing, versioning, docs, auth, limits and probes.</summary>
public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "Default";

    public static IServiceCollection AddApiLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Authentication, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = 10,
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
