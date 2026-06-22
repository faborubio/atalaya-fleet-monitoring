using Atalaya.Contracts;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Atalaya.Realtime;

/// <summary>
/// Dedup en Redis: <c>SET dedup:{eventId} 1 NX EX ttl</c>. El SET con NX es atómico y
/// devuelve true solo la primera vez que se ve la clave; el TTL acota el estado (ADR-006).
/// El lote se resuelve por pipeline para un solo round-trip.
/// </summary>
public sealed class RedisEventDeduplicator(
    IConnectionMultiplexer redis, IOptions<RedisOptions> options) : IEventDeduplicator
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.DedupTtlSeconds);

    public async Task<IReadOnlyList<TelemetryEvent>> FilterNewAsync(
        IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return events;

        var db = redis.GetDatabase();
        var batch = db.CreateBatch();
        var flags = new Task<bool>[events.Count];
        for (var i = 0; i < events.Count; i++)
            flags[i] = batch.StringSetAsync(
                $"dedup:{events[i].EventId}", "1", _ttl, when: When.NotExists);
        batch.Execute();
        await Task.WhenAll(flags);

        var fresh = new List<TelemetryEvent>(events.Count);
        for (var i = 0; i < events.Count; i++)
            if (flags[i].Result) fresh.Add(events[i]); // true = clave nueva (no duplicado)
        return fresh;
    }
}
