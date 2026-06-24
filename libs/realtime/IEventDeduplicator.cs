using Atalaya.Contracts;

namespace Atalaya.Realtime;

/// <summary>
/// Deduplicación idempotente por clave de evento (ADR-006). La entrega del broker (SQS/Pub/Sub) es
/// at-least-once; este filtro evita reprocesar lo ya aplicado. Es un <b>check + commit en dos pasos</b>:
/// <see cref="FilterNewAsync"/> solo consulta (no marca), y <see cref="CommitAsync"/> marca como
/// procesado <i>después</i> de aplicar los efectos con éxito. Así, si un efecto falla y el mensaje se
/// reentrega, el evento se reprocesa en vez de perderse (p.ej. un hueco permanente en el data lake),
/// apoyándose en que todos los efectos son idempotentes (guard <c>seq</c>, clave por hash, máquina de incidentes).
/// </summary>
public interface IEventDeduplicator
{
    /// <summary>Devuelve los eventos aún no confirmados (consulta sin marcar).</summary>
    Task<IReadOnlyList<TelemetryEvent>> FilterNewAsync(
        IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default);

    /// <summary>Marca los eventos como procesados. Llamar solo tras aplicar todos los efectos.</summary>
    Task CommitAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default);
}
