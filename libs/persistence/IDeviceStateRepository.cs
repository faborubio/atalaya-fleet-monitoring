using Atalaya.Contracts;

namespace Atalaya.Persistence;

/// <summary>
/// Read model <c>device_state</c> en Postgres (ADR-005/007): una fila por dispositivo con
/// su último estado. Escrito por el worker, leído por la API para el snapshot del dashboard.
/// </summary>
public interface IDeviceStateRepository
{
    /// <summary>Crea el esquema si no existe (idempotente).</summary>
    Task EnsureSchemaAsync(CancellationToken ct = default);

    /// <summary>Upsert por lote, conservando el <c>seq</c> más alto por dispositivo.</summary>
    Task UpsertAsync(IReadOnlyList<DeviceState> states, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceState>> GetAllAsync(CancellationToken ct = default);
}
