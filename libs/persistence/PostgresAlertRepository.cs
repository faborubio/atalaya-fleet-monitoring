using Atalaya.Contracts;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Atalaya.Persistence;

/// <summary>
/// Implementación Postgres del read model <c>alerts</c>. Inserción por lote con <c>unnest</c>
/// en un solo viaje; <c>ON CONFLICT (alert_id) DO NOTHING ... RETURNING</c> hace la escritura
/// idempotente y devuelve solo las alertas nuevas (las que el worker debe notificar).
/// </summary>
public sealed class PostgresAlertRepository(IOptions<PostgresOptions> options) : IAlertRepository
{
    private readonly string _cs = options.Value.ConnectionString;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS alerts (
              alert_id   text PRIMARY KEY,
              device_id  text NOT NULL,
              rule       text NOT NULL,
              severity   text NOT NULL,
              value      double precision NOT NULL,
              ts         timestamptz NOT NULL,
              message    text NOT NULL,
              created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_alerts_ts ON alerts (ts DESC);
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Alert>> InsertAsync(
        IReadOnlyList<Alert> alerts, CancellationToken ct = default)
    {
        if (alerts.Count == 0) return [];

        // Un mismo alert_id no puede aparecer dos veces en una sola sentencia (ON CONFLICT).
        var unique = alerts
            .GroupBy(a => a.AlertId)
            .Select(g => g.First())
            .ToArray();

        const string sql = """
            INSERT INTO alerts (alert_id, device_id, rule, severity, value, ts, message)
            SELECT * FROM unnest(
              @ids::text[], @devices::text[], @rules::text[], @sev::text[],
              @values::float8[], @ts::timestamptz[], @msgs::text[])
            ON CONFLICT (alert_id) DO NOTHING
            RETURNING alert_id, device_id, rule, severity, value, ts, message;
            """;

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", unique.Select(a => a.AlertId).ToArray());
        cmd.Parameters.AddWithValue("devices", unique.Select(a => a.DeviceId).ToArray());
        cmd.Parameters.AddWithValue("rules", unique.Select(a => a.Rule).ToArray());
        cmd.Parameters.AddWithValue("sev", unique.Select(a => a.Severity.ToString()).ToArray());
        cmd.Parameters.AddWithValue("values", unique.Select(a => a.Value).ToArray());
        cmd.Parameters.AddWithValue("ts", unique.Select(a => a.Ts.UtcDateTime).ToArray());
        cmd.Parameters.AddWithValue("msgs", unique.Select(a => a.Message).ToArray());

        var inserted = new List<Alert>(unique.Length);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            inserted.Add(ReadAlert(reader));

        return inserted;
    }

    public async Task<IReadOnlyList<Alert>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        const string sql = """
            SELECT alert_id, device_id, rule, severity, value, ts, message
            FROM alerts
            ORDER BY ts DESC
            LIMIT @limit;
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("limit", limit);

        var rows = new List<Alert>(limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(ReadAlert(reader));

        return rows;
    }

    private static Alert ReadAlert(NpgsqlDataReader r) => new(
        r.GetString(r.GetOrdinal("alert_id")),
        r.GetString(r.GetOrdinal("device_id")),
        r.GetString(r.GetOrdinal("rule")),
        Enum.Parse<AlertSeverity>(r.GetString(r.GetOrdinal("severity"))),
        r.GetDouble(r.GetOrdinal("value")),
        new DateTimeOffset(
            DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal("ts")), DateTimeKind.Utc), TimeSpan.Zero),
        r.GetString(r.GetOrdinal("message")));
}
