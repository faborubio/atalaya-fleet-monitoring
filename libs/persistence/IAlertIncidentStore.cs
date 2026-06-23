using Atalaya.Contracts;

namespace Atalaya.Persistence;

/// <summary>
/// Read model de <c>alert_incidents</c> (AUD-016/p1): un incidente por <c>(deviceId, rule)</c> con
/// estado abierto/resuelto. Reemplaza el modelo por-evento. Lo escribe el worker (o el procesador
/// InMemory); lo lee la API para el dashboard. Implementado contra Postgres y en memoria.
/// </summary>
public interface IAlertIncidentStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    /// <summary>
    /// Aplica las lecturas de un lote a la máquina de estados y devuelve solo las
    /// <b>transiciones</b> (abrir/escalar/resolver) — lo que hay que notificar.
    /// </summary>
    Task<IReadOnlyList<AlertIncident>> ApplyAsync(
        IReadOnlyList<RuleReading> readings, CancellationToken ct = default);

    /// <summary>Incidentes para el dashboard: abiertos primero, luego los resueltos recientes.</summary>
    Task<IReadOnlyList<AlertIncident>> GetActiveAsync(int limit = 100, CancellationToken ct = default);
}
