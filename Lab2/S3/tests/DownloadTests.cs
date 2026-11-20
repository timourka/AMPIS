using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace S3.Tests;

public class DownloadTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> app;
    static string Database(string prefix) => new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("LAB2_TEST_DB") ?? throw new InvalidOperationException("Set LAB2_TEST_DB to a disposable PostgreSQL instance"))
        { Database = $"lab2_s3_{prefix}_{Guid.NewGuid():N}" }.ConnectionString;
    public DownloadTests(WebApplicationFactory<Program> app)
    {
        var files = Database("files");
        var own = Database("own");
        Create(files, "create table files(id uuid primary key, owner_id uuid, name text, content bytea)");
        Create(own, "create table downloads(id uuid primary key, file_id uuid, user_id uuid, downloaded_at timestamptz default now()); create table outbox(id bigint generated always as identity primary key, body text)");
        Environment.SetEnvironmentVariable("FILES_DB", files);
        Environment.SetEnvironmentVariable("DB", own);
        this.app = app;
    }

    static void Create(string cs, string ddl)
    {
        var b = new NpgsqlConnectionStringBuilder(cs);
        var name = b.Database!;
        b.Database = "postgres";
        using var admin = new NpgsqlConnection(b.ConnectionString);
        admin.Open();
        using (var c = new NpgsqlCommand($"create database \"{name}\"", admin)) c.ExecuteNonQuery();
        using var db = new NpgsqlConnection(cs);
        db.Open();
        using var s = new NpgsqlCommand(ddl, db);
        s.ExecuteNonQuery();
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Download_unknown_id_is_not_found_without_token()
    {
        var res = await app.CreateClient().GetAsync($"/api/download/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
