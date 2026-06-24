using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Atalaya.Persistence;
using Atalaya.Realtime;
using Atalaya.Worker;
using OpenTelemetry.Metrics;

var builder = Host.CreateApplicationBuilder(args);

// Observabilidad (SAD §8): métricas OTel. Dev → consola; prod → OTLP a un colector.
builder.Services.AddSingleton<WorkerMetrics>();
builder.Services.AddOpenTelemetry().WithMetrics(m => m
    .AddMeter(WorkerMetrics.MeterName)
    .AddConsoleExporter((_, reader) =>
        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000));

// Read models + push (provider-agnósticos): Postgres y Redis son iguales en AWS y GCP.
builder.Services.AddAtalayaPersistence(builder.Configuration);
builder.Services.AddAtalayaRedis(builder.Configuration);
builder.Services.AddSingleton<TelemetryBatchProcessor>(); // lógica de lote común a todos los brokers
builder.Services.AddHostedService<WorkerHealthService>();  // health/live + health/ready (Fase 3)

// Retención del camino frío (AUD-015 p2): dropea particiones viejas de telemetry.
builder.Services.AddSingleton(
    builder.Configuration.GetSection("Retention").Get<RetentionOptions>() ?? new RetentionOptions());
builder.Services.AddHostedService<PartitionRetentionService>();

// Broker del camino caliente: "Aws" (SQS + data lake S3, ADR-001/007) o "Gcp" (Pub/Sub, ADR-013).
var transport = builder.Configuration["Telemetry:Transport"] ?? "Aws";
var useGcp = transport.Equals("Gcp", StringComparison.OrdinalIgnoreCase);

if (useGcp)
{
    var gcp = builder.Configuration.GetSection("Gcp").Get<GcpOptions>() ?? new GcpOptions();
    builder.Services.AddSingleton(gcp);
    // El cliente honra PUBSUB_EMULATOR_HOST para apuntar al emulador (dev, costo $0).
    if (gcp.UsesEmulator)
        Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", gcp.EmulatorHost);
    // El data lake GCS llega en G2; por ahora el camino frío crudo es no-op en modo Gcp.
    builder.Services.AddSingleton<IRawEventArchive, NullRawEventArchive>();
    builder.Services.AddSingleton<IWorkerReadiness, PubSubReadiness>();
    builder.Services.AddHostedService<GcpPubSubConsumer>();
}
else
{
    var aws = builder.Configuration.GetSection("Aws").Get<AwsOptions>() ?? new AwsOptions();
    builder.Services.AddSingleton(aws);

    builder.Services.AddSingleton<IAmazonSQS>(_ =>
        new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig
            {
                ServiceURL = aws.ServiceUrl,
                AuthenticationRegion = aws.Region,
            }));

    // Cliente S3 del data lake (ADR-007). ForcePathStyle: LocalStack usa path-style, no v-host.
    builder.Services.AddSingleton<IAmazonS3>(_ =>
        new AmazonS3Client(
            new BasicAWSCredentials("test", "test"),
            new AmazonS3Config
            {
                ServiceURL = aws.ServiceUrl,
                AuthenticationRegion = aws.Region,
                ForcePathStyle = true,
            }));
    builder.Services.AddSingleton<IRawEventArchive, S3RawEventArchive>();
    builder.Services.AddSingleton<IWorkerReadiness, SqsReadiness>();
    builder.Services.AddHostedService<SqsTelemetryConsumer>();
}

var host = builder.Build();

// El worker es dueño de los read models y del camino frío: asegura esquemas y bucket al arrancar.
await host.Services.GetRequiredService<IDeviceStateRepository>().EnsureSchemaAsync();
await host.Services.GetRequiredService<IAlertIncidentStore>().EnsureSchemaAsync();
await host.Services.GetRequiredService<ITelemetryArchive>().EnsureSchemaAsync();
await host.Services.GetRequiredService<IRawEventArchive>().EnsureBucketAsync();

host.Run();
