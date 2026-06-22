using System.Text.Json;
using System.Threading.Channels;
using Atalaya.Api.Hubs;
using Atalaya.Contracts;
using Atalaya.Realtime;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Atalaya.Api.Processing;

/// <summary>
/// Puente Redis→SignalR (ADR-002) endurecido:
///  - la suscripción a Redis solo **encola** (no bloquea el hilo de Redis);
///  - un bombeo único **coalesce** los deltas en ventanas de ~50 ms, fusionando por
///    dispositivo (último <c>seq</c>), y **espera** el envío (sin fire-and-forget);
///  - canal **acotado** con descarte del más viejo como backpressure ante un cliente lento.
/// Solo activo en modo Aws (en InMemory el push lo hace <see cref="TelemetryProcessor"/>).
/// </summary>
public sealed class RedisDeltaForwarder(
    IConnectionMultiplexer redis,
    IHubContext<TelemetryHub> hub,
    ViewportRegistry viewport,
    ILogger<RedisDeltaForwarder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int CoalesceMs = 50;

    private readonly Channel<DeviceState[]> _incoming = Channel.CreateBounded<DeviceState[]>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // bajo carga, gana el estado más nuevo
            SingleReader = true,
        });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Literal(RedisTelemetryBroadcaster.Channel),
            (_, value) =>
            {
                if (!value.HasValue) return;
                try
                {
                    var deltas = JsonSerializer.Deserialize<DeviceState[]>(value!, Json);
                    if (deltas is { Length: > 0 }) _incoming.Writer.TryWrite(deltas);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Delta inválido recibido por Redis");
                }
            });

        logger.LogInformation(
            "Reenviando deltas Redis→SignalR (coalescencia {Ms} ms, canal acotado).", CoalesceMs);

        var merged = new Dictionary<string, DeviceState>(256);
        var reader = _incoming.Reader;

        while (await reader.WaitToReadAsync(stoppingToken))
        {
            merged.Clear();
            Drain(reader, merged);
            await Task.Delay(CoalesceMs, stoppingToken); // ventana de coalescencia
            Drain(reader, merged);

            if (merged.Count == 0) continue;
            try
            {
                await SendAsync(merged.Values.ToArray(), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo enviando deltas por SignalR");
            }
        }
    }

    /// <summary>
    /// Envío dual (AUD-008): a los clientes del firehose, el lote completo por broadcast; a los
    /// que están en modo viewport, solo el delta de cada dispositivo a su grupo. Si nadie está en
    /// viewport, es un único <c>Clients.All</c> (comportamiento por defecto, sin sobrecoste).
    /// </summary>
    private async Task SendAsync(DeviceState[] batch, CancellationToken ct)
    {
        var viewportConns = viewport.ConnectionsInViewportMode();
        if (viewportConns.Count == 0)
        {
            await hub.Clients.All.SendAsync("devicesUpdated", batch, ct);
            return;
        }

        await hub.Clients.AllExcept(viewportConns).SendAsync("devicesUpdated", batch, ct);
        var subscribed = viewport.SubscribedDevices();
        foreach (var d in batch)
            if (subscribed.Contains(d.DeviceId))
                await hub.Clients.Group(TelemetryHub.Group(d.DeviceId))
                    .SendAsync("devicesUpdated", new[] { d }, ct);
    }

    private static void Drain(ChannelReader<DeviceState[]> reader, Dictionary<string, DeviceState> merged)
    {
        while (reader.TryRead(out var batch))
            foreach (var d in batch)
                if (!merged.TryGetValue(d.DeviceId, out var current) || d.Seq >= current.Seq)
                    merged[d.DeviceId] = d;
    }
}
