using System.Threading.Channels;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Publicador de ingesta <b>desacoplado</b> (AUD-009): <c>/ingest</c> solo encola en un canal
/// en memoria y responde 202 al instante; un <see cref="SnsBatchPublisher"/> drena el canal,
/// coalesce ráfagas y publica a SNS por lotes. Así se saca el round-trip a SNS del camino de
/// la petición (era el cuello de botella medido: <c>PublishAsync</c> síncrono por request).
/// <para>Canal <b>acotado</b> con espera (backpressure): si el publicador se atrasa, la
/// escritura espera en vez de perder eventos (igual que SQS amortigua picos, ADR-001).</para>
/// </summary>
public sealed class QueueingTelemetryPublisher : ITelemetryPublisher
{
    private readonly Channel<TelemetryEvent> _channel;

    public QueueingTelemetryPublisher(AwsOptions options)
    {
        _channel = Channel.CreateBounded<TelemetryEvent>(
            new BoundedChannelOptions(Math.Max(1_000, options.PublisherQueueCapacity))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true, // un único drenador: el SnsBatchPublisher
            });
    }

    public ChannelReader<TelemetryEvent> Reader => _channel.Reader;

    public async ValueTask PublishAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        for (var i = 0; i < events.Count; i++)
            await _channel.Writer.WriteAsync(events[i], ct);
    }
}
