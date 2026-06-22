using System.Collections.Concurrent;
using Atalaya.Contracts;
using Atalaya.Persistence;

namespace Atalaya.Api.Services;

/// <summary>
/// Camino frío en memoria para dev sin Docker / tests (espejo de
/// <c>Atalaya.Persistence.PostgresTelemetryArchive</c>). Guarda hasta <c>PerDeviceCap</c> eventos
/// recientes por dispositivo; suficiente para la vista histórica local. <b>Objetivo:</b> tabla
/// <c>telemetry</c> particionada en Postgres (ADR-007).
/// </summary>
public sealed class InMemoryTelemetryArchive : ITelemetryArchive
{
    private const int PerDeviceCap = 5_000;

    private readonly ConcurrentDictionary<string, List<TelemetryEvent>> _byDevice = new();

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task AppendAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        foreach (var group in events.GroupBy(e => e.DeviceId))
        {
            var list = _byDevice.GetOrAdd(group.Key, _ => new List<TelemetryEvent>(256));
            lock (list)
            {
                list.AddRange(group);
                if (list.Count > PerDeviceCap)
                    list.RemoveRange(0, list.Count - PerDeviceCap);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TelemetryEvent>> QueryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, int limit = 1000,
        CancellationToken ct = default)
    {
        if (!_byDevice.TryGetValue(deviceId, out var list))
            return Task.FromResult<IReadOnlyList<TelemetryEvent>>([]);

        TelemetryEvent[] result;
        lock (list)
            result = list
                .Where(e => e.Ts >= from && e.Ts < to)
                .OrderByDescending(e => e.Ts)
                .Take(limit)
                .ToArray();

        return Task.FromResult<IReadOnlyList<TelemetryEvent>>(result);
    }
}
