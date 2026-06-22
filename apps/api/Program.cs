using Atalaya.Api.Hubs;
using Atalaya.Api.Processing;
using Atalaya.Api.Services;
using Atalaya.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

// Camino caliente (modo dev sin Docker): bus en memoria + dedup + read model.
builder.Services.AddSingleton<ITelemetryBus, InMemoryTelemetryBus>();
builder.Services.AddSingleton<IDeduplicator, InMemoryDeduplicator>();
builder.Services.AddSingleton<IDeviceStateStore, InMemoryDeviceStateStore>();
builder.Services.AddHostedService<TelemetryProcessor>();

const string DevCors = "atalaya-dev";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // requerido por SignalR (WebSocket con credenciales)

var app = builder.Build();

app.UseCors(DevCors);

app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow }));

// Ingesta: NO escribe directo a la base. Publica al bus y responde 202 (ADR-001).
app.MapPost("/ingest", async (TelemetryEvent[] events, ITelemetryBus bus, CancellationToken ct) =>
{
    await bus.PublishAsync(events, ct);
    return Results.Accepted();
});

// Snapshot del read model: lo que el dashboard pide al cargar (camino caliente, ADR-005).
app.MapGet("/api/devices", (IDeviceStateStore store) => Results.Ok(store.Snapshot()));

// Hub de deltas en vivo (ADR-002).
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();

// Expuesto para pruebas de integración (WebApplicationFactory).
public partial class Program;
