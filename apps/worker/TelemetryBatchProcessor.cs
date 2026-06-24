using Atalaya.Contracts;
using Atalaya.Persistence;
using Atalaya.Realtime;

namespace Atalaya.Worker;

/// <summary>
/// Procesamiento de un lote de telemetría, compartido por los consumidores de cada broker (SQS y
/// Pub/Sub, ADR-013): dedup (ADR-006) → upsert read model (ADR-005) → push de deltas (ADR-002) →
/// camino frío (ADR-007) → reglas de alerta como incidentes (AUD-016/p1). El consumidor solo aporta
/// el transporte (recibir/ack); la lógica de dominio es idéntica e independiente del broker.
/// </summary>
public sealed class TelemetryBatchProcessor(
    IEventDeduplicator deduplicator,
    IDeviceStateRepository repository,
    IAlertIncidentStore alertIncidents,
    ITelemetryArchive telemetryArchive,
    IRawEventArchive rawArchive,
    ITelemetryBroadcaster broadcaster,
    IAlertBroadcaster alertBroadcaster,
    WorkerMetrics metrics)
{
    public async Task ProcessAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;

        // Dedup idempotente (ADR-006): descarta lo ya visto antes de aplicar efectos.
        var fresh = await deduplicator.FilterNewAsync(events, ct);
        var dups = events.Count - fresh.Count;
        if (dups > 0) metrics.AddDuplicates(dups);
        if (fresh.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var e in fresh)
            metrics.RecordLatency((now - e.Ts).TotalMilliseconds);

        var deltas = fresh.Select(DeviceState.FromEvent).ToList();
        await repository.UpsertAsync(deltas, ct);
        await broadcaster.PublishDeltasAsync(deltas, ct);
        metrics.AddProcessed(fresh.Count);

        // Camino frío (ADR-007): telemetría cruda a SQL particionado + data lake.
        await telemetryArchive.AppendAsync(fresh, ct);
        await rawArchive.AppendAsync(fresh, ct);
        metrics.AddArchived(fresh.Count);

        // Reglas por umbral como incidentes (AUD-016/p1): evalúa señales, aplica la máquina de
        // estados y notifica solo las transiciones.
        var readings = fresh.SelectMany(AlertRules.Read).ToList();
        if (readings.Count > 0)
        {
            var transitions = await alertIncidents.ApplyAsync(readings, ct);
            if (transitions.Count > 0)
            {
                await alertBroadcaster.PublishAlertsAsync(transitions, ct);
                metrics.AddAlerts(transitions.Count);
            }
        }
    }
}
