using System.Text.Json;
using Atalaya.Api.Hubs;
using Atalaya.Contracts;
using Atalaya.Realtime;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Atalaya.Api.Processing;

/// <summary>
/// Puente Redis→SignalR (ADR-002): se suscribe al canal donde el worker publica los deltas
/// y los reenvía a los navegadores conectados al hub. Solo activo en modo Aws (en InMemory
/// el push lo hace <see cref="TelemetryProcessor"/> directamente).
/// </summary>
public sealed class RedisDeltaForwarder(
    IConnectionMultiplexer redis,
    IHubContext<TelemetryHub> hub,
    ILogger<RedisDeltaForwarder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Literal(RedisTelemetryBroadcaster.Channel),
            (_, value) =>
            {
                if (!value.HasValue) return;
                var deltas = JsonSerializer.Deserialize<DeviceState[]>(value!, Json) ?? [];
                if (deltas.Length > 0)
                    hub.Clients.All.SendAsync("devicesUpdated", deltas, stoppingToken);
            });

        logger.LogInformation("Reenviando deltas Redis→SignalR (canal {Channel}).",
            RedisTelemetryBroadcaster.Channel);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
