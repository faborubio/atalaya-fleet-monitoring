using System.Collections.Concurrent;

namespace Atalaya.Api.Services;

/// <summary>
/// Deduplicación idempotente por clave de evento (ADR-006). La entrega es at-least-once,
/// así que el mismo evento puede llegar más de una vez; solo el primero produce efecto.
/// </summary>
public interface IDeduplicator
{
    /// <returns><c>true</c> si es la primera vez que se ve <paramref name="eventId"/>.</returns>
    bool TryMarkProcessed(string eventId);
}

/// <summary>
/// Versión en memoria para dev. <b>Objetivo:</b> set en ElastiCache/Redis con TTL +
/// constraint único en SQL (ADR-006). Aquí no hay expiración: suficiente para desarrollo.
/// </summary>
public sealed class InMemoryDeduplicator : IDeduplicator
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    public bool TryMarkProcessed(string eventId) => _seen.TryAdd(eventId, 0);
}
