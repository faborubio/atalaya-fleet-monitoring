using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Atalaya.Contracts;
using Atalaya.Persistence;

namespace Atalaya.Worker;

/// <summary>
/// Data lake en S3 (ADR-007): vuelca cada lote crudo como un objeto JSON inmutable bajo una
/// partición por fecha de ingesta <c>raw/yyyy/MM/dd/</c>. Es la fuente de verdad fría y la base de
/// Athena en AWS real. En dev corre contra LocalStack; el bucket lo crea el init de LocalStack,
/// pero <see cref="EnsureBucketAsync"/> lo asegura igual (idempotente).
/// </summary>
public sealed class S3RawEventArchive(IAmazonS3 s3, AwsOptions options) : IRawEventArchive
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = options.Bucket }, ct);
        }
        catch (AmazonS3Exception ex) when (
            ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Ya existe (lo crea el init de LocalStack / un arranque previo).
        }
    }

    public async Task AppendAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var key = $"raw/{now:yyyy/MM/dd}/{now:HHmmssfff}-{Guid.NewGuid():N}.json";
        var body = JsonSerializer.Serialize(events, Json);

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            ContentBody = body,
            ContentType = "application/json",
        }, ct);
    }
}
