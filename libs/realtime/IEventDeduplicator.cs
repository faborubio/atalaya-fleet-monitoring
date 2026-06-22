using Atalaya.Contracts;

namespace Atalaya.Realtime;

/// <summary>
/// Deduplicación idempotente por clave de evento (ADR-006). La entrega SQS es
/// at-least-once; este filtro descarta los eventos ya vistos para que los reintentos no
/// dupliquen efectos.
/// </summary>
public interface IEventDeduplicator
{
    /// <summary>Devuelve solo los eventos vistos por primera vez.</summary>
    Task<IReadOnlyList<TelemetryEvent>> FilterNewAsync(
        IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default);
}
