using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace S2.Tests;

public class UploadTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> app;
    public UploadTests(WebApplicationFactory<Program> app)
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
    public async Task Upload_without_token_is_unauthorized()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", "a.txt");
        var res = await app.CreateClient().PostAsync("/api/upload", form);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_path_in_name()
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Lab2.Shared.Token(Guid.NewGuid()));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1]), "file", "../a.txt");
        var res = await client.PostAsync("/api/upload", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
