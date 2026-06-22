using Atalaya.Api.Hubs;
using Atalaya.Api.Services;
using Atalaya.Contracts;
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
    IAlertStore alertStore,
    IHubContext<TelemetryHub> hub,
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
                await hub.Clients.All.SendAsync("devicesUpdated", deltas, stoppingToken);

            // Reglas por umbral (Fase 2): idéntico al worker, pero sobre el store en memoria.
            var raised = fresh.SelectMany(AlertRules.Evaluate).ToList();
            if (raised.Count > 0)
            {
                var newAlerts = alertStore.Insert(raised);
                if (newAlerts.Count > 0)
                    await hub.Clients.All.SendAsync("alertsRaised", newAlerts, stoppingToken);
            }
        }
    }
}
