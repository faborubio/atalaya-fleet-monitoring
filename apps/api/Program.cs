using System.Threading.RateLimiting;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Atalaya.Api.Hubs;
using Atalaya.Api.Processing;
using Atalaya.Api.Services;
using Atalaya.Contracts;
using Atalaya.Persistence;
using Atalaya.Realtime;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

// Seguridad de ingesta (SAD §8): token de dispositivo + rate limiting.
var ingestToken = builder.Configuration["Ingest:Token"] ?? string.Empty;
var ingestRatePerSecond = builder.Configuration.GetValue("Ingest:RatePerSecond", 500);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("ingest", opt =>
    {
        opt.PermitLimit = ingestRatePerSecond;
        opt.Window = TimeSpan.FromSeconds(1);
        opt.QueueLimit = 0;
    });
});

// Transporte de ingesta: "InMemory" (tests / dev sin Docker) o "Aws" (SNS→SQS, ADR-001).
var transport = builder.Configuration["Telemetry:Transport"] ?? "InMemory";
var useAws = transport.Equals("Aws", StringComparison.OrdinalIgnoreCase);

if (useAws)
{
    var aws = builder.Configuration.GetSection("Aws").Get<AwsOptions>() ?? new AwsOptions();
    builder.Services.AddSingleton(aws);
    builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
        new AmazonSimpleNotificationServiceClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = aws.ServiceUrl,
                AuthenticationRegion = aws.Region,
            }));
    // Ingesta desacoplada (AUD-009): /ingest encola y responde 202; el publicador en
    // background drena el canal y manda a SNS por lotes (saca el round-trip del request).
    builder.Services.AddSingleton<QueueingTelemetryPublisher>();
    builder.Services.AddSingleton<ITelemetryPublisher>(sp =>
        sp.GetRequiredService<QueueingTelemetryPublisher>());
    builder.Services.AddHostedService<SnsBatchPublisher>();
    // El read model lo sirve Postgres (lo escribe el worker, ADR-005/008).
    builder.Services.AddAtalayaPersistence(builder.Configuration);
    // Push en vivo: el worker publica deltas/alertas en Redis; la API los reenvía por SignalR (ADR-002).
    builder.Services.AddAtalayaRedis(builder.Configuration);
    builder.Services.AddHostedService<RedisDeltaForwarder>();
    builder.Services.AddHostedService<RedisAlertForwarder>();
}
else
{
    // El procesamiento corre en proceso (ADR-008 lo mueve al worker en modo Aws).
    builder.Services.AddSingleton<IDeviceStateStore, InMemoryDeviceStateStore>();
    builder.Services.AddSingleton<IAlertStore, InMemoryAlertStore>();
    builder.Services.AddSingleton<ITelemetryArchive, InMemoryTelemetryArchive>();
    builder.Services.AddSingleton<ITelemetryBus, InMemoryTelemetryBus>();
    builder.Services.AddSingleton<ITelemetryPublisher, InMemoryTelemetryPublisher>();
    builder.Services.AddSingleton<IDeduplicator, InMemoryDeduplicator>();
    builder.Services.AddHostedService<TelemetryProcessor>();
}

const string DevCors = "atalaya-dev";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // requerido por SignalR (WebSocket con credenciales)

var app = builder.Build();

if (useAws)
{
    await app.Services.GetRequiredService<IDeviceStateRepository>().EnsureSchemaAsync();
    await app.Services.GetRequiredService<IAlertRepository>().EnsureSchemaAsync();
    await app.Services.GetRequiredService<ITelemetryArchive>().EnsureSchemaAsync();
}

app.UseCors(DevCors);
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok", transport, ts = DateTimeOffset.UtcNow }));

// Ingesta: NO escribe directo a la base. Publica para procesamiento async y responde 202 (ADR-001).
// Autenticación por token de dispositivo (si está configurado) + rate limiting (ADR §8).
app.MapPost("/ingest", async (
    HttpContext http, TelemetryEvent[] events, ITelemetryPublisher publisher, CancellationToken ct) =>
{
    if (!string.IsNullOrEmpty(ingestToken) &&
        http.Request.Headers["X-Ingest-Token"] != ingestToken)
        return Results.Unauthorized();

    await publisher.PublishAsync(events, ct);
    return Results.Accepted();
}).RequireRateLimiting("ingest");

// Snapshot del read model: lo que el dashboard pide al cargar (camino caliente, ADR-005).
if (useAws)
{
    app.MapGet("/api/devices", async (IDeviceStateRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetAllAsync(ct)));
    app.MapGet("/api/alerts", async (IAlertRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetRecentAsync(100, ct)));
}
else
{
    app.MapGet("/api/devices", (IDeviceStateStore store) => Results.Ok(store.Snapshot()));
    app.MapGet("/api/alerts", (IAlertStore store) => Results.Ok(store.Snapshot()));
}

// Camino frío (ADR-005/007): histórico por dispositivo desde la telemetría particionada.
// No compite con el camino caliente; lee del archivo, no de los read models en vivo.
app.MapGet("/api/history", async (
    string deviceId, ITelemetryArchive archive, CancellationToken ct,
    int minutes = 60, int limit = 1000) =>
{
    if (string.IsNullOrWhiteSpace(deviceId))
        return Results.BadRequest(new { error = "deviceId es obligatorio" });

    var to = DateTimeOffset.UtcNow;
    var from = to.AddMinutes(-Math.Clamp(minutes, 1, 24 * 60));
    var points = await archive.QueryAsync(deviceId, from, to, Math.Clamp(limit, 1, 5000), ct);
    return Results.Ok(points);
});

// Hub de deltas en vivo (ADR-002).
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();

// Expuesto para pruebas de integración (WebApplicationFactory).
public partial class Program;
