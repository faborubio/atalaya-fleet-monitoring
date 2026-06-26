using Atalaya.Contracts;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Atalaya.Persistence;

/// <summary>
/// Implementación Postgres del read model <c>alert_incidents</c> (AUD-016/p1). Una fila por
/// <c>(device_id, rule)</c>. <see cref="ApplyAsync"/> lee el estado actual de las claves del lote,
/// decide las transiciones con <see cref="IncidentTransitions"/> y persiste por lote con
/// <c>unnest … ON CONFLICT … DO UPDATE</c>; devuelve solo lo que transicionó.
/// </summary>
public sealed class PostgresAlertIncidentStore(
    IOptions<PostgresOptions> options, IncidentOptions? incidentOptions = null) : IAlertIncidentStore
{
    private readonly string _cs = options.Value.ConnectionString;
    private readonly TimeSpan _cooldown = (incidentOptions ?? new IncidentOptions()).Cooldown;

    // Caché de incidentes abiertos para no consultar la BD por cada lote de telemetría normal:
    // un evento normal emite señales Clear, pero solo importan si su clave tiene incidente abierto.
    // Coherente en un único proceso worker (singleton); a varias instancias requeriría particionar
    // por dispositivo (FIFO) — deuda documentada (AUD-008/015).
    private readonly HashSet<string> _open = [];
    private readonly object _gate = new();
    private bool _hydrated;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS alert_incidents (
              device_id  text NOT NULL,
              rule       text NOT NULL,
              severity   text NOT NULL,
              status     text NOT NULL,
              value      double precision NOT NULL,
              opened_at  timestamptz NOT NULL,
              updated_at timestamptz NOT NULL,
              message    text NOT NULL,
              PRIMARY KEY (device_id, rule)
            );
            CREATE INDEX IF NOT EXISTS ix_alert_incidents_updated ON alert_incidents (updated_at DESC);
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AlertIncident>> ApplyAsync(
        IReadOnlyList<RuleReading> readings, CancellationToken ct = default)
    {
        if (readings.Count == 0) return [];

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await HydrateOpenAsync(conn, ct);

        // Solo importan: los Firing (pueden abrir/escalar) y los Clear de incidentes abiertos.
        List<RuleReading> candidates;
        lock (_gate)
            candidates = IncidentTransitions.Latest(readings)
                .Where(r => r.Signal == RuleSignal.Firing
                            || _open.Contains(AlertIncident.Id(r.DeviceId, r.Rule)))
                .ToList();
        if (candidates.Count == 0) return [];

        // Estado actual de las claves candidatas.
        const string selectSql = """
            SELECT device_id, rule, severity, status, value, opened_at, updated_at, message
            FROM alert_incidents
            WHERE (device_id, rule) IN (SELECT * FROM unnest(@devices::text[], @rules::text[]));
            """;
        var current = new Dictionary<string, AlertIncident>();
        await using (var sel = new NpgsqlCommand(selectSql, conn))
        {
            sel.Parameters.AddWithValue("devices", candidates.Select(r => r.DeviceId).ToArray());
            sel.Parameters.AddWithValue("rules", candidates.Select(r => r.Rule).ToArray());
            await using var reader = await sel.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var inc = Read(reader);
                current[inc.IncidentId] = inc;
            }
        }

        // Decide y persiste solo las transiciones (abrir/escalar/resolver).
        var transitions = new List<AlertIncident>();
        foreach (var r in candidates)
        {
            current.TryGetValue(AlertIncident.Id(r.DeviceId, r.Rule), out var cur);
            var (next, transition) = IncidentTransitions.Decide(cur, r, _cooldown);
            if (next is null || !transition) continue;
            transitions.Add(next);
            lock (_gate)
            {
                if (next.Status == IncidentStatus.Open) _open.Add(next.IncidentId);
                else _open.Remove(next.IncidentId);
            }
        }

        if (transitions.Count > 0)
            await UpsertAsync(conn, transitions, ct);

        return transitions;
    }

    public async Task<IReadOnlyList<AlertIncident>> GetActiveAsync(int limit = 100, CancellationToken ct = default)
    {
        const string sql = """
            SELECT device_id, rule, severity, status, value, opened_at, updated_at, message
            FROM alert_incidents
            ORDER BY (status = 'Open') DESC, updated_at DESC
            LIMIT @limit;
            """;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("limit", limit);

        var rows = new List<AlertIncident>(limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(Read(reader));
        return rows;
    }

    /// <summary>Carga una vez las claves de incidentes abiertos (estado tras un reinicio).</summary>
    private async Task HydrateOpenAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        lock (_gate)
            if (_hydrated) return;

        var open = (await conn.QueryAsync<(string DeviceId, string Rule)>(new CommandDefinition(
            "SELECT device_id, rule FROM alert_incidents WHERE status = 'Open';",
            cancellationToken: ct))).ToList();

        lock (_gate)
        {
            if (!_hydrated)
            {
                foreach (var (d, r) in open) _open.Add(AlertIncident.Id(d, r));
                _hydrated = true;
            }
        }
    }

    private static async Task UpsertAsync(NpgsqlConnection conn, List<AlertIncident> incidents, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO alert_incidents
              (device_id, rule, severity, status, value, opened_at, updated_at, message)
            SELECT * FROM unnest(
              @devices::text[], @rules::text[], @sev::text[], @status::text[],
              @value::float8[], @opened::timestamptz[], @updated::timestamptz[], @msg::text[])
            ON CONFLICT (device_id, rule) DO UPDATE SET
              severity = EXCLUDED.severity, status = EXCLUDED.status, value = EXCLUDED.value,
              opened_at = EXCLUDED.opened_at, updated_at = EXCLUDED.updated_at, message = EXCLUDED.message
            WHERE EXCLUDED.updated_at >= alert_incidents.updated_at;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("devices", incidents.Select(i => i.DeviceId).ToArray());
        cmd.Parameters.AddWithValue("rules", incidents.Select(i => i.Rule).ToArray());
        cmd.Parameters.AddWithValue("sev", incidents.Select(i => i.Severity.ToString()).ToArray());
        cmd.Parameters.AddWithValue("status", incidents.Select(i => i.Status.ToString()).ToArray());
        cmd.Parameters.AddWithValue("value", incidents.Select(i => i.Value).ToArray());
        cmd.Parameters.AddWithValue("opened", incidents.Select(i => i.OpenedAt.UtcDateTime).ToArray());
        cmd.Parameters.AddWithValue("updated", incidents.Select(i => i.UpdatedAt.UtcDateTime).ToArray());
        cmd.Parameters.AddWithValue("msg", incidents.Select(i => i.Message).ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AlertIncident Read(NpgsqlDataReader r)
    {
        var deviceId = r.GetString(0);
        var rule = r.GetString(1);
        return new AlertIncident(
            AlertIncident.Id(deviceId, rule),
            deviceId,
            rule,
            Enum.Parse<AlertSeverity>(r.GetString(2)),
            Enum.Parse<IncidentStatus>(r.GetString(3)),
            r.GetDouble(4),
            Utc(r.GetDateTime(5)),
            Utc(r.GetDateTime(6)),
            r.GetString(7));
    }

    private static DateTimeOffset Utc(DateTime dt) =>
        new(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
}
