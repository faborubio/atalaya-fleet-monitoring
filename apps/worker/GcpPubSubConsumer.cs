using System.Text.Json;
using Atalaya.Contracts;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Grpc.Core;

namespace Atalaya.Worker;

/// <summary>
/// Consumidor del camino caliente para Google Cloud Pub/Sub (ADR-013), equivalente al
/// <see cref="SqsTelemetryConsumer"/>. Usa el <see cref="SubscriberClient"/> de alto nivel
/// (streaming pull con concurrencia, flow control y ack/nack gestionados). Cada mensaje es un array
/// JSON de eventos (mismo formato que SNS→SQS), que se delega al <see cref="TelemetryBatchProcessor"/>
/// común. Contra el emulador crea topic+suscripción al arrancar (idempotente); en la nube real los
/// crea Terraform (G5).
/// </summary>
public sealed class GcpPubSubConsumer(
    GcpOptions options,
    TelemetryBatchProcessor processor,
    WorkerMetrics metrics,
    ILogger<GcpPubSubConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = TopicName.FromProjectTopic(options.ProjectId, options.TopicId);
        var subscription = SubscriptionName.FromProjectSubscription(options.ProjectId, options.SubscriptionId);
        await EnsureTopologyAsync(topic, subscription, stoppingToken);

        var subscriber = await new SubscriberClientBuilder
        {
            SubscriptionName = subscription,
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync(stoppingToken);

        logger.LogInformation("Consumiendo Pub/Sub {Subscription}", subscription);

        // Para ordenadamente al apagar el host (cancelación cooperativa): un lote a medias no se
        // ack-ea → reentrega (at-least-once, ADR-006).
        stoppingToken.Register(() => subscriber.StopAsync(TimeSpan.FromSeconds(10)));

        await subscriber.StartAsync(async (msg, ct) =>
        {
            // Veneno vs transitorio (paridad con el redrive→DLQ de SQS): un cuerpo que no deserializa
            // NUNCA tendrá éxito; reintentarlo es un bucle infinito (y el emulador no honra la DLQ).
            // Se descarta (Ack) con log + métrica visible. En cambio, un fallo de proceso (BD/Redis
            // caído) sí es transitorio → Nack para reintentar; tras MaxDeliveryAttempts la
            // DeadLetterPolicy lo enruta a la DLQ en GCP real.
            TelemetryEvent[] batch;
            try
            {
                batch = JsonSerializer.Deserialize<TelemetryEvent[]>(msg.Data.ToStringUtf8(), Json) ?? [];
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Mensaje Pub/Sub envenenado (no deserializa); se descarta");
                metrics.AddPoison(1);
                return SubscriberClient.Reply.Ack;
            }

            try
            {
                await processor.ProcessAsync(batch, ct);
                return SubscriberClient.Reply.Ack;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo procesando lote Pub/Sub; nack para reintento (→DLQ tras {N})",
                    options.MaxDeliveryAttempts);
                return SubscriberClient.Reply.Nack;
            }
        });
    }

    private async Task EnsureTopologyAsync(TopicName topic, SubscriptionName subscription, CancellationToken ct)
    {
        if (!options.UsesEmulator) return; // nube real: la topología la crea Terraform (G5)

        // El emulador puede tardar en aceptar conexiones (handshake gRPC) al arrancar; reintenta ante
        // transitorios en vez de tirar el worker (BackgroundService → StopHost). Los errores de config
        // (InvalidArgument, etc.) no se reintentan: fallan rápido.
        const int maxAttempts = 15;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await CreateTopologyAsync(topic, subscription, ct);
                return;
            }
            catch (RpcException ex) when (attempt < maxAttempts && IsTransient(ex) && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Emulador Pub/Sub no listo (intento {A}/{Max}): {Detail}; reintenta",
                    attempt, maxAttempts, ex.Status.Detail);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    private static bool IsTransient(RpcException ex) => ex.StatusCode is
        StatusCode.Unavailable or StatusCode.Internal or StatusCode.DeadlineExceeded;

    private async Task CreateTopologyAsync(TopicName topic, SubscriptionName subscription, CancellationToken ct)
    {
        var dlqTopic = TopicName.FromProjectTopic(options.ProjectId, options.DeadLetterTopicId);

        var publisherApi = await new PublisherServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync(ct);
        foreach (var t in new[] { topic, dlqTopic })
        {
            try { await publisherApi.CreateTopicAsync(t, ct); }
            catch (RpcException e) when (e.StatusCode == StatusCode.AlreadyExists) { /* idempotente */ }
        }

        var subscriberApi = await new SubscriberServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync(ct);
        // Suscripción con DLQ (espeja el redrive de SQS): tras MaxDeliveryAttempts nacks, Pub/Sub
        // enruta el mensaje al topic DLQ. En GCP real exige IAM al service account de Pub/Sub
        // (publish en la DLQ + subscribe), que define Terraform (G5); el emulador acepta la config
        // aunque no siempre honre la entrega a la DLQ.
        try
        {
            await subscriberApi.CreateSubscriptionAsync(new Subscription
            {
                SubscriptionName = subscription,
                TopicAsTopicName = topic,
                AckDeadlineSeconds = 60,
                DeadLetterPolicy = new DeadLetterPolicy
                {
                    DeadLetterTopic = dlqTopic.ToString(),
                    MaxDeliveryAttempts = options.MaxDeliveryAttempts,
                },
            }, ct);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.AlreadyExists) { /* idempotente */ }

        // Suscripción sobre el topic DLQ: sin ella Pub/Sub no retiene los dead-letters y el replay
        // (ADR-006) no tendría qué leer. La lee el API en /api/admin/dlq/replay. En la nube la crea
        // Terraform; aquí (emulador) la aseguramos junto al resto de la topología.
        var dlqSubscription = SubscriptionName.FromProjectSubscription(
            options.ProjectId, options.DeadLetterSubscriptionId);
        try
        {
            await subscriberApi.CreateSubscriptionAsync(new Subscription
            {
                SubscriptionName = dlqSubscription,
                TopicAsTopicName = dlqTopic,
                AckDeadlineSeconds = 60,
            }, ct);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.AlreadyExists) { /* idempotente */ }

        logger.LogInformation(
            "Topología Pub/Sub asegurada en el emulador: {Topic} / {Subscription} (DLQ {Dlq} + sub {DlqSub}, {N} intentos)",
            topic, subscription, dlqTopic, dlqSubscription, options.MaxDeliveryAttempts);
    }
}
