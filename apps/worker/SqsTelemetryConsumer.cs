using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Atalaya.Contracts;

namespace Atalaya.Worker;

/// <summary>
/// Consumidor del camino caliente para AWS SQS (ADR-008). Arranca N bucles de long-polling en
/// paralelo (competing consumers) sobre la misma cola para sostener mayor throughput; cada lote se
/// delega al <see cref="TelemetryBatchProcessor"/> (lógica común a todos los brokers). Borra los
/// mensajes tras procesar (at-least-once → DLQ tras 5 fallos por redrive).
/// </summary>
public sealed class SqsTelemetryConsumer(
    IAmazonSQS sqs,
    AwsOptions options,
    TelemetryBatchProcessor processor,
    ILogger<SqsTelemetryConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

            await processor.ProcessAsync(events, ct);

            if (handled.Count > 0)
                await sqs.DeleteMessageBatchAsync(queueUrl, handled, ct);
        }
    }
}
