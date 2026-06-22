using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Atalaya.Contracts;

namespace Atalaya.Worker;

/// <summary>
/// Consumidor del camino caliente (ADR-008): long-polling de SQS por lotes; cada mensaje
/// es un lote de telemetría (array JSON publicado por la API vía SNS). Borra el mensaje
/// solo tras procesarlo (at-least-once → tras 5 fallos va a la DLQ por la redrive policy).
/// <para>Paso 1: deserializa y contabiliza. En los siguientes pasos se añade dedup (Redis),
/// read model (Postgres) y push por SignalR (backplane Redis).</para>
/// </summary>
public sealed class SqsTelemetryConsumer(
    IAmazonSQS sqs,
    AwsOptions options,
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

            foreach (var msg in resp.Messages)
            {
                try
                {
                    var batch = JsonSerializer.Deserialize<TelemetryEvent[]>(msg.Body, Json) ?? [];
                    _processed += batch.Length;
                    await sqs.DeleteMessageAsync(queueUrl, msg.ReceiptHandle, stoppingToken);
                }
                catch (Exception ex)
                {
                    // No se borra: SQS lo reentrega y, tras maxReceiveCount, va a la DLQ.
                    logger.LogError(ex, "Error procesando mensaje; se reintentará");
                }
            }

            logger.LogInformation("Eventos procesados (acumulado): {Count}", _processed);
        }
    }
}
