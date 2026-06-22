using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Drena la cola del <see cref="QueueingTelemetryPublisher"/> y publica a SNS por lotes
/// (AUD-010). Saca el round-trip a SNS del camino de la petición y reduce el número de
/// llamadas: en vez de un <c>PublishAsync</c> por request, agrupa eventos en mensajes
/// (cada uno un array JSON, como espera el worker) y manda hasta 10 mensajes por
/// <c>PublishBatch</c> (límite de SNS). Una ventana de coalescencia corta acota la latencia
/// añadida cuando el tráfico es bajo.
/// <para>El armado de lotes respeta los dos límites de SNS PublishBatch: ≤10 mensajes y
/// ≤256 KB por lote (con margen); ver <see cref="PlanBatches"/>.</para>
/// <para>El ARN del topic se resuelve una vez al arrancar (CreateTopic es idempotente).</para>
/// </summary>
public sealed class SnsBatchPublisher(
    QueueingTelemetryPublisher queue,
    IAmazonSimpleNotificationService sns,
    AwsOptions options,
    ILogger<SnsBatchPublisher> logger) : BackgroundService
{
    private const int MaxBatchEntries = 10;          // límite de SNS PublishBatch
    private const int MaxBatchBytes = 240 * 1024;    // 256 KB de SNS con margen para Ids/sobrecarga

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var arn = (await sns.CreateTopicAsync(options.TopicName, stoppingToken)).TopicArn;
        var reader = queue.Reader;
        var maxEventsPerMessage = Math.Max(1, options.MessageMaxEvents);
        var flushMs = Math.Max(0, options.FlushMilliseconds);
        var pending = new List<TelemetryEvent>(maxEventsPerMessage * MaxBatchEntries);

        logger.LogInformation(
            "Publicador SNS por lotes activo (≤{Msgs} msgs × {Evts} ev/msg, ≤{KB} KB/lote, ventana {Ms} ms).",
            MaxBatchEntries, maxEventsPerMessage, MaxBatchBytes / 1024, flushMs);

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

        foreach (var batch in PlanBatches(events, maxEventsPerMessage))
            await SendBatchAsync(arn, batch, ct);

        events.Clear();
    }

    /// <summary>
    /// Trocea los eventos en mensajes (≤<paramref name="maxEventsPerMessage"/> eventos, cada uno
    /// un array JSON) y los agrupa en lotes que respetan los límites de SNS PublishBatch:
    /// ≤10 mensajes y ≤256 KB (con margen) por lote. Preserva el orden de los eventos.
    /// Puro y sin dependencias de SNS para poder testearlo.
    /// </summary>
    internal static List<List<string>> PlanBatches(IReadOnlyList<TelemetryEvent> events, int maxEventsPerMessage)
    {
        var batches = new List<List<string>>();
        var current = new List<string>(MaxBatchEntries);
        var currentBytes = 0;

        for (var i = 0; i < events.Count; i += maxEventsPerMessage)
        {
            var count = Math.Min(maxEventsPerMessage, events.Count - i);
            var slice = new TelemetryEvent[count];
            for (var j = 0; j < count; j++) slice[j] = events[i + j];

            var body = JsonSerializer.Serialize(slice);
            var bytes = Encoding.UTF8.GetByteCount(body);

            // Cierra el lote en curso si añadir este mensaje superaría algún límite de SNS.
            if (current.Count > 0 &&
                (current.Count >= MaxBatchEntries || currentBytes + bytes > MaxBatchBytes))
            {
                batches.Add(current);
                current = new List<string>(MaxBatchEntries);
                currentBytes = 0;
            }

            current.Add(body);
            currentBytes += bytes;
        }

        if (current.Count > 0) batches.Add(current);
        return batches;
    }

    private async Task SendBatchAsync(string arn, List<string> bodies, CancellationToken ct)
    {
        var entries = new List<PublishBatchRequestEntry>(bodies.Count);
        for (var i = 0; i < bodies.Count; i++)
            entries.Add(new PublishBatchRequestEntry { Id = i.ToString(), Message = bodies[i] });

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
    }
}
