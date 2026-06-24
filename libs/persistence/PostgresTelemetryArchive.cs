using Atalaya.Contracts;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Atalaya.Persistence;

/// <summary>
/// Camino frío en Postgres (ADR-007): tabla <c>telemetry</c> <b>particionada por rango de tiempo</b>
/// (una partición por día). Particionar permite retención O(1) por <c>DROP PARTITION</c> sin DELETE
/// masivos. Inserción por lote con <c>unnest</c> + <c>ON CONFLICT DO NOTHING</c> (idempotente bajo
/// at-least-once). Las particiones diarias se crean de forma perezosa según los <c>ts</c> del lote.
/// </summary>
public sealed class PostgresTelemetryArchive(IOptions<PostgresOptions> options) : ITelemetryArchive
{
    private readonly string _cs = options.Value.ConnectionString;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS telemetry (
              device_id     text NOT NULL,
              ts            timestamptz NOT NULL,
              event_id      text NOT NULL,
              seq           bigint NOT NULL,
              lat           double precision NOT NULL,
              lng           double precision NOT NULL,
              speed_kmh     double precision NOT NULL,
              heading_deg   double precision NOT NULL,
              fuel_pct      double precision NOT NULL,
              engine_temp_c double precision NOT NULL,
              PRIMARY KEY (device_id, ts, event_id)
            ) PARTITION BY RANGE (ts);
            CREATE INDEX IF NOT EXISTS ix_telemetry_device_ts ON telemetry (device_id, ts DESC);
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));

        // Asegura la partición de hoy para que el primer insert no dependa de la creación perezosa.
        await using var c2 = new NpgsqlConnection(_cs);
        await c2.OpenAsync(ct);
        await EnsurePartitionAsync(c2, DateOnly.FromDateTime(DateTime.UtcNow), ct);
    }

    public async Task AppendAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        // Crea (si faltan) las particiones diarias que cubre este lote.
        foreach (var date in events.Select(e => DateOnly.FromDateTime(e.Ts.UtcDateTime)).Distinct())
            await EnsurePartitionAsync(conn, date, ct);

        const string sql = """
            INSERT INTO telemetry
              (device_id, ts, event_id, seq, lat, lng, speed_kmh, heading_deg, fuel_pct, engine_temp_c)
            SELECT * FROM unnest(
              @devices::text[], @ts::timestamptz[], @ids::text[], @seq::bigint[],
              @lat::float8[], @lng::float8[], @speed::float8[], @heading::float8[],
              @fuel::float8[], @temp::float8[])
            ON CONFLICT (device_id, ts, event_id) DO NOTHING;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("devices", events.Select(e => e.DeviceId).ToArray());
        cmd.Parameters.AddWithValue("ts", events.Select(e => e.Ts.UtcDateTime).ToArray());
        cmd.Parameters.AddWithValue("ids", events.Select(e => e.EventId).ToArray());
        cmd.Parameters.AddWithValue("seq", events.Select(e => e.Seq).ToArray());
        cmd.Parameters.AddWithValue("lat", events.Select(e => e.Lat).ToArray());
        cmd.Parameters.AddWithValue("lng", events.Select(e => e.Lng).ToArray());
        cmd.Parameters.AddWithValue("speed", events.Select(e => e.SpeedKmh).ToArray());
        cmd.Parameters.AddWithValue("heading", events.Select(e => e.HeadingDeg).ToArray());
        cmd.Parameters.AddWithValue("fuel", events.Select(e => e.FuelPct).ToArray());
        cmd.Parameters.AddWithValue("temp", events.Select(e => e.EngineTempC).ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<TelemetryEvent>> QueryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, int limit = 1000,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT event_id, device_id, ts, seq, lat, lng, speed_kmh, heading_deg, fuel_pct, engine_temp_c
            FROM telemetry
            WHERE device_id = @deviceId AND ts >= @from AND ts < @to
            ORDER BY ts DESC
            LIMIT @limit;
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        cmd.Parameters.AddWithValue("from", from.UtcDateTime);
        cmd.Parameters.AddWithValue("to", to.UtcDateTime);
        cmd.Parameters.AddWithValue("limit", limit);

        var rows = new List<TelemetryEvent>(Math.Min(limit, 256));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new TelemetryEvent(
                reader.GetString(0),
                reader.GetString(1),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc), TimeSpan.Zero),
                reader.GetInt64(3),
                reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6),
                reader.GetDouble(7), reader.GetDouble(8), reader.GetDouble(9)));

        return rows;
    }

    public async Task<IReadOnlyList<TelemetryBucket>> QueryDownsampledAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, int buckets,
        CancellationToken ct = default)
    {
        // Tamaño de intervalo = rango / buckets (≥ 1 s). Se agrupa por el inicio del intervalo
        // (floor del epoch) → como mucho 'buckets' filas, con detalle fino en rangos cortos.
        var bucketSec = Math.Max(1, (long)((to - from).TotalSeconds / Math.Max(1, buckets)));
        const string sql = """
            SELECT to_timestamp(floor(extract(epoch FROM ts) / @bucketSec) * @bucketSec) AS bucket_ts,
                   count(*)            AS n,
                   avg(speed_kmh)      AS speed,
                   avg(fuel_pct)       AS fuel,
                   avg(engine_temp_c)  AS temp
            FROM telemetry
            WHERE device_id = @deviceId AND ts >= @from AND ts < @to
            GROUP BY bucket_ts
            ORDER BY bucket_ts;
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        cmd.Parameters.AddWithValue("from", from.UtcDateTime);
        cmd.Parameters.AddWithValue("to", to.UtcDateTime);
        cmd.Parameters.AddWithValue("bucketSec", bucketSec);

        var rows = new List<TelemetryBucket>(Math.Min(buckets, 256));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new TelemetryBucket(
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc), TimeSpan.Zero),
                (int)reader.GetInt64(1),
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4)));

        return rows;
    }

    public async Task<IReadOnlyList<string>> DropPartitionsBeforeAsync(
        DateOnly cutoff, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        // Particiones actuales de 'telemetry'.
        var names = (await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT inhrelid::regclass::text FROM pg_inherits WHERE inhparent = 'telemetry'::regclass;",
            cancellationToken: ct))).ToList();

        var dropped = new List<string>();
        foreach (var name in names)
        {
            var date = PartitionName.DateOf(name.Split('.').Last()); // puede venir con esquema
            if (date is null || date >= cutoff) continue;

            // Nombre validado (telemetry_pYYYYMMDD): seguro para interpolar.
            await conn.ExecuteAsync(new CommandDefinition(
                $"DROP TABLE IF EXISTS {PartitionName.ForDate(date.Value)};", cancellationToken: ct));
            dropped.Add(PartitionName.ForDate(date.Value));
        }
        return dropped;
    }

    /// <summary>
    /// Crea la partición diaria <c>telemetry_pYYYYMMDD</c> si falta. Idempotente y tolerante a
    /// carreras entre consumidores en paralelo (ignora "ya existe").
    /// </summary>
    private static async Task EnsurePartitionAsync(NpgsqlConnection conn, DateOnly date, CancellationToken ct)
    {
        var name = PartitionName.ForDate(date);
        var from = date.ToString("yyyy-MM-dd");
        var to = date.AddDays(1).ToString("yyyy-MM-dd");
        var ddl = $"""
            CREATE TABLE IF NOT EXISTS {name} PARTITION OF telemetry
              FOR VALUES FROM ('{from} 00:00:00+00') TO ('{to} 00:00:00+00');
            """;
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState is "42P07" or "23505" or "23P01")
        {
            // 42P07 duplicate_table · 23505 unique_violation (pg_class) · 23P01 carrera de partición.
        }
    }
}
