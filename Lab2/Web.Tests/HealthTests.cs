using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Lab2.Web.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> factory;

    public HealthTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Fact]
    public async Task Health_is_local_and_does_not_call_services()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }
}
