using System.Text.Json;
using System.Threading.Channels;
using Atalaya.Contracts;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;

namespace Atalaya.Api.Services;

/// <summary>
/// Equivalente Pub/Sub del <see cref="SnsBatchPublisher"/> (ADR-013): drena la cola del
/// <see cref="QueueingTelemetryPublisher"/> y publica a un topic de Pub/Sub por lotes. Cada mensaje
/// es un array JSON de ≤<c>MessageMaxEvents</c> eventos — el <b>mismo formato de cuerpo</b> que el
/// worker espera (idéntico a SNS con RawMessageDelivery), así el consumidor no distingue el broker.
/// Respeta los límites de Pub/Sub PublishRequest (≤1000 mensajes y ≤10 MB por llamada, con margen).
/// Contra el emulador, crea el topic al arrancar (idempotente); en la nube real lo crea Terraform (G5).
/// </summary>
public sealed class GcpPubSubBatchPublisher(
    QueueingTelemetryPublisher queue,
    PublisherServiceApiClient publisher,
    GcpOptions options,
    ILogger<GcpPubSubBatchPublisher> logger) : BackgroundService
{
    private const int MaxMessagesPerPublish = 1000;        // límite de Pub/Sub por PublishRequest
    private const int MaxPublishBytes = 9 * 1024 * 1024;   // 10 MB de Pub/Sub con margen

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = TopicName.FromProjectTopic(options.ProjectId, options.TopicId);
        await EnsureTopicAsync(topic, stoppingToken);

        var reader = queue.Reader;
        var maxEventsPerMessage = Math.Max(1, options.MessageMaxEvents);
        var flushMs = Math.Max(0, options.FlushMilliseconds);
        var pending = new List<TelemetryEvent>(maxEventsPerMessage * MaxMessagesPerPublish);

        logger.LogInformation(
            "Publicador Pub/Sub por lotes activo (topic {Topic}, {Evts} ev/msg, ventana {Ms} ms).",
            topic, maxEventsPerMessage, flushMs);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                Drain(reader, pending);
                if (flushMs > 0)
                {
                    await Task.Delay(flushMs, stoppingToken); // coalesce la ráfaga
                    Drain(reader, pending);
                }

                await FlushAsync(topic, pending, maxEventsPerMessage, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado ordenado (Fase 3): drena lo buffered antes de salir, acotado por tiempo.
            Drain(reader, pending);
            if (pending.Count > 0)
            {
                using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                logger.LogInformation("Apagado: drenando {N} eventos pendientes a Pub/Sub.", pending.Count);
                try { await FlushAsync(topic, pending, maxEventsPerMessage, drain.Token); }
                catch (OperationCanceledException) { logger.LogWarning("Drenado de cierre agotó el tiempo."); }
            }
        }
    }

    private static void Drain(ChannelReader<TelemetryEvent> reader, List<TelemetryEvent> buffer)
    {
        while (reader.TryRead(out var e)) buffer.Add(e);
    }

    private async Task FlushAsync(
        TopicName topic, List<TelemetryEvent> events, int maxEventsPerMessage, CancellationToken ct)
    {
        if (events.Count == 0) return;

        var messages = BuildMessages(events, maxEventsPerMessage);
        foreach (var chunk in Chunk(messages))
            await SendAsync(topic, chunk, ct);

        events.Clear();
    }

    /// <summary>
    /// Trocea los eventos en mensajes Pub/Sub (cada uno un array JSON de ≤<paramref name="maxEventsPerMessage"/>
    /// eventos), preservando el orden. Puro y sin dependencias de red para poder testearlo.
    /// </summary>
    internal static List<PubsubMessage> BuildMessages(
        IReadOnlyList<TelemetryEvent> events, int maxEventsPerMessage)
    {
        var messages = new List<PubsubMessage>((events.Count / Math.Max(1, maxEventsPerMessage)) + 1);
        for (var i = 0; i < events.Count; i += maxEventsPerMessage)
        {
            var count = Math.Min(maxEventsPerMessage, events.Count - i);
            var slice = new TelemetryEvent[count];
            for (var j = 0; j < count; j++) slice[j] = events[i + j];

            var body = JsonSerializer.Serialize(slice);
            messages.Add(new PubsubMessage { Data = ByteString.CopyFromUtf8(body) });
        }
        return messages;
    }

    /// <summary>Agrupa mensajes en lotes que respetan ≤1000 mensajes y ≤10 MB por PublishRequest.</summary>
    private static IEnumerable<List<PubsubMessage>> Chunk(List<PubsubMessage> messages)
    {
        var current = new List<PubsubMessage>(Math.Min(MaxMessagesPerPublish, messages.Count));
        var bytes = 0;
        foreach (var m in messages)
        {
            var size = m.Data.Length;
            if (current.Count > 0 &&
                (current.Count >= MaxMessagesPerPublish || bytes + size > MaxPublishBytes))
            {
                yield return current;
                current = new List<PubsubMessage>();
                bytes = 0;
            }
            current.Add(m);
            bytes += size;
        }
        if (current.Count > 0) yield return current;
    }

    private async Task SendAsync(TopicName topic, List<PubsubMessage> messages, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(topic, messages, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo publicando lote de {N} mensajes a Pub/Sub (se pierden)", messages.Count);
        }
    }

    private async Task EnsureTopicAsync(TopicName topic, CancellationToken ct)
    {
        if (!options.UsesEmulator) return; // nube real: la topología la crea Terraform (G5)
        try
        {
            await publisher.CreateTopicAsync(topic, ct);
            logger.LogInformation("Topic Pub/Sub creado en el emulador: {Topic}", topic);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.AlreadyExists) { /* idempotente */ }
    }
}
