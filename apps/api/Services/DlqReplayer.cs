using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Grpc.Core;

namespace Atalaya.Api.Services;

/// <summary>
/// Replay de la cola de mensajes muertos (DLQ, ADR-006). Cierra el ciclo de resiliencia: la DLQ ya
/// retenía los mensajes que agotaron los reintentos; esto los <b>re-encola</b> al topic principal para
/// reprocesarlos una vez resuelta la causa (p.ej. Postgres volvió). Acción de operación (RBAC admin).
/// </summary>
public interface IDlqReplayer
{
    /// <summary>Re-encola hasta <paramref name="max"/> mensajes de la DLQ al topic principal. Devuelve cuántos movió.</summary>
    Task<int> ReplayAsync(int max, CancellationToken ct = default);
}

/// <summary>
/// Implementación sobre Pub/Sub (ADR-013): hace pull de la suscripción de la DLQ, re-publica cada
/// mensaje crudo al topic principal (que el worker reprocesa) y <b>solo entonces</b> lo reconoce en la
/// DLQ. Ese orden (publicar→ack) garantiza que un fallo no pierde el dead-letter: queda sin ack y se
/// reentrega; el reproceso es seguro porque todo el pipeline es idempotente (dedup por EventId, clave
/// por hash de contenido, máquina de incidentes) — at-least-once (ADR-006).
/// </summary>
public sealed class PubSubDlqReplayer(
    PublisherServiceApiClient publisher,
    SubscriberServiceApiClient subscriber,
    GcpOptions options,
    ILogger<PubSubDlqReplayer> logger) : IDlqReplayer
{
    public async Task<int> ReplayAsync(int max, CancellationToken ct = default)
    {
        var dlqSub = SubscriptionName.FromProjectSubscription(
            options.ProjectId, options.DeadLetterSubscriptionId);
        var topic = TopicName.FromProjectTopic(options.ProjectId, options.TopicId);

        // Un ÚNICO pull (hasta 1000, el máximo de Pub/Sub) drena lo disponible: si hay dead-letters,
        // el long-poll los devuelve al instante; para vaciar colas de >1000 basta reinvocar el replay.
        // Se acota con un deadline en vez de ReturnImmediately: este último dispara reintentos cliente
        // que (1) sobre la DLQ vacía tardan ~20 s y (2) en carrera con el deadline devolvían un conteo
        // 0 erróneo aun habiendo mensajes (revisión crítica del replay). Con long-poll + deadline, el
        // camino con mensajes responde en ms y la DLQ vacía vence limpio en el deadline → 0.
        var request = new PullRequest
        {
            SubscriptionAsSubscriptionName = dlqSub,
            MaxMessages = Math.Clamp(max, 1, 1000),
        };
        var deadline = CallSettings.FromExpiration(Expiration.FromTimeout(TimeSpan.FromSeconds(5)));

        PullResponse pull;
        try
        {
            pull = await subscriber.PullAsync(request, deadline);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.DeadlineExceeded or StatusCode.Cancelled)
        {
            return 0; // DLQ vacía: el long-poll no recibió nada dentro del deadline
        }

        if (pull.ReceivedMessages.Count == 0) return 0; // DLQ vacía

        // Re-publica cada cuerpo crudo al topic principal (mismo formato que produjo la ingesta).
        await publisher.PublishAsync(
            topic, pull.ReceivedMessages.Select(m => new PubsubMessage { Data = m.Message.Data }), ct);

        // Ack en la DLQ SOLO tras re-publicar: si algo falla antes, el mensaje no se pierde (reentrega).
        await subscriber.AcknowledgeAsync(dlqSub, pull.ReceivedMessages.Select(m => m.AckId), ct);

        var replayed = pull.ReceivedMessages.Count;
        logger.LogInformation("DLQ replay: {N} mensajes re-encolados al topic principal", replayed);
        return replayed;
    }
}
