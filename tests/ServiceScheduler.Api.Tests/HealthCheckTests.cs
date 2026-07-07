using System.Net;
using AwesomeAssertions;
using Xunit;

namespace ServiceScheduler.Api.Tests;

public class HealthCheckTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoint_returns_ok(string path)
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
