using System.Text.Json;
using Atalaya.Api.Hubs;
using Atalaya.Contracts;
using Atalaya.Realtime;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Atalaya.Api.Processing;

/// <summary>
/// Puente Redis→SignalR para alertas (ADR-002), homólogo de <see cref="RedisDeltaForwarder"/>
/// pero sin coalescencia: las alertas son de bajo volumen y deben llegar cuanto antes (NFR
/// estrella: sub-segundos). El worker publica en el canal de alertas; aquí se reenvían a los
/// navegadores por el evento <c>alertsRaised</c>. Solo activo en modo Aws.
/// </summary>
public sealed class RedisAlertForwarder(
    IConnectionMultiplexer redis,
    IHubContext<TelemetryHub> hub,
    ILogger<RedisAlertForwarder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Literal(RedisAlertBroadcaster.Channel),
            (RedisChannel _, RedisValue value) => Forward(value, stoppingToken));

        logger.LogInformation("Reenviando alertas Redis→SignalR (canal {Channel}).",
            RedisAlertBroadcaster.Channel);
    }

    private void Forward(RedisValue value, CancellationToken ct)
    {
        if (!value.HasValue) return;
        try
        {
            var incidents = JsonSerializer.Deserialize<AlertIncident[]>(value!, Json);
            if (incidents is { Length: > 0 })
                _ = hub.Clients.All.SendAsync("alertsRaised", incidents, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Alerta inválida recibida por Redis");
        }
    }
}
