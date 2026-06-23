using System.Text.Json;
using Atalaya.Contracts;
using StackExchange.Redis;

namespace Atalaya.Realtime;

/// <summary>
/// Publica alertas como JSON en un canal de Redis pub/sub propio. El reenvío a SignalR lo hace
/// el suscriptor del lado de la API. Las alertas son de bajo volumen (no un firehose), así que
/// no se coalescen: cada lote nuevo se publica tal cual.
/// </summary>
public sealed class RedisAlertBroadcaster(IConnectionMultiplexer redis) : IAlertBroadcaster
{
    /// <summary>Canal pub/sub de alertas, compartido entre worker (publica) y API (reenvía).</summary>
    public const string Channel = "atalaya:alerts:new";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishAlertsAsync(IReadOnlyList<AlertIncident> incidents, CancellationToken ct = default)
    {
        if (incidents.Count == 0) return;
        var payload = JsonSerializer.Serialize(incidents, Json);
        await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel), payload);
    }
}
