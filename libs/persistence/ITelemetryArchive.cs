using Atalaya.Contracts;

namespace Atalaya.Persistence;

/// <summary>
/// Camino frío (ADR-005/007): telemetría cruda persistida para consultas históricas. En Postgres
/// vive en una tabla <b>particionada por tiempo</b> (retención O(1) por <c>DROP PARTITION</c>).
/// La escribe el worker; la lee la API para la vista histórica. Nunca compite con el camino
/// caliente (read models).
/// </summary>
public interface ITelemetryArchive
{
    /// <summary>Crea el esquema si no existe (idempotente).</summary>
    Task EnsureSchemaAsync(CancellationToken ct = default);

    /// <summary>Inserta el lote de eventos crudos de forma idempotente (por evento).</summary>
    Task AppendAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default);

    /// <summary>Eventos de un dispositivo en un rango de tiempo, del más reciente al más viejo.</summary>
    Task<IReadOnlyList<TelemetryEvent>> QueryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, int limit = 1000,
        CancellationToken ct = default);

    /// <summary>
    /// Retención O(1) (ADR-007, AUD-015 p2): elimina las particiones diarias anteriores a
    /// <paramref name="cutoff"/> (DROP, no DELETE). Devuelve las particiones eliminadas.
    /// </summary>
    Task<IReadOnlyList<string>> DropPartitionsBeforeAsync(
        DateOnly cutoff, CancellationToken ct = default);
}
