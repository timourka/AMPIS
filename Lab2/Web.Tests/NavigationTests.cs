using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class NavigationTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly HttpClient client;
    public NavigationTests(WebApplicationFactory<Program> app) => client = app.CreateClient();

    [Fact]
    public async Task Main_page_marks_links_by_login_state()
    {
        var html = await client.GetStringAsync("/");
        Assert.Contains("href=\"/logout\" data-member hidden", html);
        Assert.Contains("href=\"/login\" data-guest hidden", html);
        Assert.Contains("href=\"/register\" data-guest hidden", html);
        Assert.Contains("Изменить своё имя", html);
    }

    [Fact]
    public async Task Main_page_links_to_independent_file_lists()
    {
        var html = await client.GetStringAsync("/");
        Assert.Contains("href=\"/my-files\" data-member hidden", html);
        Assert.Contains("href=\"/delete\" data-member hidden", html);
        Assert.Contains("href=\"/download\"", html);
    }

    [Theory]
    [InlineData("/my-files", "Мои файлы")]
    [InlineData("/delete", "Удаление")]
    [InlineData("/download", "Скачивание")]
    public async Task Operation_pages_offer_file_lists(string path, string title)
    {
        var html = await client.GetStringAsync(path);
        Assert.Contains($"<h1>{title}</h1>", html);
        Assert.Contains("<tbody>", html);
    }

    [Fact]
    public async Task File_list_has_an_actions_column()
    {
        var html = await client.GetStringAsync("/files");
        Assert.Contains("<th>Действия</th>", html);
    }

    [Fact]
    public async Task File_list_has_size_options_above_and_page_navigation_below()
    {
        var html = await client.GetStringAsync("/files");
        Assert.DoesNotContain("name=\"page\"", html);
        Assert.Matches("<select name=\"size\"><option>2</option><option>4</option><option>8</option></select>[\\s\\S]*</form>[\\s\\S]*</table>\\s*<nav[^>]*aria-label=\"Страницы\"", html);
    }
}
