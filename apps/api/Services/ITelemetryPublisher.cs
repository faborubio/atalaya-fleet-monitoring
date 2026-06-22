using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Punto de entrada de la ingesta: publica la telemetría para procesamiento asíncrono
/// (ADR-001). La implementación varía por transporte (in-memory en tests, SNS en dev/prod).
/// </summary>
public interface ITelemetryPublisher
{
    ValueTask PublishAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default);
}

/// <summary>Transporte in-memory: reenvía al canal local (modo dev sin Docker / tests).</summary>
public sealed class InMemoryTelemetryPublisher(ITelemetryBus bus) : ITelemetryPublisher
{
    public ValueTask PublishAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
        => bus.PublishAsync(events, ct);
}
