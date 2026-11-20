using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using RabbitMQ.Client;

namespace Lab2;

public record LabEvent(Guid EventId, string Type, Guid FileId = default, Guid UserId = default, string? Name = null);
public delegate Task ActionHandler(NpgsqlConnection db, NpgsqlTransaction tx, LabEvent e);

public static class Shared
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(
        Environment.GetEnvironmentVariable("TOKEN_KEY") ?? throw new InvalidOperationException("Set TOKEN_KEY")));
    public static NpgsqlConnection Db() => new(Environment.GetEnvironmentVariable("DB"));
    public static string Token(Guid id) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer: "lab2", audience: "lab2", claims: [new Claim("sub", id.ToString())],
        expires: DateTime.UtcNow.AddHours(8), signingCredentials: new(Key, SecurityAlgorithms.HmacSha256)));

    public static Guid? User(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(header[7..], new TokenValidationParameters
            {
                IssuerSigningKey = Key, ValidateIssuerSigningKey = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidIssuer = "lab2", ValidAudience = "lab2", ClockSkew = TimeSpan.Zero
            }, out _);
            return Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return null; }
    }

    public static void Start(WebApplicationBuilder b, bool publish = false, bool consume = false)
    {
        if (publish) b.Services.AddHostedService<OutboxWorker>();
        if (consume) b.Services.AddHostedService<ReportWorker>();
    }
}

internal static class Bus
{
    internal const string Exchange = "lab2.events";
    internal const string Queue = "lab2.s4";
    internal static Task<IConnection> Connect(CancellationToken ct) => new ConnectionFactory
    {
        Uri = new Uri(Environment.GetEnvironmentVariable("AMQP") ?? "amqp://lab:lab@rabbitmq:5672/"),
        AutomaticRecoveryEnabled = false,
        RequestedHeartbeat = TimeSpan.FromSeconds(5)
    }.CreateConnectionAsync(ct);

    internal static async Task Declare(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, "", cancellationToken: ct);
    }
}

internal abstract class EventWorker(ILogger log) : BackgroundService
{
    protected sealed override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await Bus.Connect(ct);
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
                await Bus.Declare(channel, ct);
                await Process(channel, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogWarning("Повтор подключения: {Message}", ex.Message);
                await Task.Delay(1000, ct);
            }
        }
    }
    protected abstract Task Process(IChannel channel, CancellationToken ct);
}

internal sealed class OutboxWorker(ILogger<OutboxWorker> log) : EventWorker(log)
{
    protected override async Task Process(IChannel channel, CancellationToken ct)
    {
        while (channel.IsOpen && !ct.IsCancellationRequested)
        {
            await using (var db = Shared.Db())
            {
                await db.OpenAsync(ct);
                await using var select = new NpgsqlCommand("select id,body from outbox order by id limit 1", db);
                long id = 0;
                string? body = null;
                await using (var rows = await select.ExecuteReaderAsync(ct))
                    if (await rows.ReadAsync(ct)) { id = rows.GetInt64(0); body = rows.GetString(1); }
                if (body is not null)
                {
                    await channel.BasicPublishAsync(Bus.Exchange, "", mandatory: true,
                        new BasicProperties { Persistent = true, ContentType = "application/json" }, Encoding.UTF8.GetBytes(body), ct);
                    await using var delete = new NpgsqlCommand("delete from outbox where id=@id", db);
                    delete.Parameters.AddWithValue("id", id);
                    await delete.ExecuteNonQueryAsync(ct);
                    continue;
                }
            }
            await Task.Delay(150, ct);
        }
    }
}

internal sealed class ReportWorker(ActionHandler apply, ILogger<ReportWorker> log) : EventWorker(log)
{
    protected override async Task Process(IChannel channel, CancellationToken ct)
    {
        // Prefetch one preserves the delivery order of each producer. Reconnect requeues unacked messages.
        await channel.BasicQosAsync(0, 1, false, ct);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) => { stopped.TrySetResult(); return Task.CompletedTask; };
        var consumer = new RabbitMQ.Client.Events.AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var e = JsonSerializer.Deserialize<LabEvent>(args.Body.Span, Shared.Json) ?? throw new JsonException();
                await using var db = Shared.Db();
                await db.OpenAsync(ct);
                await using var tx = await db.BeginTransactionAsync(ct);
                await using var seen = new NpgsqlCommand("insert into inbox values (@id) on conflict do nothing", db, tx);
                seen.Parameters.AddWithValue("id", e.EventId);
                if (await seen.ExecuteNonQueryAsync(ct) == 1) await apply(db, tx, e);
                await tx.CommitAsync(ct);
                await channel.BasicAckAsync(args.DeliveryTag, false, ct);
            }
            catch (JsonException)
            {
                log.LogWarning("Сообщение не соответствует JSON-контракту лабораторной");
                await channel.BasicNackAsync(args.DeliveryTag, false, false, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning("Сообщение будет доставлено повторно: {Message}", ex.Message);
                stopped.TrySetException(ex);
            }
        };
        await channel.BasicConsumeAsync(Bus.Queue, false, consumer, ct);
        await stopped.Task.WaitAsync(ct);
    }
}
