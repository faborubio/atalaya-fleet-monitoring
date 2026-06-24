using System.Threading.RateLimiting;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Atalaya.Api.Hubs;
using Atalaya.Api.Processing;
using Atalaya.Api.Services;
using Atalaya.Contracts;
using Atalaya.Persistence;
using Atalaya.Realtime;
using Google.Api.Gax;
using Google.Cloud.BigQuery.V2;
using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddHealthChecks(); // las comprobaciones de dependencias se añaden en modo Aws

// Registro de viewport (AUD-008): compartido por el hub y el forwarder/procesador.
builder.Services.AddSingleton<Atalaya.Api.Processing.ViewportRegistry>();

// Auth de lecturas (AUD-015 D, SAD §6.1): JWT Bearer con RBAC operador/admin. Flag Auth:Mode
// (Disabled/Dev/Oidc). En Disabled no registra nada (base/tests sin auth, como el token vacío).
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton(authOptions);
builder.Services.AddAtalayaAuth(authOptions);

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

// Transporte de ingesta: "InMemory" (tests / dev sin Docker), "Aws" (SNS→SQS, ADR-001) o
// "Gcp" (Pub/Sub, ADR-013). Aws y Gcp comparten el pipeline real (Postgres + Redis); solo
// difieren en el broker (publicador + readiness).
var transport = builder.Configuration["Telemetry:Transport"] ?? "InMemory";
var useAws = transport.Equals("Aws", StringComparison.OrdinalIgnoreCase);
var useGcp = transport.Equals("Gcp", StringComparison.OrdinalIgnoreCase);
var useBroker = useAws || useGcp;

if (useBroker)
{
    // Read model en Postgres (lo escribe el worker, ADR-005/008) + push en vivo por Redis (ADR-002).
    builder.Services.AddAtalayaPersistence(builder.Configuration);
    builder.Services.AddAtalayaRedis(builder.Configuration);
    builder.Services.AddHostedService<RedisDeltaForwarder>();
    builder.Services.AddHostedService<RedisAlertForwarder>();

    // Readiness gateada por dependencias (Fase 3): Postgres + Redis + broker.
    var healthChecks = builder.Services.AddHealthChecks()
        .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
        .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

    // Ingesta desacoplada (AUD-009): /ingest encola y responde 202; un publicador en background
    // drena el canal y publica al broker por lotes. El canal (QueueingTelemetryPublisher) es
    // agnóstico al transporte; solo cambian el publicador y el health check del broker.
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
        builder.Services.AddSingleton(_ => new QueueingTelemetryPublisher(aws.PublisherQueueCapacity));
        builder.Services.AddSingleton<ITelemetryPublisher>(sp =>
            sp.GetRequiredService<QueueingTelemetryPublisher>());
        builder.Services.AddHostedService<SnsBatchPublisher>();
        healthChecks.AddCheck<SnsHealthCheck>("sns", tags: ["ready"]);
    }
    else // useGcp (ADR-013): mismo diseño, Pub/Sub en vez de SNS
    {
        var gcp = builder.Configuration.GetSection("Gcp").Get<GcpOptions>() ?? new GcpOptions();
        builder.Services.AddSingleton(gcp);
        // El cliente honra PUBSUB_EMULATOR_HOST para apuntar al emulador (dev, costo $0).
        if (gcp.UsesEmulator)
            Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", gcp.EmulatorHost);
        builder.Services.AddSingleton(_ => new PublisherServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.Build());
        builder.Services.AddSingleton(_ => new QueueingTelemetryPublisher(gcp.PublisherQueueCapacity));
        builder.Services.AddSingleton<ITelemetryPublisher>(sp =>
            sp.GetRequiredService<QueueingTelemetryPublisher>());
        builder.Services.AddHostedService<GcpPubSubBatchPublisher>();
        healthChecks.AddCheck<PubSubHealthCheck>("pubsub", tags: ["ready"]);

        // Replay de la DLQ (ADR-006): lee la suscripción de la DLQ y re-encola al topic principal.
        builder.Services.AddSingleton(_ => new SubscriberServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.Build());
        builder.Services.AddSingleton<IDlqReplayer, PubSubDlqReplayer>();
    }
}
else
{
    // El procesamiento corre en proceso (ADR-008 lo mueve al worker en modo Aws).
    builder.Services.AddSingleton<IDeviceStateStore, InMemoryDeviceStateStore>();
    builder.Services.AddSingleton<IAlertIncidentStore, InMemoryAlertIncidentStore>();
    builder.Services.AddSingleton<ITelemetryArchive, InMemoryTelemetryArchive>();
    builder.Services.AddSingleton<ITelemetryBus, InMemoryTelemetryBus>();
    builder.Services.AddSingleton<ITelemetryPublisher, InMemoryTelemetryPublisher>();
    builder.Services.AddSingleton<IDeduplicator, InMemoryDeduplicator>();
    builder.Services.AddHostedService<TelemetryProcessor>();
}

// Analítica con BigQuery sobre el data lake (G4, ADR-013). Se registra solo si hay dataset
// configurado (Gcp:DatasetId) → ausente en base/tests (sin dependencia de BigQuery). El cliente usa
// ADC (en dev, GOOGLE_APPLICATION_CREDENTIALS apunta a la service account de consulta).
var gcpAnalytics = builder.Configuration.GetSection("Gcp").Get<GcpOptions>() ?? new GcpOptions();
if (gcpAnalytics.AnalyticsEnabled)
{
    builder.Services.AddSingleton<IAnalyticsQuery>(_ =>
        new BigQueryAnalyticsQuery(BigQueryClient.Create(gcpAnalytics.ProjectId), gcpAnalytics));
}

