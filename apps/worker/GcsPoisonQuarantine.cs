using Google.Cloud.Storage.V1;

namespace Atalaya.Worker;

/// <summary>
/// Cuarentena de veneno en Google Cloud Storage (AUDIT §6.13). Guarda el payload crudo bajo
/// <c>quarantine/yyyy/MM/dd/{timestamp}-{guid}.bin</c> en el bucket del data lake, con la razón y la
/// fecha en metadatos. Reusa el <see cref="StorageClient"/> del worker (modo Gcp). <b>Best-effort:</b>
/// si la subida falla, se loguea y se sigue — el mensaje igual se descarta (Ack), nunca reentra.
/// </summary>
public sealed class GcsPoisonQuarantine(
    StorageClient storage, GcpOptions options, ILogger<GcsPoisonQuarantine> logger) : IPoisonQuarantine
{
    public async Task QuarantineAsync(ReadOnlyMemory<byte> payload, string reason, CancellationToken ct = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var name = $"quarantine/{now:yyyy/MM/dd}/{now:HHmmssfff}-{Guid.NewGuid():N}.bin";
            await storage.UploadObjectAsync(
                options.Bucket, name, "application/octet-stream",
                new MemoryStream(payload.ToArray()),
                options: null, cancellationToken: ct);
            logger.LogWarning("Veneno puesto en cuarentena: gs://{Bucket}/{Name} (razón: {Reason})",
                options.Bucket, name, reason);
        }
        catch (Exception ex)
        {
            // No propagamos: el Ack del veneno no debe depender de que la cuarentena funcione.
            logger.LogError(ex, "No se pudo poner el veneno en cuarentena (se descarta igualmente)");
        }
    }
}
