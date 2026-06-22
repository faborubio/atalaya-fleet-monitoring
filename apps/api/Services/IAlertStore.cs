using System.Collections.Concurrent;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Read model de alertas para el modo dev sin Docker / tests (espejo de
/// <c>Atalaya.Persistence.IAlertRepository</c>). <b>Objetivo:</b> tabla <c>alerts</c> en Postgres,
/// escrita por el worker (ADR-005/007).
/// </summary>
public interface IAlertStore
{
    /// <summary>Inserta de forma idempotente (por <c>AlertId</c>) y devuelve las nuevas.</summary>
    IReadOnlyList<Alert> Insert(IReadOnlyList<Alert> alerts);

    /// <summary>Las alertas más recientes primero (snapshot del dashboard).</summary>
    IReadOnlyList<Alert> Snapshot(int limit = 100);
}

/// <summary>Versión en memoria, acotada a las últimas <c>Capacity</c> alertas.</summary>
public sealed class InMemoryAlertStore : IAlertStore
{
    private const int Capacity = 1_000;

    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private readonly LinkedList<Alert> _recent = new(); // cola: más nuevas al frente
    private readonly object _gate = new();

    public IReadOnlyList<Alert> Insert(IReadOnlyList<Alert> alerts)
    {
        var added = new List<Alert>(alerts.Count);
        lock (_gate)
        {
            foreach (var a in alerts)
            {
                if (!_seen.TryAdd(a.AlertId, 0)) continue; // ya vista: idempotente
                _recent.AddFirst(a);
                added.Add(a);
                if (_recent.Count > Capacity)
                {
                    _seen.TryRemove(_recent.Last!.Value.AlertId, out _);
                    _recent.RemoveLast();
                }
            }
        }
        return added;
    }

    public IReadOnlyList<Alert> Snapshot(int limit = 100)
    {
        lock (_gate)
            return _recent.Take(limit).ToArray();
    }
}
