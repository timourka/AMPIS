using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lab2.Web.Tests;

public class GatewayTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> factory;

    public GatewayTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Fact]
    public async Task Register_login_and_profile_go_to_s1_with_body_and_auth()
    {
        await using var s1 = await Upstream.Start();
        using var _ = UpstreamEnv.Set("S1", s1.Url);
        using var client = factory.CreateClient();

        s1.Status = 201;
        s1.Body = [];
        var payload = Encoding.UTF8.GetBytes("{\"login\":\"ann\",\"password\":\"secret\",\"name\":\"\u0410\u043d\u043d\u0430\"}");
        var register = await client.PostAsync("/api/register", new ByteArrayContent(payload) { Headers = { { "Content-Type", "application/json" } } });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var posted = s1.Hits.Single();
        Assert.Equal("POST", posted.Method);
        Assert.Equal("/api/register", posted.Path);
        Assert.StartsWith("application/json", posted.ContentType);
        var text = Encoding.UTF8.GetString(posted.Body);
        Assert.True(text.Contains("\u0410\u043d\u043d\u0430"), Convert.ToHexString(posted.Body));

        s1.Hits.Clear();
        s1.Status = 200;
        s1.ContentType = "application/json";
        s1.Body = "{\"token\":\"abc.def\"}"u8.ToArray();
        var login = await client.PostAsJsonAsync("/api/login", new { login = "ann", password = "secret" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("abc.def", await login.Content.ReadAsStringAsync());
        Assert.Equal("/api/login", s1.Hits.Single().Path);

        s1.Hits.Clear();
        s1.Status = 204;
        s1.Body = [];
        s1.ContentType = null;
        using var profile = new HttpRequestMessage(HttpMethod.Post, "/api/profile")
        {
            Content = JsonContent.Create(new { name = "Новое имя" })
        };
        profile.Headers.Authorization = new("Bearer", "abc.def");
        var renamed = await client.SendAsync(profile);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.Equal("Bearer abc.def", s1.Hits.Single().Authorization);
        Assert.Equal("/api/profile", s1.Hits.Single().Path);
    }

    [Fact]
    public async Task Upload_and_delete_go_to_s2()
    {
        await using var s2 = await Upstream.Start();
        using var _ = UpstreamEnv.Set("S2", s2.Url);
        using var client = factory.CreateClient();
        s2.Status = 201;
        s2.ContentType = "application/json";
        s2.Body = "{\"id\":\"6f1b7c3e-1a2b-4c3d-8e9f-001122334455\"}"u8.ToArray();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", "a.bin");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/upload") { Content = form };
        upload.Headers.Authorization = new("Bearer", "tok");
        var created = await client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var hit = s2.Hits.Single();
        Assert.Equal("/api/upload", hit.Path);
        Assert.Equal("Bearer tok", hit.Authorization);
        Assert.Contains("multipart/form-data", hit.ContentType);
        Assert.Contains(hit.Body, b => b == 1);

        s2.Hits.Clear();
        s2.Status = 204;
        s2.Body = [];
        s2.ContentType = null;
        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/files/6f1b7c3e-1a2b-4c3d-8e9f-001122334455");
        delete.Headers.Authorization = new("Bearer", "tok");
        var removed = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal("/api/files/6f1b7c3e-1a2b-4c3d-8e9f-001122334455", s2.Hits.Single().Path);
    }

    [Fact]
    public async Task Download_keeps_bytes_and_content_disposition()
    {
        await using var s3 = await Upstream.Start();
        using var _ = UpstreamEnv.Set("S3", s3.Url);
        using var client = factory.CreateClient();
        var payload = new byte[] { 0, 255, 10, 13 };
        s3.Status = 200;
        s3.ContentType = "application/octet-stream";
        s3.Disposition = "attachment; filename=\"a.bin\"; filename*=UTF-8''%D0%B0.bin";
        s3.Body = payload;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/download/6f1b7c3e-1a2b-4c3d-8e9f-001122334455");
        request.Headers.Authorization = new("Bearer", "tok");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(s3.Disposition, response.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal("/api/download/6f1b7c3e-1a2b-4c3d-8e9f-001122334455", s3.Hits.Single().Path);
        Assert.Equal("Bearer tok", s3.Hits.Single().Authorization);
    }

    [Fact]
    public async Task List_and_statistics_go_to_s4_with_query()
    {
        await using var s4 = await Upstream.Start();
        using var _ = UpstreamEnv.Set("S4", s4.Url);
        using var client = factory.CreateClient();
        s4.ContentType = "application/json";
        s4.Body = "{\"items\":[],\"page\":1,\"size\":10,\"total\":0}"u8.ToArray();
        var list = await client.GetAsync("/api/files?q=a&sort=name&dir=asc&page=1&size=10");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("/api/files?q=a&sort=name&dir=asc&page=1&size=10", s4.Hits.Single().Path);

        s4.Hits.Clear();
        s4.Body = "{\"total\":1,\"anonymous\":0,\"users\":[\"Анна\"]}"u8.ToArray();
        using var stats = new HttpRequestMessage(HttpMethod.Get, "/api/statistics/6f1b7c3e-1a2b-4c3d-8e9f-001122334455");
        stats.Headers.Authorization = new("Bearer", "tok");
        var report = await client.SendAsync(stats);
        Assert.Contains("\u0410\u043d\u043d\u0430", await report.Content.ReadAsStringAsync());
        Assert.Equal("/api/statistics/6f1b7c3e-1a2b-4c3d-8e9f-001122334455", s4.Hits.Single().Path);
    }

    [Fact]
    public async Task Dead_upstream_and_timeout_become_503()
    {
        using (UpstreamEnv.Set("S1", "http://127.0.0.1:1"))
        using (UpstreamEnv.Set("UPSTREAM_TIMEOUT", "2"))
        {
            using var client = factory.CreateClient();
            var dead = await client.PostAsJsonAsync("/api/login", new { login = "a", password = "b" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, dead.StatusCode);
        }

        await using var slow = await Upstream.Start();
        slow.Delay = TimeSpan.FromSeconds(5);
        using var env = UpstreamEnv.Set("S4", slow.Url);
        using var timeout = UpstreamEnv.Set("UPSTREAM_TIMEOUT", "1");
        using var client2 = factory.CreateClient();
        var late = await client2.GetAsync("/api/files");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, late.StatusCode);
    }
}

public sealed class PageTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly HttpClient client;

    public PageTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();

    [Theory]
    [InlineData("/", "Файлы")]
    [InlineData("/register", "Регистрация")]
    [InlineData("/login", "Вход")]
    [InlineData("/logout", "Выход")]
    [InlineData("/upload", "Загрузка")]
    [InlineData("/files", "Список")]
    [InlineData("/download", "Скачивание")]
    [InlineData("/delete", "Удаление")]
    [InlineData("/rename", "Имя")]
    [InlineData("/stats", "Статистика")]
    public async Task Page_is_html(string path, string title)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(title, html);
        Assert.DoesNotContain("innerHTML", html);
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org", "", StringComparison.Ordinal));
        Assert.DoesNotContain("—", html);
    }

    [Fact]
    public async Task Scripts_escape_untrusted_text_and_keep_token_out_of_the_url()
    {
        var api = await client.GetStringAsync("/api.js");
        var files = await client.GetStringAsync("/files.js");
        var upload = await client.GetStringAsync("/upload.js");
        var operations = await client.GetStringAsync("/operations.js");
        Assert.Contains("sessionStorage", api);
        Assert.Contains("textContent", files);
        Assert.Contains("textContent", operations);
        Assert.DoesNotContain("innerHTML", api + files + upload + operations);
        Assert.Contains("1048576", upload);
        Assert.Contains("keydown", upload);
        Assert.Contains("Bearer", api);
        Assert.DoesNotContain("token=", api + operations);
    }
}

