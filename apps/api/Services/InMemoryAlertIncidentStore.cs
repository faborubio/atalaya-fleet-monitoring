using System.Collections.Concurrent;
using Atalaya.Contracts;
using Atalaya.Persistence;

namespace Atalaya.Api.Services;

/// <summary>
/// Store de incidentes en memoria para dev sin Docker / tests (espejo de
/// <c>PostgresAlertIncidentStore</c>). Misma máquina de estados (<see cref="IncidentTransitions"/>),
/// estado por <c>(deviceId, rule)</c>.
/// </summary>
public sealed class InMemoryAlertIncidentStore(IncidentOptions? options = null) : IAlertIncidentStore
{
    private readonly ConcurrentDictionary<string, AlertIncident> _byKey = new();
    private readonly object _gate = new();
    private readonly TimeSpan _cooldown = (options ?? new IncidentOptions()).Cooldown;

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<AlertIncident>> ApplyAsync(
        IReadOnlyList<RuleReading> readings, CancellationToken ct = default)
    {
        var transitions = new List<AlertIncident>();
        lock (_gate)
        {
            foreach (var r in IncidentTransitions.Latest(readings))
            {
                _byKey.TryGetValue(AlertIncident.Id(r.DeviceId, r.Rule), out var cur);
                var (next, transition) = IncidentTransitions.Decide(cur, r, _cooldown);
                if (next is null) continue;
                _byKey[next.IncidentId] = next;
                if (transition) transitions.Add(next);
            }
        }
        return Task.FromResult<IReadOnlyList<AlertIncident>>(transitions);
    }

    public Task<IReadOnlyList<AlertIncident>> GetActiveAsync(int limit = 100, CancellationToken ct = default)
    {
        IReadOnlyList<AlertIncident> snapshot;
        lock (_gate)
            snapshot = _byKey.Values
                .OrderByDescending(i => i.Status == IncidentStatus.Open)
                .ThenByDescending(i => i.UpdatedAt)
                .Take(limit)
                .ToArray();
        return Task.FromResult(snapshot);
    }
}
