using Atalaya.Contracts;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Atalaya.Persistence;

/// <summary>
/// Implementación Postgres del read model. La escritura usa <c>unnest</c> para un upsert
/// por lote en un solo viaje (alinea con "escrituras batched", SAD §9); el guard
/// <c>seq &gt;= device_state.seq</c> ignora eventos fuera de orden.
/// </summary>
public sealed class PostgresDeviceStateRepository(IOptions<PostgresOptions> options)
    : IDeviceStateRepository
{
    private readonly string _cs = options.Value.ConnectionString;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS device_state (
              device_id     text PRIMARY KEY,
              ts            timestamptz NOT NULL,
              seq           bigint NOT NULL,
              lat           double precision NOT NULL,
              lng           double precision NOT NULL,
              speed_kmh     double precision NOT NULL,
              heading_deg   double precision NOT NULL,
              fuel_pct      double precision NOT NULL,
              engine_temp_c double precision NOT NULL,
              updated_at    timestamptz NOT NULL DEFAULT now()
            );
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));
    }

    public async Task UpsertAsync(IReadOnlyList<DeviceState> states, CancellationToken ct = default)
    {
        if (states.Count == 0) return;

        // Un único estado por dispositivo (el de mayor seq): ON CONFLICT no puede tocar
        // la misma fila dos veces en una sentencia.
        var latest = states
            .GroupBy(s => s.DeviceId)
            .Select(g => g.MaxBy(s => s.Seq)!)
            .ToArray();

        const string sql = """
            INSERT INTO device_state
              (device_id, ts, seq, lat, lng, speed_kmh, heading_deg, fuel_pct, engine_temp_c)
            SELECT * FROM unnest(
              @ids::text[], @ts::timestamptz[], @seq::bigint[], @lat::float8[], @lng::float8[],
              @speed::float8[], @heading::float8[], @fuel::float8[], @temp::float8[])
            ON CONFLICT (device_id) DO UPDATE SET
              ts = EXCLUDED.ts, seq = EXCLUDED.seq, lat = EXCLUDED.lat, lng = EXCLUDED.lng,
              speed_kmh = EXCLUDED.speed_kmh, heading_deg = EXCLUDED.heading_deg,
              fuel_pct = EXCLUDED.fuel_pct, engine_temp_c = EXCLUDED.engine_temp_c,
              updated_at = now()
            WHERE EXCLUDED.seq >= device_state.seq;
            """;

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", latest.Select(s => s.DeviceId).ToArray());
        cmd.Parameters.AddWithValue("ts", latest.Select(s => s.Ts.UtcDateTime).ToArray());
        cmd.Parameters.AddWithValue("seq", latest.Select(s => s.Seq).ToArray());
        cmd.Parameters.AddWithValue("lat", latest.Select(s => s.Lat).ToArray());
        cmd.Parameters.AddWithValue("lng", latest.Select(s => s.Lng).ToArray());
        cmd.Parameters.AddWithValue("speed", latest.Select(s => s.SpeedKmh).ToArray());
        cmd.Parameters.AddWithValue("heading", latest.Select(s => s.HeadingDeg).ToArray());
        cmd.Parameters.AddWithValue("fuel", latest.Select(s => s.FuelPct).ToArray());
        cmd.Parameters.AddWithValue("temp", latest.Select(s => s.EngineTempC).ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceState>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT device_id AS DeviceId, ts AS Ts, seq AS Seq, lat AS Lat, lng AS Lng,
                   speed_kmh AS SpeedKmh, heading_deg AS HeadingDeg,
                   fuel_pct AS FuelPct, engine_temp_c AS EngineTempC
            FROM device_state;
            """;
        await using var conn = new NpgsqlConnection(_cs);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => new DeviceState(
            r.DeviceId,
            new DateTimeOffset(DateTime.SpecifyKind(r.Ts, DateTimeKind.Utc), TimeSpan.Zero),
            r.Seq, r.Lat, r.Lng, r.SpeedKmh, r.HeadingDeg, r.FuelPct, r.EngineTempC)).ToArray();
    }

    private sealed record Row(
        string DeviceId, DateTime Ts, long Seq, double Lat, double Lng,
        double SpeedKmh, double HeadingDeg, double FuelPct, double EngineTempC);
}
