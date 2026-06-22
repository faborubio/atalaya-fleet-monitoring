using Atalaya.Contracts;

namespace Atalaya.Realtime;

/// <summary>
/// Publica deltas del read model hacia los clientes en vivo a través de Redis (ADR-002).
/// El worker publica; la API (que mantiene las conexiones SignalR) reenvía a los navegadores.
/// Es un puente pub/sub sobre Redis — equivalente funcional del backplane de SignalR, que
/// sería el paso de productivización.
/// </summary>
public interface ITelemetryBroadcaster
{
    Task PublishDeltasAsync(IReadOnlyList<DeviceState> deltas, CancellationToken ct = default);
}
