using Google.Cloud.BigQuery.V2;

namespace Atalaya.Api.Services;

/// <summary>
/// Implementación de <see cref="IAnalyticsQuery"/> con <b>BigQuery</b> (G4, ADR-013): consulta la
/// external table sobre el data lake GCS (NDJSON, <c>scripts/bigquery-setup.mjs</c>) sin copiar datos
/// — equivale a Athena sobre S3 en la era AWS. Solo se registra si hay dataset configurado
/// (<see cref="GcpOptions.AnalyticsEnabled"/>); no hay emulador decente de BigQuery → se valida
/// contra el proyecto real (free-tier cubre dev). El cliente usa ADC (credenciales del entorno).
/// </summary>
public sealed class BigQueryAnalyticsQuery(BigQueryClient client, GcpOptions options) : IAnalyticsQuery
{
    public async Task<IReadOnlyList<DeviceAnalytics>> DeviceAggregatesAsync(
        DateTimeOffset from, int limit, CancellationToken ct = default)
    {
        // El nombre de tabla sale de configuración (no de entrada del usuario); el filtro temporal y
        // el límite van parametrizados (sin interpolar) para evitar inyección y permitir poda por `ts`.
        var table = $"`{client.ProjectId}.{options.DatasetId}.{options.AnalyticsTable}`";
        var sql = $"""
            SELECT deviceId,
                   COUNT(*)            AS events,
                   AVG(speedKmh)       AS avgSpeedKmh,
                   MAX(speedKmh)       AS maxSpeedKmh,
                   MAX(engineTempC)    AS maxEngineTempC,
                   MIN(fuelPct)        AS minFuelPct
            FROM {table}
            WHERE ts >= @from
            GROUP BY deviceId
            ORDER BY events DESC
            LIMIT @limit
            """;

        var parameters = new[]
        {
            new BigQueryParameter("from", BigQueryDbType.Timestamp, from.UtcDateTime),
            new BigQueryParameter("limit", BigQueryDbType.Int64, (long)limit),
        };

        // Cost guard (revisión crítica G4): sin poda de particiones la query escana todo el lake;
        // el tope hace que BigQuery rechace una consulta desmedida en vez de facturarla (pay-per-byte).
        var queryOptions = options.AnalyticsMaxBytesBilled > 0
            ? new QueryOptions { MaximumBytesBilled = options.AnalyticsMaxBytesBilled }
            : null;

        var results = await client.ExecuteQueryAsync(sql, parameters, queryOptions, cancellationToken: ct);

        var rows = new List<DeviceAnalytics>();
        foreach (var row in results) // resultado ya acotado por LIMIT; iteración trivial
        {
            rows.Add(new DeviceAnalytics(
                (string)row["deviceId"],
                Convert.ToInt64(row["events"]),
                Convert.ToDouble(row["avgSpeedKmh"]),
                Convert.ToDouble(row["maxSpeedKmh"]),
                Convert.ToDouble(row["maxEngineTempC"]),
                Convert.ToDouble(row["minFuelPct"])));
        }
        return rows;
    }
}
