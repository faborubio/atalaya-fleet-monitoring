using Atalaya.Contracts;

namespace Atalaya.Persistence;

/// <summary>
/// Read model <c>alerts</c> en Postgres (SAD §6, ADR-005/007). Escrito por el worker cuando
/// las reglas por umbral disparan; leído por la API para el snapshot del dashboard.
/// </summary>
public interface IAlertRepository
{
    /// <summary>Crea el esquema si no existe (idempotente).</summary>
    Task EnsureSchemaAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserta el lote de alertas de forma idempotente (ON CONFLICT DO NOTHING sobre
    /// <c>alert_id</c>): el at-least-once de SQS no duplica alertas (ADR-006). Devuelve las
    /// que realmente se insertaron (nuevas), para no re-notificar duplicados.
    /// </summary>
    Task<IReadOnlyList<Alert>> InsertAsync(IReadOnlyList<Alert> alerts, CancellationToken ct = default);

    /// <summary>Las alertas más recientes (snapshot inicial del dashboard).</summary>
    Task<IReadOnlyList<Alert>> GetRecentAsync(int limit = 100, CancellationToken ct = default);
}