sealed class Upstream : IAsyncDisposable
{
    readonly WebApplication app;
    public List<Hit> Hits { get; } = [];
    public int Status { get; set; } = 200;
    public byte[] Body { get; set; } = "{}"u8.ToArray();
    public string? ContentType { get; set; } = "application/json";
    public string? Disposition { get; set; }
    public TimeSpan Delay { get; set; }
    public string Url { get; private set; } = "";
    readonly Task Ready;

    Upstream()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = ["--urls", "http://127.0.0.1:0"] });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        app = builder.Build();
        app.Run(async ctx =>
        {
            using var buffer = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(buffer);
            Hits.Add(new Hit(ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString, ctx.Request.Headers.Authorization.ToString(), ctx.Request.ContentType, buffer.ToArray()));
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ctx.RequestAborted);
            ctx.Response.StatusCode = Status;
            if (ContentType is not null) ctx.Response.ContentType = ContentType;
            if (Disposition is not null) ctx.Response.Headers.ContentDisposition = Disposition;
            if (Body.Length > 0) await ctx.Response.Body.WriteAsync(Body);
        });
        Ready = app.StartAsync().ContinueWith(t => Url = app.Urls.Single(), TaskScheduler.Default);
    }

    public static async Task<Upstream> Start()
    {
        var upstream = new Upstream();
        await upstream.Ready;
        return upstream;
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
    }
}

sealed record Hit(string Method, string Path, string Authorization, string? ContentType, byte[] Body);

sealed class UpstreamEnv : IDisposable
{
    readonly string name;
    readonly string? previous;

    UpstreamEnv(string name, string? value)
    {
        this.name = name;
        previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public static UpstreamEnv Set(string name, string value) => new(name, value);

    public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
}
