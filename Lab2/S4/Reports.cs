using Lab2;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

static class Reports
{
    public static async Task<IResult> List(HttpContext http, string? q, string? sort, string? dir, int? page, int? size)
    {
        int p = page ?? 1, s = size ?? 10;
        if (p < 1 || s is < 1 or > 100 || dir is not (null or "asc" or "desc") || sort is not (null or "name" or "owner" or "downloads"))
            return Results.BadRequest();
        await using var db = new CatalogData();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var user = Shared.User(http);
        var query = from f in db.Files
                    join u in db.Users on f.OwnerId equals u.Id into owners
                    from u in owners.DefaultIfEmpty()
                    select new { f.Id, f.Name, f.OwnerId, Owner = u == null ? "" : u.Name,
                                 Downloads = db.Downloads.LongCount(d => d.FileId == f.Id) };
        var search = q?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(f => EF.Functions.ILike(f.Name, pattern) || EF.Functions.ILike(f.Owner, pattern));
        }
        var total = await query.LongCountAsync();
        var desc = dir == "desc";
        var ordered = sort switch {
            "downloads" => desc ? query.OrderByDescending(f => f.Downloads) : query.OrderBy(f => f.Downloads),
            "owner" => desc ? query.OrderByDescending(f => f.Owner) : query.OrderBy(f => f.Owner),
            _ => desc ? query.OrderByDescending(f => f.Name) : query.OrderBy(f => f.Name)
        };
        var offset = (long)(p - 1) * s;
        var items = offset >= total ? [] : await ordered.ThenBy(f => f.Id).Skip((int)offset).Take(s)
            .Select(f => new FileItem(f.Id, f.Name, f.Owner, f.Downloads, f.OwnerId == user)).ToArrayAsync();
        await tx.CommitAsync();
        return Results.Ok(new { items, page = p, size = s, total });
    }

    public static async Task<IResult> Statistics(Guid id, HttpContext http)
    {
        var user = Shared.User(http);
        if (user is null) return Results.Unauthorized();
        await using var db = new CatalogData();
        var owner = await db.Files.Where(f => f.Id == id).Select(f => (Guid?)f.OwnerId).SingleOrDefaultAsync();
        if (owner is null) return Results.NotFound();
        if (owner != user) return Results.StatusCode(403);
        var history = await (from d in db.Downloads
                             where d.FileId == id
                             join u in db.Users on d.UserId equals (Guid?)u.Id into users
                             from u in users.DefaultIfEmpty()
                             orderby u.Name, d.Id
                             select new { Anonymous = d.UserId == null, Name = u == null ? null : u.Name }).ToArrayAsync();
        return Results.Ok(new { total = history.Length, anonymous = history.Count(d => d.Anonymous),
                                users = history.Where(d => d.Name != null).Select(d => d.Name) });
    }

    public static async Task Apply(NpgsqlConnection db, NpgsqlTransaction tx, LabEvent e)
    {
        var sql = e.Type switch
        {
            "user.registered" or "user.renamed" => "insert into users values (@uid,@name) on conflict(id) do update set name=excluded.name",
            "file.uploaded" => "insert into files values (@file,@uid,@name) on conflict do nothing",
            "file.deleted" => "delete from files where id=@file",
            "file.downloaded" => "insert into downloads values (@event,@file,@uid) on conflict do nothing",
            _ => null
        };
        if (sql is null) return;
        await using var cmd = new NpgsqlCommand(sql, db, tx);
        cmd.Parameters.AddWithValue("event", e.EventId);
        cmd.Parameters.AddWithValue("file", e.FileId);
        cmd.Parameters.AddWithValue("uid", NpgsqlDbType.Uuid, e.UserId == Guid.Empty ? DBNull.Value : e.UserId);
        cmd.Parameters.AddWithValue("name", NpgsqlDbType.Text, (object?)e.Name ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}

sealed record FileItem(Guid Id, string Name, string Owner, long Downloads, bool IsOwner);
