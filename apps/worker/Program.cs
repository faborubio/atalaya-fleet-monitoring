using Amazon.Runtime;
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

builder.Services.AddAtalayaPersistence(builder.Configuration);
builder.Services.AddAtalayaRedis(builder.Configuration);
builder.Services.AddHostedService<SqsTelemetryConsumer>();

var host = builder.Build();

// El worker es dueño de los read models: asegura los esquemas al arrancar.
await host.Services.GetRequiredService<IDeviceStateRepository>().EnsureSchemaAsync();
await host.Services.GetRequiredService<IAlertRepository>().EnsureSchemaAsync();

host.Run();
