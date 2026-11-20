using Lab2;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
Shared.Start(builder, publish: true);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok());

app.MapGet("/api/downloads", async (HttpContext http) =>
{
    await using var db = new Data<StoredFile>(Environment.GetEnvironmentVariable("FILES_DB"));
    var files = await db.Rows.OrderBy(f => f.Name).ThenBy(f => f.Id)
        .Select(f => new { f.Id, f.Name }).ToListAsync(http.RequestAborted);
    return Results.Ok(files);
});

app.MapGet("/api/download/{id:guid}", async (Guid id, HttpContext http) =>
{
    await using var files = new Data<StoredFile>(Environment.GetEnvironmentVariable("FILES_DB"));
    var file = await files.Rows.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
    if (file is null) return Results.NotFound();

    var user = Shared.User(http);
    await using var db = new Data<Download>();
    db.Rows.Add(new Download { Id = Guid.NewGuid(), FileId = id, UserId = user });
    db.Enqueue(new LabEvent(Guid.NewGuid(), "file.downloaded", FileId: id, UserId: user ?? default));
    await db.SaveChangesAsync();

    http.Response.Headers.XContentTypeOptions = "nosniff";
    return Results.File(file.Content, "application/octet-stream", file.Name);
});

app.Run();

public partial class Program;
