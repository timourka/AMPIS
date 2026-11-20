using System.ComponentModel.DataAnnotations.Schema;
using Lab2;
using Microsoft.EntityFrameworkCore;

[Table("files")]
sealed class CatalogFile
{
    [Column("id")] public Guid Id { get; set; }
    [Column("owner_id")] public Guid OwnerId { get; set; }
    [Column("name")] public string Name { get; set; } = "";
}

[Table("users")]
sealed class CatalogUser
{
    [Column("id")] public Guid Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
}

sealed class CatalogData : DbContext
{
    public DbSet<CatalogFile> Files => Set<CatalogFile>();
    public DbSet<CatalogUser> Users => Set<CatalogUser>();
    public DbSet<Download> Downloads => Set<Download>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseNpgsql(Environment.GetEnvironmentVariable("DB"));
}
