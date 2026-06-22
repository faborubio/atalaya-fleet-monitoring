using Atalaya.Persistence;

namespace Atalaya.Worker;

/// <summary>Configuración de retención del camino frío (sección "Retention").</summary>
public sealed class RetentionOptions
{
    /// <summary>Días de telemetría caliente que se conservan en SQL; el resto se dropea.</summary>
    public int Days { get; set; } = 30;
    /// <summary>Cada cuántas horas se ejecuta la limpieza.</summary>
    public int IntervalHours { get; set; } = 6;
}

/// <summary>
/// Retención O(1) del camino frío (ADR-007, AUD-015 p2): periódicamente elimina las particiones
/// diarias de <c>telemetry</c> anteriores a la ventana caliente con <c>DROP PARTITION</c> (no
/// <c>DELETE</c> masivos). El histórico completo permanece en el data lake S3. Corre en el worker
/// (dueño del camino frío) y hace una pasada al arrancar.
/// </summary>
public sealed class PartitionRetentionService(
    ITelemetryArchive archive,
    RetentionOptions options,
    ILogger<PartitionRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var days = Math.Max(1, options.Days);
        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        logger.LogInformation(
            "Retención de particiones activa: conserva {Days} días, revisa cada {Hours} h.",
            days, interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);
                var dropped = await archive.DropPartitionsBeforeAsync(cutoff, stoppingToken);
                if (dropped.Count > 0)
                    logger.LogInformation("Retención: {N} particiones eliminadas (< {Cutoff}): {Names}",
                        dropped.Count, cutoff, string.Join(", ", dropped));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo en la retención de particiones; se reintenta en la próxima ventana");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
