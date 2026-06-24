using System.Net;
using System.Text;
using Atalaya.Contracts;
using Atalaya.Persistence;
using Google;
using Google.Cloud.Storage.V1;

namespace Atalaya.Worker;

/// <summary>
/// Data lake en Google Cloud Storage (ADR-013, fase G2), equivalente al <see cref="S3RawEventArchive"/>:
/// vuelca cada lote crudo como un objeto JSON inmutable bajo <c>raw/yyyy/MM/dd/{sha256}.json</c>. Reusa
/// <see cref="RawEventKey"/> (clave por hash de contenido, AUD-016) → una reentrega del mismo lote
/// sobrescribe el mismo objeto en vez de duplicarlo. Es la fuente de verdad fría y la base de
/// <b>BigQuery</b> en GCP real (fase G4). Contra el emulador (fake-gcs-server) cuesta $0.
/// </summary>
public sealed class GcsRawEventArchive(StorageClient storage, GcpOptions options) : IRawEventArchive
{
    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        try
        {
            await storage.CreateBucketAsync(options.ProjectId, options.Bucket, cancellationToken: ct);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Conflict)
        {
            // Ya existe (un arranque previo lo creó). Idempotente, igual que en S3.
        }
    }

    public async Task AppendAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;

        var (key, body) = RawEventKey.Build(events); // clave idempotente por hash de contenido
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        await storage.UploadObjectAsync(
            options.Bucket, key, "application/json", stream, cancellationToken: ct);
    }
}
