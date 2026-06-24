using Amazon.SQS;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;

namespace Atalaya.Worker;

/// <summary>
/// Comprobación de readiness del broker, abstraída del proveedor (ADR-013): el worker está "ready"
/// si puede alcanzar su fuente de mensajes. La implementación concreta la elige el transporte.
/// </summary>
public interface IWorkerReadiness
{
    Task<bool> IsReadyAsync(CancellationToken ct);
}

/// <summary>Readiness en modo AWS: la cola SQS debe ser alcanzable.</summary>
public sealed class SqsReadiness(IAmazonSQS sqs, AwsOptions options) : IWorkerReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        try { await sqs.GetQueueUrlAsync(options.QueueName, ct); return true; }
        catch { return false; }
    }
}

/// <summary>Readiness en modo Gcp: el topic de Pub/Sub debe ser alcanzable.</summary>
public sealed class PubSubReadiness(GcpOptions options) : IWorkerReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        try
        {
            var api = await new PublisherServiceApiClientBuilder
            {
                EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
            }.BuildAsync(ct);
            await api.GetTopicAsync(TopicName.FromProjectTopic(options.ProjectId, options.TopicId), ct);
            return true;
        }
        catch { return false; }
    }
}