// Orígenes permitidos del dashboard. En dev = localhost:4200; en la nube (G5) la SPA vive en Firebase
// Hosting → se inyecta su dominio por config (`Cors:Origins`, lista). SignalR exige orígenes explícitos
// (no comodín) porque usa credenciales (WebSocket con cookies/token).
const string DevCors = "atalaya-dev";
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // requerido por SignalR (WebSocket con credenciales)

var app = builder.Build();

if (useBroker)
{
    await app.Services.GetRequiredService<IDeviceStateRepository>().EnsureSchemaAsync();
    await app.Services.GetRequiredService<IAlertIncidentStore>().EnsureSchemaAsync();
    await app.Services.GetRequiredService<ITelemetryArchive>().EnsureSchemaAsync();
}

app.UseCors(DevCors);
if (authOptions.IsEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseRateLimiter();

// Liveness: el proceso está vivo (no comprueba dependencias). Readiness: gateada por deps (Fase 3).
app.MapGet("/health", () => Results.Ok(new { status = "ok", transport, ts = DateTimeOffset.UtcNow }));
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

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

// Emisor de tokens de desarrollo (solo modo Auth:Dev): mintea un JWT HS256 con rol para que el
// dashboard demuestre la cadena de auth sin un IdP real. En prod este rol lo cumple Cognito.
if (authOptions.IsDev)
{
    app.MapGet("/auth/dev-token", (string? role, string? sub) =>
    {
        var requested = role ?? AuthExtensions.OperatorRole;
        if (requested != AuthExtensions.OperatorRole && requested != AuthExtensions.AdminRole)
            return Results.BadRequest(new { error = "role debe ser 'operador' o 'admin'" });

        var (token, expiresIn) = DevTokenIssuer.Issue(
            authOptions, sub ?? $"dev-{requested}", requested);
        return Results.Ok(new { token, role = requested, expiresIn });
    });
}

// Las lecturas exigen un usuario autenticado con rol operador/admin (read policy) cuando la auth
// está activa. Con Auth:Disabled (base/tests) quedan abiertas, igual que hoy.
RouteHandlerBuilder Secured(RouteHandlerBuilder b) =>
    authOptions.IsEnabled ? b.RequireAuthorization(AuthExtensions.ReadPolicy) : b;

// Snapshot del read model: lo que el dashboard pide al cargar (camino caliente, ADR-005).
if (useBroker)
{
    Secured(app.MapGet("/api/devices", async (IDeviceStateRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetAllAsync(ct))));
    Secured(app.MapGet("/api/alerts", async (IAlertIncidentStore store, CancellationToken ct) =>
        Results.Ok(await store.GetActiveAsync(100, ct))));
}
else
{
    Secured(app.MapGet("/api/devices", (IDeviceStateStore store) => Results.Ok(store.Snapshot())));
    Secured(app.MapGet("/api/alerts", async (IAlertIncidentStore store, CancellationToken ct) =>
        Results.Ok(await store.GetActiveAsync(100, ct))));
}

// Camino frío (ADR-005/007): histórico por dispositivo desde la telemetría particionada.
// No compite con el camino caliente; lee del archivo, no de los read models en vivo.
Secured(app.MapGet("/api/history", async (
    string deviceId, ITelemetryArchive archive, CancellationToken ct,
    int minutes = 60, int limit = 1000) =>
{
    if (string.IsNullOrWhiteSpace(deviceId))
        return Results.BadRequest(new { error = "deviceId es obligatorio" });

    var to = DateTimeOffset.UtcNow;
    var from = to.AddMinutes(-Math.Clamp(minutes, 1, 24 * 60));
    var points = await archive.QueryAsync(deviceId, from, to, Math.Clamp(limit, 1, 5000), ct);
    return Results.Ok(points);
}));

// Analítica (camino frío, ADR-005/007, fase G4): agregados por dispositivo desde el data lake vía
// BigQuery. Solo existe si hay dataset configurado; mismo RBAC de lectura que el resto de lecturas.
if (gcpAnalytics.AnalyticsEnabled)
{
    Secured(app.MapGet("/api/analytics/devices", async (
        IAnalyticsQuery analytics, CancellationToken ct, int minutes = 60, int limit = 50) =>
    {
        var from = DateTimeOffset.UtcNow.AddMinutes(-Math.Clamp(minutes, 1, 7 * 24 * 60));
        var rows = await analytics.DeviceAggregatesAsync(from, Math.Clamp(limit, 1, 500), ct);
        return Results.Ok(rows);
    }));
}

// Replay de la DLQ (ADR-006, acción de operación): re-encola los mensajes muertos al topic principal.
// Solo en modo Gcp (la DLQ es de Pub/Sub) y reservado a admin (RBAC). Idempotente del lado del worker.
if (useGcp)
{
    var replay = app.MapPost("/api/admin/dlq/replay", async (
        IDlqReplayer replayer, CancellationToken ct, int max = 100) =>
    {
        var replayed = await replayer.ReplayAsync(Math.Clamp(max, 1, 1000), ct);
        return Results.Ok(new { replayed });
    });
    if (authOptions.IsEnabled) replay.RequireAuthorization(AuthExtensions.AdminPolicy);
}

// Hub de deltas en vivo (ADR-002). En modo auth, exige la misma read policy (token por query string).
var telemetryHub = app.MapHub<TelemetryHub>("/hubs/telemetry");
if (authOptions.IsEnabled) telemetryHub.RequireAuthorization(AuthExtensions.ReadPolicy);

app.Run();

// Expuesto para pruebas de integración (WebApplicationFactory).
public partial class Program;
