using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Atalaya.Contracts;
using Atalaya.Persistence;

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
    IDeviceStateRepository repository,
    ILogger<SqsTelemetryConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private long _processed;

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

            var deltas = new List<DeviceState>(resp.Messages.Count * 64);
            var handled = new List<DeleteMessageBatchRequestEntry>(resp.Messages.Count);

            foreach (var msg in resp.Messages)
            {
                try
                {
                    var batch = JsonSerializer.Deserialize<TelemetryEvent[]>(msg.Body, Json) ?? [];
                    foreach (var e in batch) deltas.Add(DeviceState.FromEvent(e));
                    handled.Add(new DeleteMessageBatchRequestEntry(msg.MessageId, msg.ReceiptHandle));
                }
                catch (Exception ex)
                {
                    // No se borra: SQS lo reentrega y, tras maxReceiveCount, va a la DLQ.
                    logger.LogError(ex, "Mensaje inválido; se reintentará");
                }
            }

            if (deltas.Count > 0)
            {
                await repository.UpsertAsync(deltas, stoppingToken);
                _processed += deltas.Count;
            }

            if (handled.Count > 0)
                await sqs.DeleteMessageBatchAsync(queueUrl, handled, stoppingToken);

            logger.LogInformation("Eventos aplicados al read model (acumulado): {Count}", _processed);
        }
    }
}
