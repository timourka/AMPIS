using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lab2;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace S4.Tests;

public class CatalogTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly HttpClient client;
    readonly string cs;

    public CatalogTests(WebApplicationFactory<Program> app)
    {
        Environment.SetEnvironmentVariable("TOKEN_KEY", "lab2-test-signing-key-32-bytes!!");
        cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LAB2_TEST_DB")
            ?? throw new InvalidOperationException("Set LAB2_TEST_DB to a disposable PostgreSQL instance"))
        { Database = $"lab2_s4_{Guid.NewGuid():N}" }.ConnectionString;
        var b = new NpgsqlConnectionStringBuilder(cs);
        var name = b.Database!;
        b.Database = "postgres";
        using (var admin = new NpgsqlConnection(b.ConnectionString))
        {
            admin.Open();
            using var c = new NpgsqlCommand($"create database \"{name}\"", admin);
            c.ExecuteNonQuery();
        }
        using (var db = new NpgsqlConnection(cs))
        {
            db.Open();
            using var s = new NpgsqlCommand("""
                create table users(id uuid primary key, name text);
                create table files(id uuid primary key, owner_id uuid, name text);
                create table downloads(id uuid primary key, file_id uuid, user_id uuid);
                """, db);
            s.ExecuteNonQuery();
        }
        Environment.SetEnvironmentVariable("DB", cs);
        client = app.CreateClient();
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Files_empty_catalog()
    {
        var res = await client.GetAsync("/api/files?page=1&size=10");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<CatalogPage>();
        Assert.Equal(0, body!.Total);
        Assert.Empty(body.Items);
    }

    [Fact]
    public async Task Statistics_requires_owner()
    {
        var id = Guid.NewGuid();
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("insert into files(id, owner_id, name) values ($1, $2, 'a.txt')", db);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();

        var anon = await client.GetAsync($"/api/statistics/{id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var other = client;
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Shared.Token(Guid.NewGuid()));
        var denied = await other.GetAsync($"/api/statistics/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    sealed record CatalogPage(int Page, int Size, int Total, Item[] Items);
    sealed record Item(Guid Id, string Name, string Owner, int Downloads);
}
