using System.Net;

namespace Atalaya.Worker;

/// <summary>
/// Endpoint de salud mínimo del worker (SAD Fase 3, AUD-015 E). Un Worker no expone HTTP por
/// defecto; aquí se levanta un <see cref="HttpListener"/> ligero con <c>/health/live</c> (proceso
/// vivo) y <c>/health/ready</c> (gateado por la accesibilidad del broker vía
/// <see cref="IWorkerReadiness"/>, agnóstico al proveedor) para readiness en orquestadores.
/// Best-effort: si no puede enlazar el puerto, registra y el worker sigue.
/// </summary>
public sealed class WorkerHealthService(
    IWorkerReadiness readiness,
    IConfiguration config,
    ILogger<WorkerHealthService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cloud Run (G5) inyecta PORT y exige escuchar en todas las interfaces (0.0.0.0:$PORT) para su
        // sonda de arranque. En local se conserva `localhost:Health:Port` (bindear a `+` en Windows
        // exigiría URL ACL/admin, rompería `nx serve`). En el contenedor Linux `+` no necesita ACL.
        var cloudRunPort = Environment.GetEnvironmentVariable("PORT");
        var prefix = !string.IsNullOrEmpty(cloudRunPort)
            ? $"http://+:{cloudRunPort}/"
            : $"http://localhost:{config.GetValue("Health:Port", 3100)}/";

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

    private async Task<(int, string)> ReadyAsync(CancellationToken ct) =>
        await readiness.IsReadyAsync(ct) ? (200, "ready") : (503, "broker-unreachable");
}
