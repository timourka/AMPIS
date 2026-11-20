using System.Net.Sockets;

const long MaxRequestBytes = 1_048_576 + 64 * 1024;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxRequestBytes);
builder.Services.AddHttpClient("upstream")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(2) });
var app = builder.Build();
var www = Path.Combine(app.Environment.ContentRootPath, "wwwroot");

app.MapGet("/health", () => Results.Text("ok"));
app.MapGet("/", () => Results.File(Path.Combine(www, "index.html"), "text/html; charset=utf-8"));
foreach (var page in new[] { "register", "login", "logout", "upload", "files", "my-files", "download", "delete", "rename", "stats" })
{
    var file = page;
    app.MapGet("/" + file, () => Results.File(Path.Combine(www, file + ".html"), "text/html; charset=utf-8"));
}
app.MapGet("/site.css", () => Results.File(Path.Combine(www, "site.css"), "text/css; charset=utf-8"));
foreach (var script in new[] { "api", "files", "upload", "operations" })
{
    var file = script;
    app.MapGet("/" + file + ".js", () => Results.File(Path.Combine(www, file + ".js"), "text/javascript; charset=utf-8"));
}
app.MapMethods("/api/{**rest}", ["GET", "POST", "DELETE"], Proxy);
app.Run();

async Task Proxy(HttpContext context)
{
    var path = context.Request.Path.Value ?? "";
    var target = path switch
    {
        "/api/register" or "/api/login" or "/api/profile" => Upstream("S1"),
        "/api/upload" or "/api/my-files" => Upstream("S2"),
        "/api/downloads" => Upstream("S3"),
        "/api/files" => Upstream("S4"),
        _ when path.StartsWith("/api/files/") => Upstream("S2"),
        _ when path.StartsWith("/api/download/") => Upstream("S3"),
        _ when path.StartsWith("/api/statistics/") => Upstream("S4"),
        _ => null
    };
    if (target is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    var timeout = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("UPSTREAM_TIMEOUT"), out var seconds) ? seconds : 10);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    cts.CancelAfter(timeout);
    try
    {
        var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("upstream");
        using var message = new HttpRequestMessage(new HttpMethod(context.Request.Method), target + path + context.Request.QueryString);
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, cts.Token);
        var bytes = buffer.ToArray();
        if (bytes.Length > 0)
        {
            message.Content = new ByteArrayContent(bytes);
            if (context.Request.ContentType is not null)
                message.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }
        if (context.Request.Headers.Authorization.Count > 0)
            message.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToString());
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        context.Response.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType is { } type) context.Response.ContentType = type.ToString();
        if (response.Content.Headers.ContentDisposition is { } disposition)
            context.Response.Headers.ContentDisposition = disposition.ToString();
        if (response.Headers.TryGetValues("X-Content-Type-Options", out var options))
            context.Response.Headers.XContentTypeOptions = options.ToArray();
        await response.Content.CopyToAsync(context.Response.Body, cts.Token);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or SocketException)
    {
        if (context.RequestAborted.IsCancellationRequested) return;
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    }
}

static string Upstream(string name) => (Environment.GetEnvironmentVariable(name) ?? name switch
{
    "S1" => "http://s1:8080",
    "S2" => "http://s2:8080",
    "S3" => "http://s3:8080",
    _ => "http://s4:8080"
}).TrimEnd('/');

public partial class Program;
