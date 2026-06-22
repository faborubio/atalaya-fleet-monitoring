using System.Threading.Channels;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Bus de telemetría que desacopla la ingesta del procesamiento (ADR-001).
/// <para><b>Modo dev (sin Docker):</b> <see cref="InMemoryTelemetryBus"/> usa un canal
/// en memoria como amortiguador. <b>Modo objetivo:</b> esta abstracción se reimplementa
/// sobre SNS→SQS sin tocar la ingesta ni el procesador.</para>
/// </summary>
public interface ITelemetryBus
{
    ValueTask PublishAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default);

    /// <summary>Lado de consumo. El procesador drena por lotes para coalescer ráfagas.</summary>
    ChannelReader<TelemetryEvent> Reader { get; }
}
