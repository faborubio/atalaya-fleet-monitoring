using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Atalaya.Contracts;
using Atalaya.Persistence;
using Atalaya.Realtime;

namespace Atalaya.Worker;

/// <summary>
/// Consumidor del camino caliente (ADR-008): long-polling de SQS por lotes; cada mensaje
/// es un lote de telemetría (array JSON publicado por la API vía SNS). Tras procesar el
/// ciclo, hace upsert del read model en Postgres (ADR-005) y borra los mensajes
/// (at-least-once → tras 5 fallos van a la DLQ por la redrive policy).
/// </summary>
public sealed class SqsTelemetryConsumer(
    IAmazonSQS sqs,
    AwsOptions options,
    IEventDeduplicator deduplicator,
    IDeviceStateRepository repository,
    ITelemetryBroadcaster broadcaster,
    ILogger<SqsTelemetryConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private long _processed;
    private long _duplicates;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = (await sqs.GetQueueUrlAsync(options.QueueName, stoppingToken)).QueueUrl;
        logger.LogInformation("Consumiendo SQS: {Queue}", queueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            var resp = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 20, // long polling
            }, stoppingToken);

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
                    // No se borra: SQS lo reentrega y, tras maxReceiveCount, va a la DLQ.
                    logger.LogError(ex, "Mensaje inválido; se reintentará");
                }
            }

            // Dedup idempotente (ADR-006): descarta lo ya visto antes de aplicar efectos.
            var fresh = await deduplicator.FilterNewAsync(events, stoppingToken);
            _duplicates += events.Count - fresh.Count;

            if (fresh.Count > 0)
            {
                var deltas = fresh.Select(DeviceState.FromEvent).ToList();
                await repository.UpsertAsync(deltas, stoppingToken);
                await broadcaster.PublishDeltasAsync(deltas, stoppingToken); // push en vivo (ADR-002)
                _processed += fresh.Count;
            }

            if (handled.Count > 0)
                await sqs.DeleteMessageBatchAsync(queueUrl, handled, stoppingToken);

            logger.LogInformation(
                "Read model (acumulado): aplicados={Applied} duplicados={Dups}",
                _processed, _duplicates);
        }
    }
}
