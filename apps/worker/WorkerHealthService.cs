using System.Net;
using Amazon.SQS;

namespace Atalaya.Worker;

/// <summary>
/// Endpoint de salud mínimo del worker (SAD Fase 3, AUD-015 E). Un Worker no expone HTTP por
/// defecto; aquí se levanta un <see cref="HttpListener"/> ligero con <c>/health/live</c> (proceso
/// vivo) y <c>/health/ready</c> (gateado por la accesibilidad de SQS) para readiness en
/// orquestadores. Best-effort: si no puede enlazar el puerto, registra y el worker sigue.
/// </summary>
public sealed class WorkerHealthService(
    IAmazonSQS sqs,
    AwsOptions options,
    IConfiguration config,
    ILogger<WorkerHealthService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = config.GetValue("Health:Port", 3100);
        var prefix = $"http://localhost:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo abrir el endpoint de salud en {Prefix}; el worker sigue.", prefix);
            return;
        }

        logger.LogInformation("Salud del worker en {Prefix}health/live y {Prefix}health/ready.", prefix, prefix);
        stoppingToken.Register(listener.Stop);

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Error en el endpoint de salud"); continue; }

            _ = HandleAsync(ctx, stoppingToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var (status, body) = path switch
        {
            "/health/ready" => await ReadyAsync(ct),
            _ => (200, "live"),
        };

        try
        {
            ctx.Response.StatusCode = status;
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        }
        catch { /* cliente desconectado */ }
        finally { ctx.Response.Close(); }
    }

    private async Task<(int, string)> ReadyAsync(CancellationToken ct)
    {
        try
        {
            await sqs.GetQueueUrlAsync(options.QueueName, ct);
            return (200, "ready");
        }
        catch
        {
            return (503, "sqs-unreachable");
        }
    }
}
