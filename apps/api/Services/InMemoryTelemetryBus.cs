using System.Threading.Channels;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Implementación en memoria del <see cref="ITelemetryBus"/> para desarrollo sin Docker.
/// Canal acotado: si el consumo se atrasa, la escritura espera (backpressure), igual que
/// SQS amortigua los picos en la arquitectura objetivo (ADR-001).
/// </summary>
public sealed class InMemoryTelemetryBus : ITelemetryBus
{
    private readonly Channel<TelemetryEvent> _channel =
        Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(capacity: 100_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ChannelReader<TelemetryEvent> Reader => _channel.Reader;

    public async ValueTask PublishAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        for (var i = 0; i < events.Count; i++)
            await _channel.Writer.WriteAsync(events[i], ct);
    }
}
