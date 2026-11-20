using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class FlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly HttpClient client;
    public FlowTests(WebApplicationFactory<Program> app) => client = app.CreateClient();

    [Fact]
    public async Task Choosing_a_file_does_not_submit_the_upload_form()
    {
        var js = await client.GetStringAsync("/upload.js");
        Assert.DoesNotContain("requestSubmit()", js);
    }

    [Theory]
    [InlineData("/register")]
    [InlineData("/login")]
    public async Task Successful_auth_form_returns_to_the_home_page(string path)
    {
        var html = await client.GetStringAsync(path);
        Assert.Contains("location.replace(\"/\")", html);
    }

    [Fact]
    public async Task Statistics_does_not_require_a_show_button()
    {
        var html = await client.GetStringAsync("/stats");
        Assert.DoesNotContain("<button", html);
    }
}
