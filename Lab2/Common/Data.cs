using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Lab2;

[Table("users")]
public sealed class User
{
    [Column("id")] public Guid Id { get; set; }
    [Column("login")] public string Login { get; set; } = "";
    [Column("password")] public string PasswordHash { get; set; } = "";
    [Column("name")] public string Name { get; set; } = "";
}

[Table("files")]
public sealed class StoredFile
{
    [Column("id")] public Guid Id { get; set; }
    [Column("owner_id")] public Guid OwnerId { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("content")] public byte[] Content { get; set; } = [];
}

[Table("downloads")]
public sealed class Download
{
    [Column("id")] public Guid Id { get; set; }
    [Column("file_id")] public Guid FileId { get; set; }
    [Column("user_id")] public Guid? UserId { get; set; }
}

[Table("outbox")]
public sealed class OutboxEntry
{
    [Column("id")] public long Id { get; set; }
    [Column("body")] public string Body { get; set; } = "";
}

public sealed class Data<T> : DbContext where T : class
{
    readonly string? connectionString;
    public Data(string? connectionString = null) => this.connectionString = connectionString;
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseNpgsql(connectionString ?? Environment.GetEnvironmentVariable("DB"));
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<T>();
        model.Entity<OutboxEntry>();
    }
    public DbSet<T> Rows => Set<T>();
    public void Enqueue(LabEvent e) => Set<OutboxEntry>().Add(new OutboxEntry { Body = JsonSerializer.Serialize(e, Shared.Json) });
    public DbSet<OutboxEntry> Outbox => Set<OutboxEntry>();
}
