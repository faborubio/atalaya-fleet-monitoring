using System.Text.Json;
using System.Threading.Channels;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Drena la cola del <see cref="QueueingTelemetryPublisher"/> y publica a SNS por lotes
/// (AUD-009). Saca el round-trip a SNS del camino de la petición y reduce el número de
/// llamadas: en vez de un <c>PublishAsync</c> por request, agrupa eventos en mensajes
/// (cada uno un array JSON, como espera el worker) y manda hasta 10 mensajes por
/// <c>PublishBatch</c> (límite de SNS). Una ventana de coalescencia corta acota la latencia
/// añadida cuando el tráfico es bajo.
/// <para>El ARN del topic se resuelve una vez al arrancar (CreateTopic es idempotente).</para>
/// </summary>
public sealed class SnsBatchPublisher(
    QueueingTelemetryPublisher queue,
    IAmazonSimpleNotificationService sns,
    AwsOptions options,
    ILogger<SnsBatchPublisher> logger) : BackgroundService
{
    private const int MaxBatchEntries = 10; // límite de SNS PublishBatch

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var arn = (await sns.CreateTopicAsync(options.TopicName, stoppingToken)).TopicArn;
        var reader = queue.Reader;
        var maxEventsPerMessage = Math.Max(1, options.MessageMaxEvents);
        var flushMs = Math.Max(0, options.FlushMilliseconds);
        var pending = new List<TelemetryEvent>(maxEventsPerMessage * MaxBatchEntries);

        logger.LogInformation(
            "Publicador SNS por lotes activo (≤{Msgs} msgs × {Evts} ev/msg, ventana {Ms} ms).",
            MaxBatchEntries, maxEventsPerMessage, flushMs);

        while (await reader.WaitToReadAsync(stoppingToken))
        {
            Drain(reader, pending);
            if (flushMs > 0)
            {
                await Task.Delay(flushMs, stoppingToken); // coalesce la ráfaga
                Drain(reader, pending);
            }

            await FlushAsync(arn, pending, maxEventsPerMessage, stoppingToken);
        }
    }

    private static void Drain(ChannelReader<TelemetryEvent> reader, List<TelemetryEvent> buffer)
    {
        while (reader.TryRead(out var e)) buffer.Add(e);
    }

    private async Task FlushAsync(
        string arn, List<TelemetryEvent> events, int maxEventsPerMessage, CancellationToken ct)
    {
        if (events.Count == 0) return;

        var entries = new List<PublishBatchRequestEntry>(MaxBatchEntries);
        for (var i = 0; i < events.Count; i += maxEventsPerMessage)
        {
            var count = Math.Min(maxEventsPerMessage, events.Count - i);
            var slice = events.GetRange(i, count); // un mensaje = array JSON (el worker lo expande)
            entries.Add(new PublishBatchRequestEntry { Message = JsonSerializer.Serialize(slice) });

            if (entries.Count == MaxBatchEntries)
                await SendBatchAsync(arn, entries, ct);
        }

        if (entries.Count > 0)
            await SendBatchAsync(arn, entries, ct);

        events.Clear();
    }

    private async Task SendBatchAsync(string arn, List<PublishBatchRequestEntry> entries, CancellationToken ct)
    {
        for (var i = 0; i < entries.Count; i++)
            entries[i].Id = i.ToString(); // Id único dentro de la petición (lo exige SNS)

        try
        {
            var resp = await sns.PublishBatchAsync(new PublishBatchRequest
            {
                TopicArn = arn,
                PublishBatchRequestEntries = entries,
            }, ct);

            if (resp.Failed is { Count: > 0 })
                logger.LogError("SNS PublishBatch: {N} mensajes fallidos (se pierden)", resp.Failed.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo publicando lote de {N} mensajes a SNS (se pierden)", entries.Count);
        }
        finally
        {
            entries.Clear();
        }
    }
}
