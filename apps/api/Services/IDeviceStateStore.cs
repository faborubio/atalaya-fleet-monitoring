using System.Collections.Concurrent;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Read model del camino caliente (ADR-005): última posición/estado por dispositivo.
/// </summary>
public interface IDeviceStateStore
{
    /// <summary>Aplica un evento. Ignora los que llegan fuera de orden (seq menor).</summary>
    DeviceState Upsert(TelemetryEvent e);

    IReadOnlyCollection<DeviceState> Snapshot();
}

/// <summary>
/// Versión en memoria para dev. <b>Objetivo:</b> SQL particionado (read model
/// <c>device_state</c>), una fila por dispositivo (ADR-007).
/// </summary>
public sealed class InMemoryDeviceStateStore : IDeviceStateStore
{
    private readonly ConcurrentDictionary<string, DeviceState> _states = new();

    public DeviceState Upsert(TelemetryEvent e) => _states.AddOrUpdate(
        e.DeviceId,
        _ => DeviceState.FromEvent(e),
        (_, current) => e.Seq >= current.Seq ? DeviceState.FromEvent(e) : current);

    public IReadOnlyCollection<DeviceState> Snapshot() => _states.Values.ToArray();
}
