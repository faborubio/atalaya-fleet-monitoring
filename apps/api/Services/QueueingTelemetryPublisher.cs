using System.Threading.Channels;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Publicador de ingesta <b>desacoplado</b> (AUD-009): <c>/ingest</c> solo encola en un canal
/// en memoria y responde 202 al instante; un publicador en background (<see cref="SnsBatchPublisher"/>
/// para SNS o <see cref="GcpPubSubBatchPublisher"/> para Pub/Sub, ADR-013) drena el canal, coalesce
/// ráfagas y publica al broker por lotes. Así se saca el round-trip de red del camino de la petición
/// (era el cuello de botella medido: <c>PublishAsync</c> síncrono por request). El canal es
/// agnóstico al transporte; solo cambia quién lo drena.
/// <para>Canal <b>acotado</b> con espera (backpressure): si el publicador se atrasa, la
/// escritura espera en vez de perder eventos (igual que SQS amortigua picos, ADR-001).</para>
/// </summary>
public sealed class QueueingTelemetryPublisher : ITelemetryPublisher
{
    private readonly Channel<TelemetryEvent> _channel;

    public QueueingTelemetryPublisher(int queueCapacity)
    {
        _channel = Channel.CreateBounded<TelemetryEvent>(
            new BoundedChannelOptions(Math.Max(1_000, queueCapacity))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true, // un único drenador: el publicador en background
            });
    }

    public ChannelReader<TelemetryEvent> Reader => _channel.Reader;

    public async ValueTask PublishAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        for (var i = 0; i < events.Count; i++)
            await _channel.Writer.WriteAsync(events[i], ct);
    }
}
