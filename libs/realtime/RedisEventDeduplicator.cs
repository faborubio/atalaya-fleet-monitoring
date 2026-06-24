using Atalaya.Contracts;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Atalaya.Realtime;

/// <summary>
/// Dedup en Redis en dos pasos (ADR-006): <see cref="FilterNewAsync"/> consulta <c>EXISTS</c> (no
/// muta) y <see cref="CommitAsync"/> hace <c>SET dedup:{eventId} 1 EX ttl</c> tras aplicar los
/// efectos. Marcar al confirmar (no al filtrar) evita perder un evento si un efecto posterior falla y
/// el mensaje se reentrega. El TTL acota el estado; el lote se resuelve por pipeline (un round-trip).
/// La carrera de dos entregas concurrentes del mismo evento es inocua: ambos efectos son idempotentes.
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
        var exists = new Task<bool>[events.Count];
        for (var i = 0; i < events.Count; i++)
            exists[i] = batch.KeyExistsAsync($"dedup:{events[i].EventId}");
        batch.Execute();
        await Task.WhenAll(exists);

        var fresh = new List<TelemetryEvent>(events.Count);
        for (var i = 0; i < events.Count; i++)
            if (!exists[i].Result) fresh.Add(events[i]); // no existe = aún no confirmado
        return fresh;
    }

    public async Task CommitAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;

        var db = redis.GetDatabase();
        var batch = db.CreateBatch();
        var sets = new Task<bool>[events.Count];
        for (var i = 0; i < events.Count; i++)
            sets[i] = batch.StringSetAsync($"dedup:{events[i].EventId}", "1", _ttl);
        batch.Execute();
        await Task.WhenAll(sets);
    }
}
