using Atalaya.Contracts;

namespace Atalaya.Realtime;

/// <summary>
/// Publica alertas nuevas hacia los clientes en vivo a través de Redis (ADR-002), igual que
/// <see cref="ITelemetryBroadcaster"/> pero en su propio canal. El worker publica; la API
/// (dueña de las conexiones SignalR) reenvía a los navegadores.
/// </summary>
public interface IAlertBroadcaster
{
    Task PublishAlertsAsync(IReadOnlyList<AlertIncident> incidents, CancellationToken ct = default);
}
