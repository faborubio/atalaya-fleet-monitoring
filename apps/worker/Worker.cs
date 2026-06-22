namespace Atalaya.Worker;

/// <summary>
/// Consumidor de telemetría del camino caliente (ADR-008). En la arquitectura objetivo
/// recibe lotes desde <b>SQS</b>, deduplica (ADR-006), actualiza los read models
/// (ADR-005) y publica deltas al hub SignalR vía backplane Redis (ADR-002).
/// <para><b>Estado:</b> esqueleto. El consumo real de SQS requiere LocalStack/Docker
/// (TROUBLESHOOTING TS-002). Mientras tanto, el procesamiento equivalente corre en la
/// API (modo dev). Cuando Docker esté disponible, la lógica de
/// <c>Atalaya.Api.Processing.TelemetryProcessor</c> se mueve aquí sobre un cliente SQS.</para>
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogWarning(
            "Worker en modo esqueleto: el consumo de SQS está pendiente de LocalStack/Docker (TS-002).");

        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO(Fase 1, post-Docker): ReceiveMessageAsync desde SQS → dedup → read models → SignalR.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
