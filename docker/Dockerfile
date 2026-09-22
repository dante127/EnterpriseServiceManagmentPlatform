# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage: restore, compile and publish the API.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so package restore is cached independently of source edits.
COPY Directory.Build.props ./
COPY src/ServiceManagement.Domain/ServiceManagement.Domain.csproj src/ServiceManagement.Domain/
COPY src/ServiceManagement.Application/ServiceManagement.Application.csproj src/ServiceManagement.Application/
COPY src/ServiceManagement.Infrastructure/ServiceManagement.Infrastructure.csproj src/ServiceManagement.Infrastructure/
COPY src/ServiceManagement.Api/ServiceManagement.Api.csproj src/ServiceManagement.Api/

RUN dotnet restore src/ServiceManagement.Api/ServiceManagement.Api.csproj

# Now copy the sources and publish a release build.
COPY src/ src/
RUN dotnet publish src/ServiceManagement.Api/ServiceManagement.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Runtime stage: minimal ASP.NET Core image, non-root user.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

COPY --from=build /app/publish .

# The base image ships an unprivileged 'app' user.
USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "ServiceManagement.Api.dll"]
