namespace Atalaya.Api.Services;

/// <summary>
/// Agregados por dispositivo calculados sobre el <b>data lake frío</b> (no sobre el read model en
/// vivo): conteo de eventos y extremos de telemetría en una ventana temporal. Es el resultado de
/// una consulta analítica (BigQuery en GCP, G4) que el camino caliente no responde bien.
/// </summary>
public sealed record DeviceAnalytics(
    string DeviceId,
    long Events,
    double AvgSpeedKmh,
    double MaxSpeedKmh,
    double MaxEngineTempC,
    double MinFuelPct);

/// <summary>
/// Consulta analítica sobre el data lake (ADR-005/007, fase G4). Abstrae el motor (BigQuery en GCP
/// real) para no acoplar el endpoint al SDK y poder sustituirlo/stubbearlo en pruebas, igual que el
/// resto de interfaces de extensión del proyecto (<c>ITelemetryArchive</c>, etc.).
/// </summary>
public interface IAnalyticsQuery
{
    /// <summary>
    /// Agregados por dispositivo desde <paramref name="from"/> hasta ahora, ordenados por número de
    /// eventos descendente y acotados a <paramref name="limit"/> filas.
    /// </summary>
    Task<IReadOnlyList<DeviceAnalytics>> DeviceAggregatesAsync(
        DateTimeOffset from, int limit, CancellationToken ct = default);
}
