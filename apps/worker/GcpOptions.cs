namespace Atalaya.Worker;

/// <summary>
/// Configuración de Google Cloud Pub/Sub para el worker (ADR-013). Sección "Gcp". En dev se apunta
/// al <b>emulador</b> (<c>EmulatorHost</c>, p.ej. <c>localhost:8085</c>); en la nube real se deja
/// vacío y el cliente usa las credenciales del entorno (ADC). El topic lo comparte con la API; la
/// suscripción de pull es propia del worker.
/// </summary>
public sealed class GcpOptions
{
    public string ProjectId { get; set; } = "atalaya-local";
    public string TopicId { get; set; } = "atalaya-telemetry";
    public string SubscriptionId { get; set; } = "atalaya-telemetry-sub";
    /// <summary>Topic de mensajes envenenados (DLQ), espeja la DLQ de SQS (redrive maxReceiveCount=5).</summary>
    public string DeadLetterTopicId { get; set; } = "atalaya-telemetry-dlq";
    /// <summary>Suscripción sobre el topic DLQ: retiene los dead-letters para que el replay (ADR-006)
    /// pueda leerlos y re-encolarlos. Sin suscripción, Pub/Sub no retiene los mensajes del topic.</summary>
    public string DeadLetterSubscriptionId { get; set; } = "atalaya-telemetry-dlq-sub";
    /// <summary>Intentos antes de enviar a la DLQ (paridad con SQS). Pub/Sub exige 5..100.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Host:puerto del emulador Pub/Sub. Vacío = Pub/Sub real (no auto-crea topología).</summary>
    public string EmulatorHost { get; set; } = string.Empty;

    /// <summary>Bucket del data lake en Cloud Storage (ADR-007), espeja el bucket S3.</summary>
    public string Bucket { get; set; } = "atalaya-datalake";
    /// <summary>URL del emulador GCS (fake-gcs-server), p.ej. <c>http://localhost:4443</c>. Vacío = GCS real.</summary>
    public string StorageEmulatorHost { get; set; } = string.Empty;

    public bool UsesEmulator => !string.IsNullOrWhiteSpace(EmulatorHost);
    public bool UsesStorageEmulator => !string.IsNullOrWhiteSpace(StorageEmulatorHost);
}
