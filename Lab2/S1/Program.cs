using System.Security.Cryptography;
using Lab2;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNameCaseInsensitive = true);
Shared.Start(builder, publish: true);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok());

app.MapPost("/api/register", async (Cred body) =>
{
    var login = body.Login?.Trim();
    var password = body.Password;
    var name = body.Name?.Trim();
    if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(name))
        return Results.BadRequest();

    var id = Guid.NewGuid();
    await using var db = new Data<User>();
    db.Rows.Add(new User { Id = id, Login = login, Name = name, PasswordHash = PasswordHasher.Hash(password) });
    db.Enqueue(new LabEvent(Guid.NewGuid(), "user.registered", UserId: id, Name: name));
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        return Results.Conflict();
    }
    return Results.StatusCode(StatusCodes.Status201Created);
});

app.MapPost("/api/login", async (LoginBody body) =>
{
    var login = body.Login?.Trim();
    if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(body.Password))
        return Results.BadRequest();

    await using var db = new Data<User>();
    var account = await db.Rows.AsNoTracking().SingleOrDefaultAsync(u => u.Login == login);
    if (account is null || !PasswordHasher.Verify(body.Password, account.PasswordHash)) return Results.Unauthorized();
    return Results.Ok(new { token = Shared.Token(account.Id) });
});

app.MapPost("/api/profile", async (HttpContext http, NameBody body) =>
{
    var user = Shared.User(http);
    if (user is null) return Results.Unauthorized();
    var name = body.Name?.Trim();
    if (string.IsNullOrEmpty(name)) return Results.BadRequest();

    await using var db = new Data<User>();
    var account = await db.Rows.FindAsync(user.Value);
    if (account is null) return Results.Unauthorized();
    account.Name = name;
    db.Enqueue(new LabEvent(Guid.NewGuid(), "user.renamed", UserId: user.Value, Name: name));
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

public partial class Program;

public sealed record Cred(string? Login, string? Password, string? Name);
public sealed record LoginBody(string? Login, string? Password);
public sealed record NameBody(string? Name);

static class PasswordHasher
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.', 2);
        if (parts.Length != 2) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expected = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException) { return false; }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
