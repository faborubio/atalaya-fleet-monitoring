using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Atalaya.Api.Hubs;
using Atalaya.Api.Processing;
using Atalaya.Api.Services;
using Atalaya.Contracts;
using Atalaya.Persistence;
using Atalaya.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

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
    builder.Services.AddSingleton<ITelemetryPublisher, SnsTelemetryPublisher>();
    // El read model lo sirve Postgres (lo escribe el worker, ADR-005/008).
    builder.Services.AddAtalayaPersistence(builder.Configuration);
    // Push en vivo: el worker publica deltas en Redis; la API los reenvía por SignalR (ADR-002).
    builder.Services.AddAtalayaRedis(builder.Configuration);
    builder.Services.AddHostedService<RedisDeltaForwarder>();
}
else
{
    // El procesamiento corre en proceso (ADR-008 lo mueve al worker en modo Aws).
    builder.Services.AddSingleton<IDeviceStateStore, InMemoryDeviceStateStore>();
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
    await app.Services.GetRequiredService<IDeviceStateRepository>().EnsureSchemaAsync();

app.UseCors(DevCors);

app.MapGet("/health", () => Results.Ok(new { status = "ok", transport, ts = DateTimeOffset.UtcNow }));

// Ingesta: NO escribe directo a la base. Publica para procesamiento async y responde 202 (ADR-001).
app.MapPost("/ingest", async (TelemetryEvent[] events, ITelemetryPublisher publisher, CancellationToken ct) =>
{
    await publisher.PublishAsync(events, ct);
    return Results.Accepted();
});

// Snapshot del read model: lo que el dashboard pide al cargar (camino caliente, ADR-005).
if (useAws)
    app.MapGet("/api/devices", async (IDeviceStateRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetAllAsync(ct)));
else
    app.MapGet("/api/devices", (IDeviceStateStore store) => Results.Ok(store.Snapshot()));

// Hub de deltas en vivo (ADR-002).
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();

// Expuesto para pruebas de integración (WebApplicationFactory).
public partial class Program;
