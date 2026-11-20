using Lab2;
using Microsoft.EntityFrameworkCore;

const long MaxBytes = 1_048_576;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxBytes + 64 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = MaxBytes + 64 * 1024);
Shared.Start(builder, publish: true);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok());

app.MapGet("/api/my-files", async (HttpContext http) =>
{
    var owner = Shared.User(http);
    if (owner is null) return Results.Unauthorized();
    await using var db = new Data<StoredFile>();
    var files = await db.Rows.Where(f => f.OwnerId == owner.Value)
        .OrderBy(f => f.Name).ThenBy(f => f.Id).Select(f => new { f.Id, f.Name })
        .ToListAsync(http.RequestAborted);
    return Results.Ok(files);
});

app.MapPost("/api/upload", async (HttpContext http) =>
{
    var owner = Shared.User(http);
    if (owner is null) return Results.Unauthorized();
    if (!http.Request.HasFormContentType) return Results.BadRequest();

    IFormFile file;
    try { file = http.Request.Form.Files.GetFile("file") ?? throw new BadHttpRequestException("file"); }
    catch (BadHttpRequestException) { return Results.BadRequest(); }
    if (file.Length > MaxBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    var name = SafeName(file.FileName);
    if (name is null) return Results.BadRequest();

    await using var body = new MemoryStream();
    await file.CopyToAsync(body);
    if (body.Length > MaxBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    var id = Guid.NewGuid();
    await using var db = new Data<StoredFile>();
    db.Rows.Add(new StoredFile { Id = id, OwnerId = owner.Value, Name = name, Content = body.ToArray() });
    db.Enqueue(new LabEvent(Guid.NewGuid(), "file.uploaded", FileId: id, UserId: owner.Value, Name: name));
    await db.SaveChangesAsync();
    return Results.Created($"/api/files/{id}", new { id });
});

app.MapDelete("/api/files/{id:guid}", async (Guid id, HttpContext http) =>
{
    var owner = Shared.User(http);
    if (owner is null) return Results.Unauthorized();

    await using var db = new Data<StoredFile>();
    var fileOwner = await db.Rows.Where(f => f.Id == id).Select(f => (Guid?)f.OwnerId).SingleOrDefaultAsync();
    if (fileOwner is null) return Results.NotFound();
    if (fileOwner != owner) return Results.StatusCode(403);
    db.Rows.Remove(new StoredFile { Id = id });
    db.Enqueue(new LabEvent(Guid.NewGuid(), "file.deleted", FileId: id, UserId: owner.Value));
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateConcurrencyException) { return Results.NotFound(); }
    return Results.NoContent();
});

app.Run();

static string? SafeName(string raw)
{
    if (raw.IndexOfAny(['/', '\\']) >= 0) return null;
    if (raw is "" or "." or "..") return null;
    foreach (var c in raw)
        if (char.IsControl(c)) return null;
    return raw;
}

public partial class Program;
