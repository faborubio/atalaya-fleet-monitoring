using Atalaya.Api.Hubs;
using Atalaya.Api.Services;
using Atalaya.Contracts;
using Atalaya.Persistence;
using Microsoft.AspNetCore.SignalR;

namespace Atalaya.Api.Processing;

/// <summary>
/// Consumidor del camino caliente. Drena el bus por lotes (coalescencia de ráfagas),
/// deduplica (ADR-006), actualiza el read model (ADR-005) y empuja los deltas por
/// SignalR (ADR-002).
/// <para><b>Nota de arquitectura:</b> en el modo dev sin Docker este consumidor vive en
/// el proceso de la API. En la arquitectura objetivo (ADR-008) corre en el
/// <c>worker</c> .NET consumiendo SQS; el código de procesamiento es el mismo.</para>
/// </summary>
public sealed class TelemetryProcessor(
    ITelemetryBus bus,
    IDeduplicator deduplicator,
    IDeviceStateStore store,
    IAlertIncidentStore alertIncidents,
    ITelemetryArchive telemetryArchive,
    IHubContext<TelemetryHub> hub,
    ViewportRegistry viewport,
    ILogger<TelemetryProcessor> logger) : BackgroundService
{
    private const int MaxBatch = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TelemetryProcessor iniciado (modo dev: bus en memoria).");
        var reader = bus.Reader;
        var deltas = new List<DeviceState>(MaxBatch);
        var fresh = new List<TelemetryEvent>(MaxBatch);

        while (await reader.WaitToReadAsync(stoppingToken))
        {
            deltas.Clear();
            fresh.Clear();
            while (deltas.Count < MaxBatch && reader.TryRead(out var e))
            {
                if (!deduplicator.TryMarkProcessed(e.EventId)) continue;
                deltas.Add(store.Upsert(e));
                fresh.Add(e);
            }

            if (deltas.Count > 0)
                await SendAsync(deltas, stoppingToken);

            // Camino frío (ADR-007): archiva la telemetría cruda para la vista histórica.
            if (fresh.Count > 0)
                await telemetryArchive.AppendAsync(fresh, stoppingToken);

            // Reglas por umbral como incidentes (AUD-016/p1): idéntico al worker, en memoria.
            var readings = fresh.SelectMany(AlertRules.Read).ToList();
            if (readings.Count > 0)
            {
                var transitions = await alertIncidents.ApplyAsync(readings, stoppingToken);
                if (transitions.Count > 0)
                    await hub.Clients.All.SendAsync("alertsRaised", transitions, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Envío dual de deltas (AUD-008), igual que el <see cref="RedisDeltaForwarder"/>: firehose a
    /// los clientes normales y grupos por dispositivo a los que están en modo viewport.
    /// </summary>
    private async Task SendAsync(IReadOnlyList<DeviceState> deltas, CancellationToken ct)
    {
        var viewportConns = viewport.ConnectionsInViewportMode();
        if (viewportConns.Count == 0)
        {
            await hub.Clients.All.SendAsync("devicesUpdated", deltas, ct);
            return;
        }

        await hub.Clients.AllExcept(viewportConns).SendAsync("devicesUpdated", deltas, ct);
        var subscribed = viewport.SubscribedDevices();
        foreach (var d in deltas)
            if (subscribed.Contains(d.DeviceId))
                await hub.Clients.Group(TelemetryHub.Group(d.DeviceId))
                    .SendAsync("devicesUpdated", new[] { d }, ct);
    }
}
