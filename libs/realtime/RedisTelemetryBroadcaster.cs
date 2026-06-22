using System.Text.Json;
using Atalaya.Contracts;
using StackExchange.Redis;

namespace Atalaya.Realtime;

/// <summary>
/// Publica los deltas como JSON en un canal de Redis pub/sub. El reenvío a SignalR lo hace
/// el suscriptor del lado de la API.
/// </summary>
public sealed class RedisTelemetryBroadcaster(IConnectionMultiplexer redis) : ITelemetryBroadcaster
{
    /// <summary>Canal pub/sub compartido entre worker (publica) y API (reenvía).</summary>
    public const string Channel = "atalaya:telemetry:deltas";

    public async Task PublishDeltasAsync(IReadOnlyList<DeviceState> deltas, CancellationToken ct = default)
    {
        if (deltas.Count == 0) return;
        var payload = JsonSerializer.Serialize(deltas);
        await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel), payload);
    }
}
