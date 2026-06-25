using Atalaya.Api.Services;

namespace Atalaya.Api.Processing;

/// <summary>
/// Generador de telemetría server-side para la demo de portafolio (ADR-014, ver DEMO.md). Con
/// <c>Demo:Enabled=true</c> empuja la <see cref="DemoFleet"/> por el mismo <see cref="ITelemetryPublisher"/>
/// que <c>/ingest</c> → reusa el pipeline InMemory completo (bus → <see cref="TelemetryProcessor"/> →
/// SignalR), sin simulador local. En Cloud Run con scale-to-zero la CPU solo está asignada mientras hay
/// un request en vuelo (el WebSocket del dashboard), así que el generador <b>solo produce mientras
/// alguien mira</b> y el servicio escala a cero al quedarse sin tráfico ⇒ costo ~$0 en reposo.
/// </summary>
public sealed class DemoTelemetryGenerator(
    ITelemetryPublisher publisher, DemoOptions options, ILogger<DemoTelemetryGenerator> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var fleet = new DemoFleet(options);
        logger.LogInformation(
            "DemoTelemetryGenerator iniciado: {Devices} dispositivos cada {Interval} ms (demo de portafolio).",
            fleet.Count, options.IntervalMs);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(100, options.IntervalMs)));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await publisher.PublishAsync(fleet.Step(), ct);
        }
        catch (OperationCanceledException)
        {
            // Apagado normal (graceful shutdown).
        }
    }
}
