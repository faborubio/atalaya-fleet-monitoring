using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Atalaya.Contracts;
using Atalaya.Persistence;
using Atalaya.Realtime;

namespace Atalaya.Worker;

/// <summary>
/// Consumidor del camino caliente (ADR-008). Arranca N bucles de long-polling en paralelo
/// (competing consumers) sobre la misma cola para sostener mayor throughput. Cada lote:
/// dedup (ADR-006) → upsert read model (ADR-005) → push de deltas (ADR-002), con métricas
/// OTel. Borra los mensajes tras procesar (at-least-once → DLQ tras 5 fallos por redrive).
/// </summary>
public sealed class SqsTelemetryConsumer(
    IAmazonSQS sqs,
    AwsOptions options,
    IEventDeduplicator deduplicator,
    IDeviceStateRepository repository,
    IAlertRepository alertRepository,
    ITelemetryBroadcaster broadcaster,
    IAlertBroadcaster alertBroadcaster,
    WorkerMetrics metrics,
    ILogger<SqsTelemetryConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private long _processed;
    private long _duplicates;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = (await sqs.GetQueueUrlAsync(options.QueueName, stoppingToken)).QueueUrl;
        var consumers = Math.Max(1, options.Consumers);
        logger.LogInformation("Consumiendo SQS {Queue} con {N} consumidores", queueUrl, consumers);

        var loops = Enumerable.Range(0, consumers)
            .Select(i => ConsumeLoopAsync(queueUrl, i, stoppingToken));
        await Task.WhenAll(loops);
    }

    private async Task ConsumeLoopAsync(string queueUrl, int id, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ReceiveMessageResponse resp;
            try
            {
                resp = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20, // long polling
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Consumidor {Id}: error recibiendo; reintenta", id);
                continue;
            }

            if (resp.Messages is not { Count: > 0 }) continue;

            var events = new List<TelemetryEvent>(resp.Messages.Count * 64);
            var handled = new List<DeleteMessageBatchRequestEntry>(resp.Messages.Count);
            foreach (var msg in resp.Messages)
            {
                try
                {
                    var batch = JsonSerializer.Deserialize<TelemetryEvent[]>(msg.Body, Json) ?? [];
                    events.AddRange(batch);
                    handled.Add(new DeleteMessageBatchRequestEntry(msg.MessageId, msg.ReceiptHandle));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Mensaje inválido; se reintentará");
                }
            }

            // Dedup idempotente (ADR-006): descarta lo ya visto antes de aplicar efectos.
            var fresh = await deduplicator.FilterNewAsync(events, ct);
            var dups = events.Count - fresh.Count;
            if (dups > 0) { Interlocked.Add(ref _duplicates, dups); metrics.AddDuplicates(dups); }

            if (fresh.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var e in fresh)
                    metrics.RecordLatency((now - e.Ts).TotalMilliseconds);

                var deltas = fresh.Select(DeviceState.FromEvent).ToList();
                await repository.UpsertAsync(deltas, ct);
                await broadcaster.PublishDeltasAsync(deltas, ct);
                Interlocked.Add(ref _processed, fresh.Count);
                metrics.AddProcessed(fresh.Count);

                // Reglas por umbral (Fase 2): evalúa sobre los eventos frescos, persiste de forma
                // idempotente y notifica solo las alertas realmente nuevas (ADR-005/006).
                var raised = fresh.SelectMany(AlertRules.Evaluate).ToList();
                if (raised.Count > 0)
                {
                    var newAlerts = await alertRepository.InsertAsync(raised, ct);
                    if (newAlerts.Count > 0)
                    {
                        await alertBroadcaster.PublishAlertsAsync(newAlerts, ct);
                        metrics.AddAlerts(newAlerts.Count);
                    }
                }
            }

            if (handled.Count > 0)
                await sqs.DeleteMessageBatchAsync(queueUrl, handled, ct);
        }
    }
}
