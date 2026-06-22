using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Atalaya.Contracts;

namespace Atalaya.Api.Services;

/// <summary>
/// Publica el lote de telemetría como UN mensaje en SNS (array JSON). SNS hace fan-out a
/// SQS, que amortigua los picos; el worker consume y expande el lote (ADR-001).
/// El ARN del topic se resuelve y cachea en el primer uso (CreateTopic es idempotente).
/// </summary>
public sealed class SnsTelemetryPublisher(
    IAmazonSimpleNotificationService sns,
    AwsOptions options) : ITelemetryPublisher
{
    private string? _topicArn;

    public async ValueTask PublishAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;
        var arn = await ResolveTopicArnAsync(ct);
        var body = JsonSerializer.Serialize(events);
        await sns.PublishAsync(new PublishRequest { TopicArn = arn, Message = body }, ct);
    }

    private async ValueTask<string> ResolveTopicArnAsync(CancellationToken ct)
        => _topicArn ??= (await sns.CreateTopicAsync(options.TopicName, ct)).TopicArn;
}
