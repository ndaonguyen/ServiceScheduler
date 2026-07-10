using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using AppointmentScheduler.Booking.Api.Endpoints;
using AppointmentScheduler.Api.Endpoints;
using AppointmentScheduler.Api.Security;
using AppointmentScheduler.BuildingBlocks.Abstractions;
using AppointmentScheduler.BuildingBlocks.Messaging;
using AppointmentScheduler.Api;
using AppointmentScheduler.BuildingBlocks.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Logging: stamp every log with the current trace/span id so logs correlate with OTel traces.
// Development keeps the human-readable console; other environments emit structured JSON for
// log aggregators.
builder.Logging.Configure(options =>
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(HostModules.Configurations);
builder.Services.AddMessaging();
builder.Services.AddCatalogModule();
builder.Services.AddFleetModule();
builder.Services.AddWorkforceModule();
builder.Services.AddBookingModule();
builder.Services.AddPersistence(builder.Configuration);

// Self-hosted user store: ASP.NET Core Identity core services (UserManager / RoleManager /
// password hashing) over our Postgres. AddIdentityCore registers no auth scheme — JWT (below)
// owns authentication. AddRoles enables RBAC.
builder.Services.AddAuthorization();
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        // Account lockout (brute-force mitigation): lock for 5 minutes after 5 failed logins.
        // /api/auth/login enforces this via UserManager's lockout methods.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// JWT bearer authentication. Settings come from the "Jwt" config section (signing key from a
// secret / env var in production). TokenService issues the tokens; /api/auth/login hands them out.
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names verbatim (notably "sub"); we emit standard ClaimTypes URIs already.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            // Pin the signing algorithm — never trust the token's own `alg` (algorithm-confusion
            // defense). Only HS256 tokens are accepted.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            // Tolerate ±2 min of clock drift between instances (default is 5 min).
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        // The access token rides in an httpOnly cookie, not the Authorization header — pull it from
        // there before validation. Falls back to the header if the cookie is absent.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthCookies.AccessCookieName, out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

// Surface the caller to the Application layer via ICurrentUser.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Health checks: liveness has no dependencies; readiness pings the database via EF Core
// (CanConnect). Probes hit /health/live and /health/ready (see endpoint mapping below).
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

// OpenTelemetry: traces + metrics for ASP.NET Core, HttpClient, and the runtime, exported via
// OTLP. The exporter honors the standard OTEL_EXPORTER_OTLP_* env vars (endpoint defaults to
// http://localhost:4317); point it at your collector in each environment.
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: builder.Environment.ApplicationName,
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

builder.Services.AddOpenApi();

var app = builder.Build();

// Apply EF Core migrations + seed RBAC roles (and an optional dev admin) in Development.
// In production run `dotnet ef database update` as a deliberate deploy step instead.
if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateAndSeedAsync();
}

// Liveness: process is up (no dependency checks). Readiness: dependencies (DB) reachable.
// /health is kept as a liveness alias for back-compat with existing probes.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

// OpenAPI document at /openapi/v1.json (used by the ClientApp's nswag client generator).
app.MapOpenApi();


app.UseAuthentication();
app.UseAuthorization();

// Slice endpoints own their route group, tags, and auth policy internally (see each
// Map*Endpoints). Auth: /api/auth/* (httpOnly-cookie tokens). Profile: /api/profile/me.
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapBookingEndpoints();

app.MapGet("/", () => Results.Redirect("/openapi/v1.json"));


app.Logger.LogInformation("Starting a new instance of the app");
app.Run();

// Exposed so integration tests can reference the entry point via WebApplicationFactory.
public partial class Program;
