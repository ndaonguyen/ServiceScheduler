# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Node is needed to build the React ClientApp during `dotnet publish`.
# (Harmless for the API-only variant, which has no ClientApp.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/Host/AppointmentScheduler.Api/AppointmentScheduler.Api.csproj
RUN dotnet publish src/Host/AppointmentScheduler.Api/AppointmentScheduler.Api.csproj \
    -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Run as the non-root 'app' user shipped in the runtime image.
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AppointmentScheduler.Api.dll"]

# Healthchecks handled by the orchestrator (e.g. ECS/k8s): liveness probe -> /health/live,
# readiness probe -> /health/ready (checks the database). /health is a liveness alias.
HEALTHCHECK NONE
