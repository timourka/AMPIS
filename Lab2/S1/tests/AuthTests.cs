using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace S1.Tests;

public class AuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> app;
    public AuthTests(WebApplicationFactory<Program> app)
    {
        Environment.SetEnvironmentVariable("TOKEN_KEY", "lab2-test-signing-key-32-bytes!!");
        this.app = app;
    }
    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Profile_without_token_is_unauthorized()
    {
        var res = await app.CreateClient().PostAsJsonAsync("/api/profile", new { name = "Ada" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_blank_login()
    {
        var res = await app.CreateClient().PostAsJsonAsync("/api/register", new { login = " ", password = "secret", name = "Ada" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Login_rejects_blank_password()
    {
        var res = await app.CreateClient().PostAsJsonAsync("/api/login", new { login = "ada", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
