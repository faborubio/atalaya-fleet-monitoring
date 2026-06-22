using Amazon.Runtime;
using Amazon.SQS;
using Atalaya.Persistence;
using Atalaya.Worker;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddHostedService<SqsTelemetryConsumer>();

var host = builder.Build();

// El worker es dueño del read model: asegura el esquema al arrancar.
await host.Services.GetRequiredService<IDeviceStateRepository>().EnsureSchemaAsync();

host.Run();
