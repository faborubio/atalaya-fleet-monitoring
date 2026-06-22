using Amazon.S3;
using Amazon.S3.Model;
using Atalaya.Contracts;
using Atalaya.Persistence;

namespace Atalaya.Worker;

/// <summary>
/// Data lake en S3 (ADR-007): vuelca cada lote crudo como un objeto JSON inmutable bajo una
/// partición por fecha <c>raw/yyyy/MM/dd/</c>. La clave se deriva del <b>hash del contenido</b>
/// (<see cref="RawEventKey"/>, AUD-015 p3): una reentrega del mismo lote sobrescribe el mismo
/// objeto en vez de duplicarlo. Es la fuente de verdad fría y la base de Athena en AWS real.
/// El bucket lo crea el init de LocalStack, pero <see cref="EnsureBucketAsync"/> lo asegura igual.
/// </summary>
public sealed class S3RawEventArchive(IAmazonS3 s3, AwsOptions options) : IRawEventArchive
{
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

        var (key, body) = RawEventKey.Build(events); // clave idempotente por hash de contenido

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            ContentBody = body,
            ContentType = "application/json",
        }, ct);
    }
}
