using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AppointmentScheduler.BuildingBlocks.Persistence;

namespace AppointmentScheduler.Api.Tests;

/// <summary>
/// Boots the API for integration tests but swaps the PostgreSQL-backed <see cref="AppDbContext"/>
/// for the EF Core in-memory provider, so tests need no running database. Each factory instance
/// gets an isolated store (unique database name).
/// </summary>
internal class TestWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"tests-{Guid.NewGuid()}";

    /// <summary>
    /// When <c>true</c> (default), the header-driven test auth scheme replaces JWT so endpoint
    /// tests need no real login. Set <c>false</c> to exercise the real JWT pipeline end-to-end.
    /// </summary>
    protected virtual bool UseTestAuthentication => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Pin ContentRoot to the Api project directory. WAF's built-in fallback tries
        // <solution-root>/<assembly-name>/, which under our src/Host/ layout resolves to a
        // non-existent path and throws DirectoryNotFoundException whenever the MSBuild-emitted
        // WebApplicationFactoryContentRootAttribute isn't present in the test assembly.
        var apiAssemblyDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        var apiProjectDir = Path.GetFullPath(Path.Combine(apiAssemblyDir, "..", "..", ".."));
        builder.UseContentRoot(apiProjectDir);

        builder.UseEnvironment("Testing");

        // AddInfrastructure requires a non-empty ConnectionStrings:AppDb at host-build time.
        // The value is irrelevant — we replace the Npgsql DbContext with in-memory below — but
        // it must be present so the guard does not throw.
        builder.UseSetting("ConnectionStrings:AppDb", "Host=in-memory;Database=tests");

        builder.ConfigureServices(services =>
        {
            // Drop the Npgsql-backed registrations so the in-memory provider we add next does
            // not collide with "more than one provider configured". Covers the context and its
            // options, plus the EF 9+ options-configuration descriptor (matched by reflection so
            // we don't have to name an EF-version-specific type).
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();

            var leftover = services
                .Where(d =>
                    d.ServiceType.IsGenericType &&
                    d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext)))
                .ToList();
            foreach (var descriptor in leftover)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            if (!UseTestAuthentication)
            {
                return; // keep the real JWT scheme registered by Program
            }

            // Register a header-driven test scheme as the default (no real login / JWT needed).
            // Override ALL default schemes so this is what RequireAuthorization authenticates with.
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>Creates a client authenticated as <paramref name="userId"/> with the given roles.</summary>
    public HttpClient CreateClientAs(string userId = "test-user", params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }
}
